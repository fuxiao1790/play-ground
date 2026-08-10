using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Collision.Narrowphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Targeted;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Vfx;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Aoes
{
    internal static class AoeCollisionCore
    {
        private const int ImpactAoeIdSalt = 0x5F1A0E;
        private const int ProjectileBurstIdSalt = 0x7AB025;

        internal static void RunCollision(
            Entity sourceEntity,
            in AoeIdentityComponent identity,
            in CombatHitPayload payload,
            in CombatKinematicsComponent kinematics,
            in CombatCollisionComponent collision,
            in AoeHitSpawnComponent hitSpawn,
            in TimedSpawnComponent timedSpawn,
            in AoeVfxIds vfxIds,
            in VfxTimingData timing,
            in AoeAreaComponent area,
            bool deactivateAfterPass,
            EnabledRefRW<Active> active,
            EnabledRefRW<CombatCollisionActiveTag> collisionActive,
            EnabledRefRW<ArmingTag> arming,
            NativeArray<Entity> targetEntities,
            NativeArray<TargetPosition> targetPositions,
            NativeArray<TargetCollisionShape> targetShapes,
            NativeArray<TargetFaction> targetFactions,
            NativeParallelMultiHashMap<long, int> occupiedTargetCells,
            NativeQueue<CombatHitEvent>.ParallelWriter hitWriter,
            NativeQueue<CircularVfxSpawnRequest>.ParallelWriter circularVfxPendingWriter,
            NativeQueue<TimedCircularVfxSpawnRequest>.ParallelWriter timedCircularVfxPendingWriter,
            NativeQueue<ProjectileSpawnEvent>.ParallelWriter projectileEventWriter,
            NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter impactAoeEventWriter,
            NativeQueue<LingeringAoeSpawnEvent>.ParallelWriter lingeringAoeEventWriter,
            NativeQueue<TargetedSpawnEvent>.ParallelWriter targetedEventWriter,
            NativeQueue<SpawnTemplateRefDelta>.ParallelWriter spawnTemplateDeltas)
        {
            if (identity.Faction == CombatFaction.None)
            {
                Deactivate(
                    active, collisionActive, arming, in hitSpawn, in timedSpawn, in payload, spawnTemplateDeltas);
                return;
            }

            // Bounded, allocation-free broadphase: walk cells inline, narrow-phase
            // each candidate, de-dup within this pass, stop at the hard cap.
            // Overflow keeps first-N in cell-scan order, not nearest-N.
            int remaining = CollisionConstants.MaxAoeTargetsPerTick;
            bool hitVfxEmitted = false;
            FixedList512Bytes<int> seen = default;

            int2 min = CombatSpatialHash.MinCell(collision.BoundsMin, CombatSpatialHash.AoeCellSize);
            int2 max = CombatSpatialHash.MaxCell(collision.BoundsMax, CombatSpatialHash.AoeCellSize);
            for (int cy = min.y; cy <= max.y && remaining > 0; cy++)
            {
                for (int cx = min.x; cx <= max.x && remaining > 0; cx++)
                {
                    long key = CombatSpatialHash.CellKey(cx, cy);
                    if (!occupiedTargetCells.TryGetFirstValue(
                            key,
                            out int i,
                            out NativeParallelMultiHashMapIterator<long> it))
                        continue;

                    do
                    {
                        if (targetFactions[i].Value == identity.Faction)
                            continue;

                        Entity targetEntity = targetEntities[i];
                        int targetKey = TargetKey(targetEntity);

                        if (seen.IndexOf(targetKey) >= 0)
                            continue;

                        TargetPosition targetPosition = targetPositions[i];
                        TargetCollisionShape target = targetShapes[i];

                        if (!CombatCollisionMath.BoundsIntersect(
                                collision.BoundsMin,
                                collision.BoundsMax,
                                target.BoundsMin,
                                target.BoundsMax))
                            continue;

                        if (!CombatCollisionMath.Hit(
                                kinematics.Position,
                                collision.Radius,
                                collision.HalfExtents,
                                collision.RotationRadians,
                                collision.ShapeType,
                                targetPosition.Value,
                                target.Radius,
                                target.HalfExtents,
                                target.RotationRadians,
                                target.ShapeType))
                            continue;

                        seen.Add(targetKey);
                        EmitHit(
                            identity,
                            kinematics,
                            hitSpawn,
                            vfxIds,
                            timing,
                            area,
                            targetEntity,
                            targetPosition,
                            targetKey,
                            circularVfxPendingWriter,
                            timedCircularVfxPendingWriter,
                            ref hitVfxEmitted,
                            hitWriter,
                            sourceEntity,
                            in payload,
                            projectileEventWriter,
                            impactAoeEventWriter,
                            lingeringAoeEventWriter,
                            targetedEventWriter);

                        if (--remaining == 0)
                            break;
                    }
                    while (occupiedTargetCells.TryGetNextValue(out i, ref it));
                }
            }

            if (deactivateAfterPass)
                Deactivate(
                    active, collisionActive, arming, in hitSpawn, in timedSpawn, in payload, spawnTemplateDeltas);
        }

        internal static void EmitHit(
            AoeIdentityComponent identity,
            CombatKinematicsComponent kinematics,
            AoeHitSpawnComponent hitSpawn,
            AoeVfxIds vfxIds,
            VfxTimingData timing,
            AoeAreaComponent area,
            Entity targetEntity,
            TargetPosition targetPosition,
            int targetKey,
            NativeQueue<CircularVfxSpawnRequest>.ParallelWriter circularVfxPending,
            NativeQueue<TimedCircularVfxSpawnRequest>.ParallelWriter timedCircularVfxPending,
            ref bool hitVfxEmitted,
            NativeQueue<CombatHitEvent>.ParallelWriter hitWriter,
            Entity sourceEntity,
            in CombatHitPayload payload,
            NativeQueue<ProjectileSpawnEvent>.ParallelWriter projectileEventWriter,
            NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter impactAoeEventWriter,
            NativeQueue<LingeringAoeSpawnEvent>.ParallelWriter lingeringAoeEventWriter,
            NativeQueue<TargetedSpawnEvent>.ParallelWriter targetedEventWriter)
        {
            if (HasHitEvent(payload))
            {
                hitWriter.Enqueue(new CombatHitEvent
                {
                    Source = sourceEntity,
                    Target = targetEntity
                });
            }

            if (hitSpawn.OnHitSpawn.Enabled
                && hitSpawn.OnHitSpawn.Kind == IntervalChildKind.Targeted)
            {
                TargetedSpawnEmission.Enqueue(
                    identity.AoeId,
                    identity.TypeId,
                    identity.Faction,
                    targetPosition.Value,
                    targetKey,
                    hitSpawn.OnHitSpawn.Kind,
                    hitSpawn.OnHitSpawn.TemplateKey,
                    targetedEventWriter);
            }

            if (hitSpawn.OnHitSpawn.Enabled
                && hitSpawn.OnHitSpawn.Kind != IntervalChildKind.Projectile
                && hitSpawn.OnHitSpawn.Kind != IntervalChildKind.ImpactAoe
                && hitSpawn.OnHitSpawn.Kind != IntervalChildKind.LingeringAoe
                && hitSpawn.OnHitSpawn.Kind != IntervalChildKind.Targeted)
            {
                throw new global::System.InvalidOperationException(
                    "Unhandled interval child kind.");
            }

            if (hitSpawn.OnHitSpawn.Enabled
                && hitSpawn.OnHitSpawn.Kind == IntervalChildKind.Projectile)
            {
                int baseId = HashId(identity.AoeId, identity.TypeId, targetKey, ProjectileBurstIdSalt);
                projectileEventWriter.Enqueue(new ProjectileSpawnEvent
                {
                    Kind = hitSpawn.OnHitSpawn.Kind,
                    TemplateKey = hitSpawn.OnHitSpawn.TemplateKey,
                    Faction = identity.Faction,
                    Position = targetPosition.Value,
                    AimDirection = DirectionFromTo(targetPosition.Value, kinematics.Position),
                    SourceId = baseId,
                    JitterSeed = (uint)baseId * 2654435761u,
                    ContactGateSeedTargetId = targetKey
                });
            }

            if (hitSpawn.OnHitSpawn.Enabled
                && (hitSpawn.OnHitSpawn.Kind == IntervalChildKind.ImpactAoe
                    || hitSpawn.OnHitSpawn.Kind == IntervalChildKind.LingeringAoe))
            {
                int aoeId = HashId(identity.AoeId, identity.TypeId, targetKey, ImpactAoeIdSalt ^ 0x13579B);
                if (hitSpawn.OnHitSpawn.Kind == IntervalChildKind.LingeringAoe)
                {
                    lingeringAoeEventWriter.Enqueue(new LingeringAoeSpawnEvent
                    {
                        Kind = hitSpawn.OnHitSpawn.Kind,
                        TemplateKey = hitSpawn.OnHitSpawn.TemplateKey,
                        Faction = identity.Faction,
                        Position = targetPosition.Value,
                        SourceId = aoeId,
                        JitterSeed = (uint)aoeId * 2654435761u,
                        ContactGateSeedTargetId = targetKey
                    });
                }
                else
                {
                    impactAoeEventWriter.Enqueue(new ImpactAoeSpawnEvent
                    {
                        Kind = hitSpawn.OnHitSpawn.Kind,
                        TemplateKey = hitSpawn.OnHitSpawn.TemplateKey,
                        Faction = identity.Faction,
                        Position = targetPosition.Value,
                        SourceId = aoeId,
                        JitterSeed = (uint)aoeId * 2654435761u,
                        ContactGateSeedTargetId = targetKey
                    });
                }
            }

            if (!hitVfxEmitted && vfxIds.HitId > 0)
            {
                VfxEmit.Enqueue(
                    vfxIds.HitId,
                    kinematics.Position,
                    area.Size,
                    timing,
                    circularVfxPending,
                    timedCircularVfxPending);
                hitVfxEmitted = true;
            }
        }

        // Single AOE death funnel for the impact and lingering lanes. Despawn emits a release
        // event for every template key the entity carries; it never touches a reference count.
        // Impact archetypes have no TimedSpawnComponent and pass default, which releases nothing.
        internal static void Deactivate(
            EnabledRefRW<Active> active,
            EnabledRefRW<CombatCollisionActiveTag> collisionActive,
            EnabledRefRW<ArmingTag> arming,
            in AoeHitSpawnComponent hitSpawn,
            in TimedSpawnComponent timedSpawn,
            in CombatHitPayload payload,
            NativeQueue<SpawnTemplateRefDelta>.ParallelWriter deltas)
        {
            CombatDeathUtility.Kill(active, collisionActive, arming);
            SpawnTemplateRefEmit.ReleaseAoe(in hitSpawn, in timedSpawn, in payload, deltas);
        }

        internal static bool HasHitEvent(in CombatHitPayload payload) =>
            payload.DirectDamageEnabled || payload.StackEffect.Enabled;

        internal static int TargetKey(Entity entity)
        {
            unchecked
            {
                int key = ((entity.Index + 1) * 397) ^ entity.Version;
                key &= 0x7fffffff;
                return key == 0 ? 1 : key;
            }
        }

        private static float2 DirectionFromTo(float2 from, float2 to)
        {
            float2 toTarget = to - from;
            if (math.lengthsq(toTarget) <= 0.0001f)
            {
                return new float2(1f, 0f);
            }

            return math.normalize(toTarget);
        }

        private static int HashId(int a, int b, int c, int salt)
        {
            unchecked
            {
                int hash = salt;
                hash = (hash * 397) ^ a;
                hash = (hash * 397) ^ b;
                hash = (hash * 397) ^ c;
                hash &= int.MaxValue;
                return hash == 0 ? 1 : hash;
            }
        }
    }
}
