using PlayGround.System.Common;
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
    [UpdateAfter(typeof(PlayGround.System.Common.CombatRenderPrepareSystem))]
    public partial struct AoeCollisionSystem : ISystem
    {
        private const float SpatialHashCellSize = 64f;

        private EntityQuery activeAoeQuery;
        private EntityQuery scopeQuery;

        public void OnCreate(ref SystemState state)
        {
            activeAoeQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<AoeTag>(),
                ComponentType.ReadOnly<AoeActiveTag>());
            scopeQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<AoeScope>(),
                ComponentType.ReadOnly<CombatTargetElement>());
        }

        public void OnUpdate(ref SystemState state)
        {
            if (activeAoeQuery.CalculateEntityCount() == 0)
            {
                return;
            }

            state.EntityManager.CompleteDependencyBeforeRO<CombatTargetElement>();
            int targetCellCapacity = 0;
            using NativeArray<Entity> scopes = scopeQuery.ToEntityArray(Allocator.Temp);
            for (int scopeIndex = 0; scopeIndex < scopes.Length; scopeIndex++)
            {
                DynamicBuffer<CombatTargetElement> targets =
                    state.EntityManager.GetBuffer<CombatTargetElement>(scopes[scopeIndex]);
                for (int i = 0; i < targets.Length; i++)
                {
                    CombatTargetElement target = targets[i];
                    int2 min = MinCell(target.BoundsMin);
                    int2 max = MaxCell(target.BoundsMax);
                    targetCellCapacity += ((max.x - min.x) + 1) * ((max.y - min.y) + 1);
                }
            }

            var occupiedTargetCells = new NativeParallelHashSet<long>(math.max(1, targetCellCapacity), Allocator.TempJob);
            for (int scopeIndex = 0; scopeIndex < scopes.Length; scopeIndex++)
            {
                Entity scope = scopes[scopeIndex];
                DynamicBuffer<CombatTargetElement> targets =
                    state.EntityManager.GetBuffer<CombatTargetElement>(scope);
                for (int i = 0; i < targets.Length; i++)
                {
                    CombatTargetElement target = targets[i];
                    int2 min = MinCell(target.BoundsMin);
                    int2 max = MaxCell(target.BoundsMax);
                    for (int y = min.y; y <= max.y; y++)
                    {
                        for (int x = min.x; x <= max.x; x++)
                        {
                            occupiedTargetCells.Add(CellKey(scope, x, y));
                        }
                    }
                }
            }

            var pendingHits = new NativeQueue<AoePendingHit>(Allocator.TempJob);
            var recycled = new NativeQueue<AoePendingRecycle>(Allocator.TempJob);
            var vfxPending = new NativeQueue<VfxPendingSpawn>(Allocator.TempJob);
            var job = new AoeCollisionJob
            {
                Targets = SystemAPI.GetBufferLookup<CombatTargetElement>(true),
                OccupiedTargetCells = occupiedTargetCells,
                OccupiedTargetCellCount = occupiedTargetCells.Count(),
                PendingHits = pendingHits.AsParallelWriter(),
                Recycled = recycled.AsParallelWriter(),
                VfxPending = vfxPending.AsParallelWriter()
            };

            JobHandle collisionHandle = job.ScheduleParallel(state.Dependency);
            JobHandle hitFlushHandle = new AoeHitFlushJob
            {
                PendingHits = pendingHits,
                Hits = SystemAPI.GetBufferLookup<AoeHitElement>()
            }.Schedule(collisionHandle);
            JobHandle recycleFlushHandle = new AoeRecycleFlushJob
            {
                Recycled = recycled,
                RecycleBuffers = SystemAPI.GetBufferLookup<AoeRecycleElement>()
            }.Schedule(collisionHandle);
            JobHandle vfxFlushHandle = new VfxFlushJob
            {
                Pending = vfxPending,
                VfxBuffers = SystemAPI.GetBufferLookup<VfxSpawnRequestElement>()
            }.Schedule(collisionHandle);

            JobHandle disposeHitsHandle = pendingHits.Dispose(hitFlushHandle);
            JobHandle disposeRecycleHandle = recycled.Dispose(recycleFlushHandle);
            JobHandle disposeVfxHandle = vfxPending.Dispose(vfxFlushHandle);
            JobHandle flushesHandle = JobHandle.CombineDependencies(
                JobHandle.CombineDependencies(disposeHitsHandle, disposeRecycleHandle),
                disposeVfxHandle);
            state.Dependency = occupiedTargetCells.Dispose(flushesHandle);
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(AoeActiveTag))]
        private partial struct AoeCollisionJob : IJobEntity
        {
            [ReadOnly] public BufferLookup<CombatTargetElement> Targets;
            [ReadOnly] public NativeParallelHashSet<long> OccupiedTargetCells;
            public int OccupiedTargetCellCount;
            public NativeQueue<AoePendingHit>.ParallelWriter PendingHits;
            public NativeQueue<AoePendingRecycle>.ParallelWriter Recycled;
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;

            private void Execute(
                Entity entity,
                in AoeIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatCollisionComponent collision,
                in CombatHitComponent hit,
                in AoeLifetimeComponent lifetime,
                in AoeHitGateComponent hitGate,
                in AoeHitSpawnComponent hitSpawn,
                in CombatRenderElement renderElement,
                EnabledRefRW<AoeActiveTag> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive,
                DynamicBuffer<AoeContactGateElement> contactGates)
            {
                if (identity.Scope == Entity.Null || !Targets.HasBuffer(identity.Scope))
                {
                    Deactivate(entity, identity, renderElement, active, renderActive);
                    return;
                }

                if (SpatialHashIntersects(identity, collision))
                {
                    DynamicBuffer<CombatTargetElement> targets = Targets[identity.Scope];
                    for (int i = 0; i < targets.Length; i++)
                    {
                        CombatTargetElement target = targets[i];
                        if ((hit.TargetMask & target.TargetMask) == 0)
                        {
                            continue;
                        }

                        if (!CombatCollisionMath.BoundsIntersect(
                            collision.BoundsMin,
                            collision.BoundsMax,
                            target.BoundsMin,
                            target.BoundsMax))
                        {
                            continue;
                        }

                        if (!CombatCollisionMath.Hit(kinematics, collision, target))
                        {
                            continue;
                        }

                        if (lifetime.IsPulse == 1)
                        {
                            ResolvePulseHit(identity, kinematics, hit, hitSpawn, target, contactGates);
                        }
                        else
                        {
                            ResolveLingeringHit(identity, kinematics, hit, hitGate, hitSpawn, target, contactGates);
                        }
                    }
                }

                if (lifetime.IsPulse == 1)
                {
                    Deactivate(entity, identity, renderElement, active, renderActive);
                }
            }

            private void ResolvePulseHit(
                AoeIdentityComponent identity,
                CombatKinematicsComponent kinematics,
                CombatHitComponent hit,
                AoeHitSpawnComponent hitSpawn,
                CombatTargetElement target,
                DynamicBuffer<AoeContactGateElement> contactGates)
            {
                if (IndexOfGate(contactGates, target.TargetId) >= 0)
                {
                    return;
                }

                contactGates.Add(new AoeContactGateElement
                {
                    TargetId = target.TargetId,
                    CooldownRemaining = 0f
                });
                EmitHit(identity, kinematics, hit, hitSpawn, target);
            }

            private void ResolveLingeringHit(
                AoeIdentityComponent identity,
                CombatKinematicsComponent kinematics,
                CombatHitComponent hit,
                AoeHitGateComponent hitGate,
                AoeHitSpawnComponent hitSpawn,
                CombatTargetElement target,
                DynamicBuffer<AoeContactGateElement> contactGates)
            {
                if (IndexOfGate(contactGates, target.TargetId) >= 0)
                {
                    return;
                }

                contactGates.Add(new AoeContactGateElement
                {
                    TargetId = target.TargetId,
                    CooldownRemaining = hitGate.RepeatHitCooldownSeconds
                });
                EmitHit(identity, kinematics, hit, hitSpawn, target);
            }

            private void EmitHit(
                AoeIdentityComponent identity,
                CombatKinematicsComponent kinematics,
                CombatHitComponent hit,
                AoeHitSpawnComponent hitSpawn,
                CombatTargetElement target)
            {
                PendingHits.Enqueue(new AoePendingHit
                {
                    Scope = identity.Scope,
                    AoeId = identity.AoeId,
                    TypeId = identity.TypeId,
                    TargetId = target.TargetId,
                    Position = kinematics.Position,
                    DamageAmount = hit.DamageAmount,
                    ProjectileBurst = hitSpawn.ProjectileBurst
                });
                VfxPending.Enqueue(new VfxPendingSpawn
                {
                    Scope = identity.Scope,
                    TypeId = identity.TypeId,
                    Trigger = 1,
                    Position = kinematics.Position
                });
            }

            private void Deactivate(
                Entity entity,
                AoeIdentityComponent identity,
                CombatRenderElement renderElement,
                EnabledRefRW<AoeActiveTag> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive)
            {
                active.ValueRW = false;
                renderActive.ValueRW = false;
                Recycled.Enqueue(new AoePendingRecycle
                {
                    Scope = identity.Scope,
                    AoeEntity = entity,
                    TypeId = identity.TypeId,
                    Render = renderElement
                });
            }

            private bool SpatialHashIntersects(AoeIdentityComponent identity, CombatCollisionComponent collision)
            {
                if (OccupiedTargetCellCount == 0)
                {
                    return false;
                }

                int2 min = MinCell(collision.BoundsMin);
                int2 max = MaxCell(collision.BoundsMax);
                for (int y = min.y; y <= max.y; y++)
                {
                    for (int x = min.x; x <= max.x; x++)
                    {
                        if (OccupiedTargetCells.Contains(CellKey(identity.Scope, x, y)))
                        {
                            return true;
                        }
                    }
                }

                return false;
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
        }

        [BurstCompile]
        private struct AoeHitFlushJob : IJob
        {
            public NativeQueue<AoePendingHit> PendingHits;
            public BufferLookup<AoeHitElement> Hits;

            public void Execute()
            {
                while (PendingHits.TryDequeue(out AoePendingHit pending))
                {
                    if (pending.Scope == Entity.Null || !Hits.HasBuffer(pending.Scope))
                    {
                        continue;
                    }

                    Hits[pending.Scope].Add(new AoeHitElement
                    {
                        AoeId = pending.AoeId,
                        TypeId = pending.TypeId,
                        TargetId = pending.TargetId,
                        Position = pending.Position,
                        DamageAmount = pending.DamageAmount,
                        ProjectileBurst = pending.ProjectileBurst
                    });
                }
            }
        }

        [BurstCompile]
        private struct AoeRecycleFlushJob : IJob
        {
            public NativeQueue<AoePendingRecycle> Recycled;
            public BufferLookup<AoeRecycleElement> RecycleBuffers;

            public void Execute()
            {
                while (Recycled.TryDequeue(out AoePendingRecycle recycle))
                {
                    if (recycle.Scope == Entity.Null || !RecycleBuffers.HasBuffer(recycle.Scope))
                    {
                        continue;
                    }

                    RecycleBuffers[recycle.Scope].Add(new AoeRecycleElement
                    {
                        AoeEntity = recycle.AoeEntity,
                        TypeId = recycle.TypeId,
                        Render = recycle.Render
                    });
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

        private static long CellKey(Entity scope, int x, int y)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ (uint)scope.Index) * 1099511628211UL;
                hash = (hash ^ (uint)scope.Version) * 1099511628211UL;
                hash = (hash ^ (uint)x) * 1099511628211UL;
                hash = (hash ^ (uint)y) * 1099511628211UL;
                return (long)hash;
            }
        }
    }
}
