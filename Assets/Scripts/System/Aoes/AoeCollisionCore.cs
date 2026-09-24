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
        internal static void RunCollision(
            Entity sourceEntity,
            in AoeIdentityComponent identity,
            in CombatHitPayload payload,
            in CombatKinematicsComponent kinematics,
            in CombatCollisionComponent collision,
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
            NativeArray<int> seenTargetKeys,
            int seenTargetKeysStart,
            NativeQueue<CombatHitEvent>.ParallelWriter hitWriter,
            NativeQueue<ImpactCircleVfxEvent>.ParallelWriter circularVfxPendingWriter,
            NativeQueue<LingeringCircleVfxEvent>.ParallelWriter timedCircularVfxPendingWriter,
            NativeQueue<SpawnTemplateRefDelta>.ParallelWriter spawnTemplateDeltas)
        {
            if (identity.Faction == CombatFaction.None)
            {
                Deactivate(
                    active, collisionActive, arming, in timedSpawn, in payload, spawnTemplateDeltas);
                return;
            }

            // Bounded broadphase: walk cells inline, narrow-phase each candidate,
            // de-dup through caller-owned chunk scratch, stop at the hard cap.
            // Overflow keeps first-N in cell-scan order, not nearest-N.
            int remaining = CollisionConstants.MaxAoeTargetsPerTick;
            bool hitVfxEmitted = false;
            int seenTargetKeyCount = 0;

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
                        TargetFaction candidateFaction = targetFactions[i];
                        if (!TargetFaction.CanHit(identity.Faction, in candidateFaction))
                            continue;

                        Entity targetEntity = targetEntities[i];
                        int targetKey = TargetKey(targetEntity);

                        if (ContainsSeenTargetKey(
                                seenTargetKeys,
                                seenTargetKeysStart,
                                seenTargetKeyCount,
                                targetKey))
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

                        seenTargetKeys[seenTargetKeysStart + seenTargetKeyCount++] = targetKey;
                        EnqueueHitEvent(hitWriter, sourceEntity, targetEntity, in payload);
                        EnqueueHitVfx(
                            vfxIds,
                            kinematics,
                            timing,
                            area,
                            circularVfxPendingWriter,
                            timedCircularVfxPendingWriter,
                            ref hitVfxEmitted);

                        if (--remaining == 0)
                            break;
                    }
                    while (occupiedTargetCells.TryGetNextValue(out i, ref it));
                }
            }

            if (deactivateAfterPass)
                Deactivate(
                    active, collisionActive, arming, in timedSpawn, in payload, spawnTemplateDeltas);
        }

        internal static void EnqueueHitEvent(
            NativeQueue<CombatHitEvent>.ParallelWriter hitWriter,
            Entity sourceEntity,
            Entity targetEntity,
            in CombatHitPayload payload)
        {
            if (HasHitEvent(payload))
            {
                hitWriter.Enqueue(new CombatHitEvent
                {
                    Source = sourceEntity,
                    Target = targetEntity
                });
            }
        }

        internal static void EnqueueHitVfx(
            AoeVfxIds vfxIds,
            CombatKinematicsComponent kinematics,
            VfxTimingData timing,
            AoeAreaComponent area,
            NativeQueue<ImpactCircleVfxEvent>.ParallelWriter circularVfxPending,
            NativeQueue<LingeringCircleVfxEvent>.ParallelWriter timedCircularVfxPending,
            ref bool hitVfxEmitted)
        {
            if (hitVfxEmitted || vfxIds.HitId <= 0)
            {
                return;
            }

            VfxEmit.Enqueue(
                vfxIds.HitId,
                kinematics.Position,
                area.Size,
                timing,
                circularVfxPending,
                timedCircularVfxPending);
            hitVfxEmitted = true;
        }

        // Single AOE death funnel for the impact and lingering lanes. Despawn emits a release
        // event for every template key the entity carries; it never touches a reference count.
        // Impact archetypes have no TimedSpawnComponent and pass default, which releases nothing.
        internal static void Deactivate(
            EnabledRefRW<Active> active,
            EnabledRefRW<CombatCollisionActiveTag> collisionActive,
            EnabledRefRW<ArmingTag> arming,
            in TimedSpawnComponent timedSpawn,
            in CombatHitPayload payload,
            NativeQueue<SpawnTemplateRefDelta>.ParallelWriter deltas)
        {
            CombatDeathUtility.Kill(active, collisionActive, arming);
            SpawnTemplateRefEmit.ReleaseAoe(in timedSpawn, in payload, deltas);
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

        private static bool ContainsSeenTargetKey(
            NativeArray<int> seenTargetKeys,
            int start,
            int count,
            int targetKey)
        {
            int end = start + count;
            for (int i = start; i < end; i++)
            {
                if (seenTargetKeys[i] == targetKey)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
