using PlayGround.System.Common;
using PlayGround.System.Projectile;
using PlayGround.System.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
namespace PlayGround.System.Aoe
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AoeContactGateSystem))]
    [UpdateBefore(typeof(DamageFinalizeSystem))]
    [UpdateBefore(typeof(CombatRenderPrepareSystem))]
    public partial struct AoeCollisionSystem : ISystem
    {
        private const float SpatialHashCellSize = 64f;

        private EntityQuery activeAoeQuery;
        private EntityQuery targetQuery;

        public void OnCreate(ref SystemState state)
        {
            activeAoeQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<AoeTag>(),
                ComponentType.ReadOnly<Active>(),
                ComponentType.ReadOnly<AoeCollisionActiveTag>(),
                ComponentType.ReadOnly<AoeIdentityComponent>(),
                ComponentType.ReadOnly<CombatKinematicsComponent>(),
                ComponentType.ReadOnly<CombatCollisionComponent>(),
                // CombatLifetimeComponent is enableable; listing it here would require it
                // ENABLED, excluding pulse AOEs (lifetime disabled) from the count and the
                // per-entity vfx stream sizing. The job reads its state via EnabledRefRO.
                ComponentType.ReadOnly<AoeHitGateComponent>(),
                ComponentType.ReadOnly<AoeHitSpawnComponent>(),
                ComponentType.ReadOnly<AoeAreaComponent>(),
                ComponentType.ReadWrite<CombatRenderActiveTag>(),
                ComponentType.ReadWrite<AoeContactGateElement>());
            targetQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<TargetProxyTag>(),
                ComponentType.ReadOnly<TargetPosition>(),
                ComponentType.ReadOnly<TargetCollisionShape>(),
                ComponentType.ReadOnly<TargetFaction>());
        }

        public void OnUpdate(ref SystemState state)
        {
            int activeAoeCount = activeAoeQuery.CalculateEntityCount();
            if (activeAoeCount == 0)
            {
                return;
            }

            state.EntityManager.CompleteDependencyBeforeRO<TargetPosition>();
            state.EntityManager.CompleteDependencyBeforeRO<TargetCollisionShape>();
            state.EntityManager.CompleteDependencyBeforeRO<TargetFaction>();

            NativeArray<Entity> targetEntities = targetQuery.ToEntityArray(Allocator.TempJob);
            NativeArray<TargetPosition> targetPositions = targetQuery.ToComponentDataArray<TargetPosition>(Allocator.TempJob);
            NativeArray<TargetCollisionShape> targetShapes = targetQuery.ToComponentDataArray<TargetCollisionShape>(Allocator.TempJob);
            NativeArray<TargetFaction> targetFactions = targetQuery.ToComponentDataArray<TargetFaction>(Allocator.TempJob);

            int targetCellCapacity = 0;
            for (int i = 0; i < targetShapes.Length; i++)
            {
                TargetCollisionShape target = targetShapes[i];
                int2 min = MinCell(target.BoundsMin);
                int2 max = MaxCell(target.BoundsMax);
                targetCellCapacity += ((max.x - min.x) + 1) * ((max.y - min.y) + 1);
            }

            var occupiedTargetCells = new NativeParallelMultiHashMap<long, int>(math.max(1, targetCellCapacity), Allocator.TempJob);
            for (int i = 0; i < targetShapes.Length; i++)
            {
                TargetCollisionShape target = targetShapes[i];
                int2 min = MinCell(target.BoundsMin);
                int2 max = MaxCell(target.BoundsMax);
                for (int y = min.y; y <= max.y; y++)
                {
                    for (int x = min.x; x <= max.x; x++)
                    {
                        occupiedTargetCells.Add(CellKey(targetFactions[i].Value, x, y), i);
                    }
                }
            }

            var expansion = state.World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
            var damageBridge = state.World.GetExistingSystemManaged<DamageDispatchBridge>();
            var vfxPending = new NativeStream(activeAoeCount, Allocator.TempJob);
            var job = new AoeCollisionJob
            {
                TargetEntities = targetEntities,
                TargetPositions = targetPositions,
                TargetShapes = targetShapes,
                OccupiedTargetCells = occupiedTargetCells,
                DamageWriter = damageBridge != null
                    ? damageBridge.DamageQueue.AsParallelWriter()
                    : default,
                HasDamageWriter = damageBridge != null && damageBridge.DamageQueue.IsCreated,
                VfxPending = vfxPending.AsWriter(),
                ProjectileEventWriter = expansion != null
                    ? expansion.EventQueue.AsParallelWriter()
                    : default
            };

            JobHandle collisionHandle = job.ScheduleParallel(state.Dependency);

            // The collision job writes the projectile expansion EventQueue via ParallelWriter.
            // That queue is read on the main thread by ProjectileSpawnExpansionSystem, which only
            // completes its own component-derived dependency. Forward this write job to it.
            if (expansion != null)
                expansion.ProducerHandle =
                    JobHandle.CombineDependencies(expansion.ProducerHandle, collisionHandle);
            if (damageBridge != null)
                damageBridge.ProducerHandle =
                    JobHandle.CombineDependencies(damageBridge.ProducerHandle, collisionHandle);

            JobHandle vfxFlushHandle = new VfxStreamFlushJob
            {
                Scope = SystemAPI.GetSingletonEntity<CombatScope>(),
                Pending = vfxPending,
                VfxBuffers = SystemAPI.GetBufferLookup<VfxSpawnRequestElement>()
            }.Schedule(collisionHandle);

            JobHandle disposeVfxHandle = vfxPending.Dispose(vfxFlushHandle);
            JobHandle targetDisposeHandle = JobHandle.CombineDependencies(
                targetEntities.Dispose(collisionHandle),
                JobHandle.CombineDependencies(
                    targetPositions.Dispose(collisionHandle),
                    JobHandle.CombineDependencies(
                        targetShapes.Dispose(collisionHandle),
                        targetFactions.Dispose(collisionHandle))));
            state.Dependency = occupiedTargetCells.Dispose(
                JobHandle.CombineDependencies(
                    targetDisposeHandle,
                    disposeVfxHandle));
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(Active), typeof(AoeCollisionActiveTag))]
        // Pulse AOEs have CombatLifetimeComponent DISABLED. Without WithPresent the
        // EnabledRefRO param would require it enabled, excluding pulse AOEs from
        // collision entirely (they would never hit, never deactivate -> leak).
        [WithPresent(typeof(CombatLifetimeComponent))]
        private partial struct AoeCollisionJob : IJobEntity
        {
            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            [ReadOnly] public NativeArray<TargetCollisionShape> TargetShapes;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> OccupiedTargetCells;
            public NativeQueue<DamageReplayEvent>.ParallelWriter DamageWriter;
            public bool HasDamageWriter;
            public NativeStream.Writer VfxPending;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventWriter;

            private void Execute(
                [EntityIndexInQuery] int entityIndexInQuery,
                Entity entity,
                in AoeIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatCollisionComponent collision,
                EnabledRefRO<CombatLifetimeComponent> lifetimeEnabled,
                in AoeHitGateComponent hitGate,
                in AoeHitSpawnComponent hitSpawn,
                in AoeAreaComponent area,
                EnabledRefRW<Active> active,
                EnabledRefRW<AoeCollisionActiveTag> collisionActive,
                EnabledRefRW<CombatRenderActiveTag> renderActive,
                DynamicBuffer<AoeContactGateElement> contactGates)
            {
                NativeStream.Writer vfxPending = VfxPending;
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
                float cooldown = !lifetimeEnabled.ValueRO ? 0f : hitGate.RepeatHitCooldownSeconds;

                int2 min = MinCell(collision.BoundsMin);
                int2 max = MaxCell(collision.BoundsMax);
                for (int cy = min.y; cy <= max.y && remaining > 0; cy++)
                {
                    for (int cx = min.x; cx <= max.x && remaining > 0; cx++)
                    {
                        long key = CellKey(identity.Faction, cx, cy);
                        if (!OccupiedTargetCells.TryGetFirstValue(
                                key,
                                out int i,
                                out NativeParallelMultiHashMapIterator<long> it))
                        {
                            continue;
                        }

                        do
                        {
                            Entity targetEntity = TargetEntities[i];
                            int targetKey = TargetKey(targetEntity);

                            if (IndexOfGate(contactGates, targetKey) >= 0)
                            {
                                continue;
                            }

                            TargetPosition targetPosition = TargetPositions[i];
                            TargetCollisionShape target = TargetShapes[i];

                            if (!CombatCollisionMath.BoundsIntersect(
                                    collision.BoundsMin,
                                    collision.BoundsMax,
                                    target.BoundsMin,
                                    target.BoundsMax))
                            {
                                continue;
                            }

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
                            {
                                continue;
                            }

                            contactGates.Add(new AoeContactGateElement
                            {
                                TargetId = targetKey,
                                CooldownRemaining = cooldown
                            });
                            EmitHit(
                                identity,
                                kinematics,
                                hitSpawn,
                                area,
                                targetEntity,
                                targetPosition,
                                targetKey,
                                ref vfxPending,
                                ref hitVfxEmitted);

                            if (--remaining == 0)
                            {
                                break;
                            }
                        }
                        while (OccupiedTargetCells.TryGetNextValue(out i, ref it));
                    }
                }

                if (!lifetimeEnabled.ValueRO)
                {
                    Deactivate(active, collisionActive, renderActive);
                }

                EndVfxStream(ref vfxPending);
            }

            private void EmitHit(
                AoeIdentityComponent identity,
                CombatKinematicsComponent kinematics,
                AoeHitSpawnComponent hitSpawn,
                AoeAreaComponent area,
                Entity targetEntity,
                TargetPosition targetPosition,
                int targetKey,
                ref NativeStream.Writer vfxPending,
                ref bool hitVfxEmitted)
            {
                if (HasDamageWriter && HasDamageEvent(hitSpawn))
                {
                    DamageWriter.Enqueue(new DamageReplayEvent
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
                        StackEffect = hitSpawn.HitPayload.StackEffect,
                        SourceId = identity.AoeId,
                        TypeId = identity.TypeId
                    });
                }

                if (hitSpawn.ProjectileBurst.Enabled)
                {
                    ProjectileEventWriter.Enqueue(ProjectileSpawnPipeline.BuildBurstEvent(
                        identity.Faction, identity.AoeId, identity.TypeId, targetKey,
                        kinematics.Position, targetPosition.Value,
                        hitSpawn.ProjectileBurst));
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

            private static void Deactivate(
                EnabledRefRW<Active> active,
                EnabledRefRW<AoeCollisionActiveTag> collisionActive,
                EnabledRefRW<CombatRenderActiveTag> renderActive)
            {
                active.ValueRW = false;
                collisionActive.ValueRW = false;
                renderActive.ValueRW = false;
            }

            private static void EndVfxStream(ref NativeStream.Writer vfxPending)
            {
                vfxPending.EndForEachIndex();
            }

            private static int IndexOfGate(DynamicBuffer<AoeContactGateElement> contactGates, int targetId)
            {
                for (int i = 0; i < contactGates.Length; i++)
                {
                    if (contactGates[i].TargetId == targetId)
                    {
                        return i;
                    }
                }

                return -1;
            }

            private static bool HasDamageEvent(in AoeHitSpawnComponent hitSpawn) =>
                hitSpawn.HitPayload.DirectDamageEnabled || hitSpawn.HitPayload.StackEffect.Enabled;

            private static float2 HitDirection(float2 fallback)
            {
                return math.lengthsq(fallback) > ProjectileSimulationConstants.MinimumDirectionLengthSquared
                    ? math.normalize(fallback)
                    : float2.zero;
            }

            private static int TargetKey(Entity entity)
            {
                unchecked
                {
                    int key = ((entity.Index + 1) * 397) ^ entity.Version;
                    key &= 0x7fffffff;
                    return key == 0 ? 1 : key;
                }
            }
        }

        private static int2 MinCell(float2 min)
        {
            return new int2(
                (int)math.floor(min.x / SpatialHashCellSize),
                (int)math.floor(min.y / SpatialHashCellSize));
        }

        private static int2 MaxCell(float2 max)
        {
            return new int2(
                (int)math.floor(max.x / SpatialHashCellSize),
                (int)math.floor(max.y / SpatialHashCellSize));
        }

        private static long CellKey(CombatFaction faction, int x, int y)
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
