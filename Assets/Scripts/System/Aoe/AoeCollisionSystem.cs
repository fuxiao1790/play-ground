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
    [UpdateBefore(typeof(ProjectileSpawnSystem))]
    [UpdateBefore(typeof(CombatRenderPrepareSystem))]
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

            var occupiedTargetCells = new NativeParallelMultiHashMap<long, int>(math.max(1, targetCellCapacity), Allocator.TempJob);
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
                            occupiedTargetCells.Add(CellKey(scope, x, y), i);
                        }
                    }
                }
            }

            var pendingHits = new NativeQueue<CombatPendingHit>(Allocator.TempJob);
            var recycled = new NativeQueue<AoePendingRecycle>(Allocator.TempJob);
            var vfxPending = new NativeQueue<VfxPendingSpawn>(Allocator.TempJob);
            var job = new AoeCollisionJob
            {
                Targets = SystemAPI.GetBufferLookup<CombatTargetElement>(true),
                OccupiedTargetCells = occupiedTargetCells,
                PendingHits = pendingHits.AsParallelWriter(),
                Recycled = recycled.AsParallelWriter(),
                VfxPending = vfxPending.AsParallelWriter()
            };

            JobHandle collisionHandle = job.ScheduleParallel(state.Dependency);
            JobHandle hitFlushHandle = new CombatHitFlushJob
            {
                PendingHits = pendingHits,
                Hits = SystemAPI.GetBufferLookup<CombatHitElement>(),
                Payloads = SystemAPI.GetBufferLookup<CombatHitPayloadElement>(),
                Effects = SystemAPI.GetBufferLookup<CombatHitEffectElement>()
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
        [WithAll(typeof(AoeTag), typeof(AoeActiveTag), typeof(AoeCollisionActiveTag))]
        private partial struct AoeCollisionJob : IJobEntity
        {
            [ReadOnly] public BufferLookup<CombatTargetElement> Targets;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> OccupiedTargetCells;
            public NativeQueue<CombatPendingHit>.ParallelWriter PendingHits;
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
                in AoeAreaComponent area,
                EnabledRefRW<AoeActiveTag> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive,
                DynamicBuffer<AoeContactGateElement> contactGates)
            {
                if (identity.Scope == Entity.Null || !Targets.HasBuffer(identity.Scope))
                {
                    Deactivate(entity, identity, active, renderActive);
                    return;
                }

                var candidates = new NativeHashSet<int>(4, Allocator.Temp);
                CollectCandidates(identity, collision, ref candidates);

                if (!candidates.IsEmpty)
                {
                    DynamicBuffer<CombatTargetElement> targets = Targets[identity.Scope];
                    foreach (int i in candidates)
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

                        float cooldown = lifetime.IsPulse == 1 ? 0f : hitGate.RepeatHitCooldownSeconds;
                        ResolveHit(identity, kinematics, hit, hitSpawn, area, target, contactGates, cooldown);
                    }
                }

                candidates.Dispose();

                if (lifetime.IsPulse == 1)
                {
                    Deactivate(entity, identity, active, renderActive);
                }
            }

            private void ResolveHit(
                AoeIdentityComponent identity,
                CombatKinematicsComponent kinematics,
                CombatHitComponent hit,
                AoeHitSpawnComponent hitSpawn,
                AoeAreaComponent area,
                CombatTargetElement target,
                DynamicBuffer<AoeContactGateElement> contactGates,
                float cooldown)
            {
                if (IndexOfGate(contactGates, target.TargetId) >= 0)
                {
                    return;
                }

                contactGates.Add(new AoeContactGateElement
                {
                    TargetId = target.TargetId,
                    CooldownRemaining = cooldown
                });
                EmitHit(identity, kinematics, hit, hitSpawn, area, target);
            }

            private void EmitHit(
                AoeIdentityComponent identity,
                CombatKinematicsComponent kinematics,
                CombatHitComponent hit,
                AoeHitSpawnComponent hitSpawn,
                AoeAreaComponent area,
                CombatTargetElement target)
            {
                PendingHits.Enqueue(new CombatPendingHit
                {
                    Scope = identity.Scope,
                    SourceId = identity.AoeId,
                    TypeId = identity.TypeId,
                    TargetId = target.TargetId,
                    Position = kinematics.Position,
                    Kind = CombatHitKind.Aoe,
                    DamageAmount = hit.DamageAmount,
                    CritChance = hit.CritChance,
                    CritMultiplier = hit.CritMultiplier,
                    DirectDamageEnabled = hit.DirectDamageEnabled,
                    SourceNodeId = hit.SourceNodeId,
                    ProjectileBurst = hitSpawn.ProjectileBurst,
                    StackEffect = hitSpawn.StackEffect
                });
                VfxPending.Enqueue(new VfxPendingSpawn
                {
                    Scope = identity.Scope,
                    TypeId = identity.TypeId,
                    Trigger = 1,
                    Position = kinematics.Position,
                    AreaSize = area.Size
                });
            }

            private void Deactivate(
                Entity entity,
                AoeIdentityComponent identity,
                EnabledRefRW<AoeActiveTag> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive)
            {
                active.ValueRW = false;
                renderActive.ValueRW = false;
                Recycled.Enqueue(new AoePendingRecycle
                {
                    Scope = identity.Scope,
                    AoeEntity = entity,
                    TypeId = identity.TypeId
                });
            }

            private void CollectCandidates(AoeIdentityComponent identity, CombatCollisionComponent collision, ref NativeHashSet<int> candidates)
            {
                int2 min = MinCell(collision.BoundsMin);
                int2 max = MaxCell(collision.BoundsMax);
                for (int cy = min.y; cy <= max.y; cy++)
                {
                    for (int cx = min.x; cx <= max.x; cx++)
                    {
                        long key = CellKey(identity.Scope, cx, cy);
                        if (OccupiedTargetCells.TryGetFirstValue(key, out int targetIdx, out NativeParallelMultiHashMapIterator<long> it))
                        {
                            do
                            {
                                candidates.Add(targetIdx);
                            } while (OccupiedTargetCells.TryGetNextValue(out targetIdx, ref it));
                        }
                    }
                }
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
                        TypeId = recycle.TypeId
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
