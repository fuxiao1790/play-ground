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
            in AoeIdentityComponent identity,
            in CombatKinematicsComponent kinematics,
            in CombatCollisionComponent collision,
            in AoeHitSpawnComponent hitSpawn,
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
            bool hasHitWriter,
            NativeQueue<VfxPendingSpawn>.ParallelWriter vfxPendingWriter,
            bool hasVfxWriter,
            NativeQueue<ProjectileSpawnEvent>.ParallelWriter projectileEventWriter,
            NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter impactAoeEventWriter,
            NativeQueue<LingeringAoeSpawnEvent>.ParallelWriter lingeringAoeEventWriter,
            bool hasImpactAoeEventWriter,
            bool hasLingeringAoeEventWriter)
        {
            if (identity.Faction == CombatFaction.None)
            {
                Deactivate(active, collisionActive, arming);
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
                            area,
                            targetEntity,
                            targetPosition,
                            targetKey,
                            vfxPendingWriter,
                            hasVfxWriter,
                            ref hitVfxEmitted,
                            hitWriter,
                            hasHitWriter,
                            projectileEventWriter,
                            impactAoeEventWriter,
                            lingeringAoeEventWriter,
                            hasImpactAoeEventWriter,
                            hasLingeringAoeEventWriter);

                        if (--remaining == 0)
                            break;
                    }
                    while (occupiedTargetCells.TryGetNextValue(out i, ref it));
                }
            }

            if (deactivateAfterPass)
                Deactivate(active, collisionActive, arming);
        }

        internal static void EmitHit(
            AoeIdentityComponent identity,
            CombatKinematicsComponent kinematics,
            AoeHitSpawnComponent hitSpawn,
            AoeAreaComponent area,
            Entity targetEntity,
            TargetPosition targetPosition,
            int targetKey,
            NativeQueue<VfxPendingSpawn>.ParallelWriter vfxPending,
            bool hasVfxWriter,
            ref bool hitVfxEmitted,
            NativeQueue<CombatHitEvent>.ParallelWriter hitWriter,
            bool hasHitWriter,
            NativeQueue<ProjectileSpawnEvent>.ParallelWriter projectileEventWriter,
            NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter impactAoeEventWriter,
            NativeQueue<LingeringAoeSpawnEvent>.ParallelWriter lingeringAoeEventWriter,
            bool hasImpactAoeEventWriter,
            bool hasLingeringAoeEventWriter)
        {
            if (hasHitWriter && HasHitEvent(hitSpawn))
            {
                hitWriter.Enqueue(new CombatHitEvent
                {
                    TargetProxy = targetEntity,
                    HitPosition = kinematics.Position,
                    Kind = CombatHitKind.Aoe,
                    DamageAmount = hitSpawn.HitPayload.DamageAmount,
                    CritChance = hitSpawn.HitPayload.CritChance,
                    CritMultiplier = hitSpawn.HitPayload.CritMultiplier,
                    DirectDamageEnabled = hitSpawn.HitPayload.DirectDamageEnabled,
                    SourceNodeId = hitSpawn.HitPayload.SourceNodeId,
                    SourceId = identity.AoeId,
                    TypeId = identity.TypeId,
                    StackEffect = hitSpawn.HitPayload.StackEffect
                });
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
                    if (hasLingeringAoeEventWriter)
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
                }
                else if (hasImpactAoeEventWriter)
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

            if (!hitVfxEmitted && hasVfxWriter)
            {
                vfxPending.Enqueue(new VfxPendingSpawn
                {
                    TypeId = identity.TypeId,
                    Trigger = 1,
                    Position = kinematics.Position,
                    AreaSize = area.Size
                });
                hitVfxEmitted = true;
            }
        }

        internal static void Deactivate(
            EnabledRefRW<Active> active,
            EnabledRefRW<CombatCollisionActiveTag> collisionActive,
            EnabledRefRW<ArmingTag> arming)
        {
            CombatDeathUtility.Kill(active, collisionActive, arming);
        }

        internal static bool HasHitEvent(in AoeHitSpawnComponent hitSpawn) =>
            hitSpawn.HitPayload.DirectDamageEnabled || hitSpawn.HitPayload.StackEffect.Enabled;

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
