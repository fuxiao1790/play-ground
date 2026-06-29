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
        private const int ImpactAoeIdSalt = 0x5F1A0E;
        private const int ProjectileBurstIdSalt = 0x7AB025;

        internal static void RunCollision<TGate>(
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
            NativeArray<TargetFaction> targetFactions,
            NativeParallelMultiHashMap<long, int> occupiedTargetCells,
            NativeQueue<CombatHitEvent>.ParallelWriter hitWriter,
            bool hasHitWriter,
            NativeQueue<VfxPendingSpawn>.ParallelWriter vfxPendingWriter,
            bool hasVfxWriter,
            NativeQueue<ProjectileSpawnEvent>.ParallelWriter projectileEventWriter,
            NativeQueue<AoeSpawnEvent>.ParallelWriter aoeEventWriter,
            bool hasAoeEventWriter)
            where TGate : struct, IContactGate
        {
            if (identity.Faction == CombatFaction.None)
            {
                Deactivate(active, collisionActive, renderActive);
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
                    long key = CellKey(cx, cy);
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
                            vfxPendingWriter,
                            hasVfxWriter,
                            ref hitVfxEmitted,
                            hitWriter,
                            hasHitWriter,
                            projectileEventWriter,
                            aoeEventWriter,
                            hasAoeEventWriter);

                        if (--remaining == 0)
                            break;
                    }
                    while (occupiedTargetCells.TryGetNextValue(out i, ref it));
                }
            }

            if (deactivateAfterPass)
                Deactivate(active, collisionActive, renderActive);
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
            NativeQueue<AoeSpawnEvent>.ParallelWriter aoeEventWriter,
            bool hasAoeEventWriter)
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

            if (hasAoeEventWriter
                && hitSpawn.OnHitSpawn.Enabled
                && hitSpawn.OnHitSpawn.Kind == IntervalChildKind.Aoe)
            {
                int aoeId = HashId(identity.AoeId, identity.TypeId, targetKey, ImpactAoeIdSalt ^ 0x13579B);
                aoeEventWriter.Enqueue(new AoeSpawnEvent
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
            EnabledRefRW<AoeCollisionActiveTag> collisionActive,
            EnabledRefRW<CombatRenderActiveTag> renderActive)
        {
            active.ValueRW = false;
            collisionActive.ValueRW = false;
            renderActive.ValueRW = false;
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

        internal static int2 MinCell(float2 min) => new int2(
            (int)math.floor(min.x / SpatialHashCellSize),
            (int)math.floor(min.y / SpatialHashCellSize));

        internal static int2 MaxCell(float2 max) => new int2(
            (int)math.floor(max.x / SpatialHashCellSize),
            (int)math.floor(max.y / SpatialHashCellSize));

        internal static long CellKey(int x, int y)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ (uint)x) * 1099511628211UL;
                hash = (hash ^ (uint)y) * 1099511628211UL;
                return (long)hash;
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
