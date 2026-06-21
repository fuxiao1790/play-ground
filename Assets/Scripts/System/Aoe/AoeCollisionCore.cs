using PlayGround.System.Common;
using PlayGround.System.Projectile;
using PlayGround.System.Vfx;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Aoe
{
    internal interface IContactGate
    {
        int IndexOf(int targetKey);
        void Add(int targetKey, float cooldown);
    }

    internal struct BufferGate : IContactGate
    {
        public DynamicBuffer<AoeContactGateElement> ContactGates;

        public int IndexOf(int targetKey)
        {
            for (int i = 0; i < ContactGates.Length; i++)
            {
                if (ContactGates[i].TargetId == targetKey)
                    return i;
            }
            return -1;
        }

        public void Add(int targetKey, float cooldown)
        {
            ContactGates.Add(new AoeContactGateElement
            {
                TargetId = targetKey,
                CooldownRemaining = cooldown
            });
        }
    }

    internal struct ScratchGate : IContactGate
    {
        public FixedList512Bytes<int> Seen;

        public int IndexOf(int targetKey)
        {
            for (int i = 0; i < Seen.Length; i++)
            {
                if (Seen[i] == targetKey)
                    return i;
            }
            return -1;
        }

        public void Add(int targetKey, float cooldown)
        {
            Seen.Add(targetKey);
        }
    }

    internal static class AoeCollisionCore
    {
        internal const float SpatialHashCellSize = 64f;

        internal static void RunCollision<TGate>(
            int entityIndexInQuery,
            in AoeIdentityComponent identity,
            in CombatKinematicsComponent kinematics,
            in CombatCollisionComponent collision,
            in AoeHitSpawnComponent hitSpawn,
            in AoeAreaComponent area,
            float cooldown,
            bool deactivateAfterPass,
            EnabledRefRW<Active> active,
            EnabledRefRW<AoeCollisionActiveTag> collisionActive,
            EnabledRefRW<CombatRenderActiveTag> renderActive,
            ref TGate gate,
            NativeArray<Entity> targetEntities,
            NativeArray<TargetPosition> targetPositions,
            NativeArray<TargetCollisionShape> targetShapes,
            NativeParallelMultiHashMap<long, int> occupiedTargetCells,
            NativeQueue<DamageReplayEvent>.ParallelWriter damageWriter,
            bool hasDamageWriter,
            NativeStream.Writer vfxPendingWriter,
            NativeQueue<ProjectileSpawnEvent>.ParallelWriter projectileEventWriter,
            NativeQueue<AoeSpawnEvent>.ParallelWriter aoeEventWriter,
            bool hasAoeEventWriter,
            NativeQueue<StackApplyEvent>.ParallelWriter stackApplyWriter,
            bool hasStackApplyWriter)
            where TGate : struct, IContactGate
        {
            NativeStream.Writer vfxPending = vfxPendingWriter;
            vfxPending.BeginForEachIndex(entityIndexInQuery);

            if (identity.Faction == CombatFaction.None)
            {
                Deactivate(active, collisionActive, renderActive);
                EndVfxStream(ref vfxPending);
                return;
            }

            // Bounded, allocation-free broadphase: walk cells inline, narrow-phase
            // each candidate, de-dup via the contact gate, stop at the hard cap.
            // Overflow keeps first-N in cell-scan order, not nearest-N.
            int remaining = CollisionConstants.MaxAoeTargetsPerTick;
            bool hitVfxEmitted = false;

            int2 min = MinCell(collision.BoundsMin);
            int2 max = MaxCell(collision.BoundsMax);
            for (int cy = min.y; cy <= max.y && remaining > 0; cy++)
            {
                for (int cx = min.x; cx <= max.x && remaining > 0; cx++)
                {
                    long key = CellKey(identity.Faction, cx, cy);
                    if (!occupiedTargetCells.TryGetFirstValue(
                            key,
                            out int i,
                            out NativeParallelMultiHashMapIterator<long> it))
                        continue;

                    do
                    {
                        Entity targetEntity = targetEntities[i];
                        int targetKey = TargetKey(targetEntity);

                        if (gate.IndexOf(targetKey) >= 0)
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

                        gate.Add(targetKey, cooldown);
                        EmitHit(
                            identity,
                            kinematics,
                            hitSpawn,
                            area,
                            targetEntity,
                            targetPosition,
                            targetKey,
                            ref vfxPending,
                            ref hitVfxEmitted,
                            damageWriter,
                            hasDamageWriter,
                            projectileEventWriter,
                            aoeEventWriter,
                            hasAoeEventWriter,
                            stackApplyWriter,
                            hasStackApplyWriter);

                        if (--remaining == 0)
                            break;
                    }
                    while (occupiedTargetCells.TryGetNextValue(out i, ref it));
                }
            }

            if (deactivateAfterPass)
                Deactivate(active, collisionActive, renderActive);

            EndVfxStream(ref vfxPending);
        }

        internal static void EmitHit(
            AoeIdentityComponent identity,
            CombatKinematicsComponent kinematics,
            AoeHitSpawnComponent hitSpawn,
            AoeAreaComponent area,
            Entity targetEntity,
            TargetPosition targetPosition,
            int targetKey,
            ref NativeStream.Writer vfxPending,
            ref bool hitVfxEmitted,
            NativeQueue<DamageReplayEvent>.ParallelWriter damageWriter,
            bool hasDamageWriter,
            NativeQueue<ProjectileSpawnEvent>.ParallelWriter projectileEventWriter,
            NativeQueue<AoeSpawnEvent>.ParallelWriter aoeEventWriter,
            bool hasAoeEventWriter,
            NativeQueue<StackApplyEvent>.ParallelWriter stackApplyWriter,
            bool hasStackApplyWriter)
        {
            if (hasDamageWriter && HasDamageEvent(hitSpawn))
            {
                damageWriter.Enqueue(new DamageReplayEvent
                {
                    TargetProxy = targetEntity,
                    HitPosition = kinematics.Position,
                    HitDirection = HitDirection(targetPosition.Value - kinematics.Position),
                    Kind = CombatHitKind.Aoe,
                    DamageAmount = hitSpawn.HitPayload.DamageAmount,
                    CritChance = hitSpawn.HitPayload.CritChance,
                    CritMultiplier = hitSpawn.HitPayload.CritMultiplier,
                    DirectDamageEnabled = hitSpawn.HitPayload.DirectDamageEnabled,
                    SourceNodeId = hitSpawn.HitPayload.SourceNodeId,
                    SourceId = identity.AoeId,
                    TypeId = identity.TypeId
                });
            }

            if (hasStackApplyWriter && hitSpawn.HitPayload.StackEffect.Enabled)
            {
                StackEffectSnapshot stackEffect = hitSpawn.HitPayload.StackEffect;
                stackApplyWriter.Enqueue(new StackApplyEvent
                {
                    TargetProxy = targetEntity,
                    DebuffKey = stackEffect.DebuffKey,
                    Threshold = stackEffect.Threshold,
                    Lifetime = stackEffect.Lifetime,
                    Contribution = stackEffect.Contribution,
                    Detonation = stackEffect.Detonation
                });
            }

            if (hitSpawn.ProjectileBurst.Enabled)
            {
                projectileEventWriter.Enqueue(ProjectileSpawnPipeline.BuildBurstEvent(
                    identity.Faction, identity.AoeId, identity.TypeId, targetKey,
                    kinematics.Position, targetPosition.Value,
                    hitSpawn.ProjectileBurst));
            }

            if (hasAoeEventWriter && hitSpawn.AoeSpawn.Enabled)
            {
                // The link is bounded by AoeOnHitSpawnSnapshot.MaxStackingSkillChainLinks.
                // Each spawned AOE is a fresh snapshot; there is no runtime retarget.
                aoeEventWriter.Enqueue(AoeSpawnPipeline.BuildOnHitAoeSpawnEvent(
                    identity.Faction,
                    identity.AoeId,
                    identity.TypeId,
                    targetKey,
                    targetPosition.Value,
                    hitSpawn.AoeSpawn));
            }

            if (!hitVfxEmitted)
            {
                vfxPending.Write(new VfxPendingSpawn
                {
                    Faction = identity.Faction,
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
            EnabledRefRW<AoeCollisionActiveTag> collisionActive,
            EnabledRefRW<CombatRenderActiveTag> renderActive)
        {
            active.ValueRW = false;
            collisionActive.ValueRW = false;
            renderActive.ValueRW = false;
        }

        internal static void EndVfxStream(ref NativeStream.Writer vfxPending)
        {
            vfxPending.EndForEachIndex();
        }

        internal static bool HasDamageEvent(in AoeHitSpawnComponent hitSpawn) =>
            hitSpawn.HitPayload.DirectDamageEnabled;

        internal static float2 HitDirection(float2 fallback)
        {
            return math.lengthsq(fallback) > ProjectileSimulationConstants.MinimumDirectionLengthSquared
                ? math.normalize(fallback)
                : float2.zero;
        }

        internal static int TargetKey(Entity entity)
        {
            unchecked
            {
                int key = ((entity.Index + 1) * 397) ^ entity.Version;
                key &= 0x7fffffff;
                return key == 0 ? 1 : key;
            }
        }

        internal static int2 MinCell(float2 min) => new int2(
            (int)math.floor(min.x / SpatialHashCellSize),
            (int)math.floor(min.y / SpatialHashCellSize));

        internal static int2 MaxCell(float2 max) => new int2(
            (int)math.floor(max.x / SpatialHashCellSize),
            (int)math.floor(max.y / SpatialHashCellSize));

        internal static long CellKey(CombatFaction faction, int x, int y)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ (byte)faction) * 1099511628211UL;
                hash = (hash ^ (uint)x) * 1099511628211UL;
                hash = (hash ^ (uint)y) * 1099511628211UL;
                return (long)hash;
            }
        }
    }
}
