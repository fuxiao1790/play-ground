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
                ComponentType.ReadOnly<AoeActiveTag>(),
                ComponentType.ReadOnly<AoeCollisionActiveTag>(),
                ComponentType.ReadOnly<AoeIdentityComponent>(),
                ComponentType.ReadOnly<CombatKinematicsComponent>(),
                ComponentType.ReadOnly<CombatCollisionComponent>(),
                ComponentType.ReadOnly<AoeLifetimeComponent>(),
                ComponentType.ReadOnly<AoeHitGateComponent>(),
                ComponentType.ReadOnly<AoeHitSpawnComponent>(),
                ComponentType.ReadOnly<AoeAreaComponent>(),
                ComponentType.ReadWrite<CombatRenderActiveTag>(),
                ComponentType.ReadWrite<AoeContactGateElement>());
            scopeQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<AoeScope>(),
                ComponentType.ReadOnly<CombatTargetElement>());

        }

        public void OnUpdate(ref SystemState state)
        {
            int activeAoeCount = activeAoeQuery.CalculateEntityCount();
            if (activeAoeCount == 0)
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

            var pendingDamage = new NativeStream(activeAoeCount, Allocator.TempJob);
            var pendingSpawns = new NativeStream(activeAoeCount, Allocator.TempJob);
            var vfxPending = new NativeStream(activeAoeCount, Allocator.TempJob);
            var job = new AoeCollisionJob
            {
                Targets = SystemAPI.GetBufferLookup<CombatTargetElement>(true),
                OccupiedTargetCells = occupiedTargetCells,
                PendingDamage = pendingDamage.AsWriter(),
                PendingSpawns = pendingSpawns.AsWriter(),
                VfxPending = vfxPending.AsWriter()
            };

            JobHandle collisionHandle = job.ScheduleParallel(state.Dependency);
            JobHandle hitFlushHandle = new CombatHitFlushJob
            {
                PendingDamage = pendingDamage,
                PendingSpawns = pendingSpawns,
                Damage = SystemAPI.GetBufferLookup<CombatDamageElement>(),
                Spawns = SystemAPI.GetBufferLookup<CombatSpawnElement>()
            }.Schedule(collisionHandle);
            JobHandle vfxFlushHandle = new VfxStreamFlushJob
            {
                Pending = vfxPending,
                VfxBuffers = SystemAPI.GetBufferLookup<VfxSpawnRequestElement>()
            }.Schedule(collisionHandle);

            JobHandle disposeDamageHandle = pendingDamage.Dispose(hitFlushHandle);
            JobHandle disposeSpawnsHandle = pendingSpawns.Dispose(hitFlushHandle);
            JobHandle disposeVfxHandle = vfxPending.Dispose(vfxFlushHandle);
            state.Dependency = occupiedTargetCells.Dispose(
                JobHandle.CombineDependencies(disposeDamageHandle, disposeSpawnsHandle, disposeVfxHandle));
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(AoeActiveTag), typeof(AoeCollisionActiveTag))]
        private partial struct AoeCollisionJob : IJobEntity
        {
            [ReadOnly] public BufferLookup<CombatTargetElement> Targets;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> OccupiedTargetCells;
            public NativeStream.Writer PendingDamage;
            public NativeStream.Writer PendingSpawns;
            public NativeStream.Writer VfxPending;

            private void Execute(
                [EntityIndexInQuery] int entityIndexInQuery,
                Entity entity,
                in AoeIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatCollisionComponent collision,
                in AoeLifetimeComponent lifetime,
                in AoeHitGateComponent hitGate,
                in AoeHitSpawnComponent hitSpawn,
                in AoeAreaComponent area,
                EnabledRefRW<AoeActiveTag> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive,
                DynamicBuffer<AoeContactGateElement> contactGates)
            {
                NativeStream.Writer pendingDamage = PendingDamage;
                NativeStream.Writer pendingSpawns = PendingSpawns;
                NativeStream.Writer vfxPending = VfxPending;
                pendingDamage.BeginForEachIndex(entityIndexInQuery);
                pendingSpawns.BeginForEachIndex(entityIndexInQuery);
                vfxPending.BeginForEachIndex(entityIndexInQuery);

                if (identity.Scope == Entity.Null || !Targets.HasBuffer(identity.Scope))
                {
                    Deactivate(active, renderActive);
                    EndStreams(ref pendingDamage, ref pendingSpawns, ref vfxPending);
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
                        ResolveHit(
                            identity,
                            kinematics,
                            hitSpawn,
                            area,
                            target,
                            contactGates,
                            cooldown,
                            ref pendingDamage,
                            ref pendingSpawns,
                            ref vfxPending);
                    }
                }

                candidates.Dispose();

                if (lifetime.IsPulse == 1)
                {
                    Deactivate(active, renderActive);
                }

                EndStreams(ref pendingDamage, ref pendingSpawns, ref vfxPending);
            }

            private void ResolveHit(
                AoeIdentityComponent identity,
                CombatKinematicsComponent kinematics,
                AoeHitSpawnComponent hitSpawn,
                AoeAreaComponent area,
                CombatTargetElement target,
                DynamicBuffer<AoeContactGateElement> contactGates,
                float cooldown,
                ref NativeStream.Writer pendingDamage,
                ref NativeStream.Writer pendingSpawns,
                ref NativeStream.Writer vfxPending)
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
                EmitHit(identity, kinematics, hitSpawn, area, target, ref pendingDamage, ref pendingSpawns, ref vfxPending);
            }

            private void EmitHit(
                AoeIdentityComponent identity,
                CombatKinematicsComponent kinematics,
                AoeHitSpawnComponent hitSpawn,
                AoeAreaComponent area,
                CombatTargetElement target,
                ref NativeStream.Writer pendingDamage,
                ref NativeStream.Writer pendingSpawns,
                ref NativeStream.Writer vfxPending)
            {
                if (HasDamageEvent(hitSpawn))
                {
                    pendingDamage.Write(new CombatPendingDamage
                    {
                        Scope = identity.Scope,
                        SourceId = identity.AoeId,
                        TypeId = identity.TypeId,
                        TargetId = target.TargetId,
                        Position = kinematics.Position,
                        Kind = CombatHitKind.Aoe,
                        DamageAmount = hitSpawn.HitPayload.DamageAmount,
                        CritChance = hitSpawn.HitPayload.CritChance,
                        CritMultiplier = hitSpawn.HitPayload.CritMultiplier,
                        DirectDamageEnabled = hitSpawn.HitPayload.DirectDamageEnabled,
                        SourceNodeId = hitSpawn.HitPayload.SourceNodeId,
                        StackEffect = hitSpawn.HitPayload.StackEffect
                    });
                }

                if (HasSpawnEvent(hitSpawn))
                {
                    pendingSpawns.Write(new CombatPendingSpawn
                    {
                        Scope = identity.Scope,
                        SourceId = identity.AoeId,
                        TypeId = identity.TypeId,
                        TargetId = target.TargetId,
                        Position = kinematics.Position,
                        TargetPosition = target.Position,
                        Kind = CombatHitKind.Aoe,
                        SourceNodeId = hitSpawn.HitPayload.SourceNodeId,
                        ProjectileBurst = hitSpawn.ProjectileBurst
                    });
                }

                vfxPending.Write(new VfxPendingSpawn
                {
                    Scope = identity.Scope,
                    TypeId = identity.TypeId,
                    Trigger = 1,
                    Position = kinematics.Position,
                    AreaSize = area.Size
                });
            }

            private static void Deactivate(
                EnabledRefRW<AoeActiveTag> active,
                EnabledRefRW<CombatRenderActiveTag> renderActive)
            {
                active.ValueRW = false;
                renderActive.ValueRW = false;
            }

            private static void EndStreams(
                ref NativeStream.Writer pendingDamage,
                ref NativeStream.Writer pendingSpawns,
                ref NativeStream.Writer vfxPending)
            {
                pendingDamage.EndForEachIndex();
                pendingSpawns.EndForEachIndex();
                vfxPending.EndForEachIndex();
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

            private static bool HasDamageEvent(in AoeHitSpawnComponent hitSpawn) =>
                hitSpawn.HitPayload.DirectDamageEnabled || hitSpawn.HitPayload.StackEffect.Enabled;

            private static bool HasSpawnEvent(in AoeHitSpawnComponent hitSpawn) =>
                hitSpawn.ProjectileBurst.Enabled;
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
