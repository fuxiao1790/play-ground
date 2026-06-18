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
                ComponentType.ReadOnly<Active>(),
                ComponentType.ReadOnly<AoeCollisionActiveTag>(),
                ComponentType.ReadOnly<AoeIdentityComponent>(),
                ComponentType.ReadOnly<CombatKinematicsComponent>(),
                ComponentType.ReadOnly<CombatCollisionComponent>(),
                ComponentType.ReadOnly<CombatLifetimeComponent>(),
                ComponentType.ReadOnly<AoeHitGateComponent>(),
                ComponentType.ReadOnly<AoeHitSpawnComponent>(),
                ComponentType.ReadOnly<AoeAreaComponent>(),
                ComponentType.ReadWrite<CombatRenderActiveTag>(),
                ComponentType.ReadWrite<AoeContactGateElement>());
            scopeQuery = state.GetEntityQuery(ComponentType.ReadOnly<CombatScope>());
        }

        public void OnUpdate(ref SystemState state)
        {
            int activeAoeCount = activeAoeQuery.CalculateEntityCount();
            if (activeAoeCount == 0)
            {
                return;
            }

            state.EntityManager.CompleteDependencyBeforeRO<CombatTargetElement>();

            Entity scope = scopeQuery.GetSingletonEntity();
            DynamicBuffer<CombatTargetElement> targets = state.EntityManager.GetBuffer<CombatTargetElement>(scope);

            int targetCellCapacity = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                CombatTargetElement target = targets[i];
                int2 min = MinCell(target.BoundsMin);
                int2 max = MaxCell(target.BoundsMax);
                targetCellCapacity += ((max.x - min.x) + 1) * ((max.y - min.y) + 1);
            }

            var occupiedTargetCells = new NativeParallelMultiHashMap<long, int>(math.max(1, targetCellCapacity), Allocator.TempJob);
            for (int i = 0; i < targets.Length; i++)
            {
                CombatTargetElement target = targets[i];
                int2 min = MinCell(target.BoundsMin);
                int2 max = MaxCell(target.BoundsMax);
                for (int y = min.y; y <= max.y; y++)
                {
                    for (int x = min.x; x <= max.x; x++)
                    {
                        occupiedTargetCells.Add(CellKey(target.Faction, x, y), i);
                    }
                }
            }

            var expansion = state.World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
            var pendingDamage = new NativeStream(activeAoeCount, Allocator.TempJob);
            var vfxPending = new NativeStream(activeAoeCount, Allocator.TempJob);
            var job = new AoeCollisionJob
            {
                Targets = targets,
                OccupiedTargetCells = occupiedTargetCells,
                PendingDamage = pendingDamage.AsWriter(),
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

            JobHandle hitFlushHandle = new CombatHitFlushJob
            {
                Scope = scope,
                PendingDamage = pendingDamage,
                Damage = SystemAPI.GetBufferLookup<CombatDamageElement>()
            }.Schedule(collisionHandle);
            JobHandle vfxFlushHandle = new VfxStreamFlushJob
            {
                Scope = scope,
                Pending = vfxPending,
                VfxBuffers = SystemAPI.GetBufferLookup<VfxSpawnRequestElement>()
            }.Schedule(collisionHandle);

            JobHandle disposeDamageHandle = pendingDamage.Dispose(hitFlushHandle);
            JobHandle disposeVfxHandle = vfxPending.Dispose(vfxFlushHandle);
            state.Dependency = occupiedTargetCells.Dispose(
                JobHandle.CombineDependencies(disposeDamageHandle, disposeVfxHandle));
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(Active), typeof(AoeCollisionActiveTag))]
        private partial struct AoeCollisionJob : IJobEntity
        {
            [ReadOnly] public DynamicBuffer<CombatTargetElement> Targets;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> OccupiedTargetCells;
            public NativeStream.Writer PendingDamage;
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
                EnabledRefRW<CombatRenderActiveTag> renderActive,
                DynamicBuffer<AoeContactGateElement> contactGates)
            {
                NativeStream.Writer pendingDamage = PendingDamage;
                NativeStream.Writer vfxPending = VfxPending;
                pendingDamage.BeginForEachIndex(entityIndexInQuery);
                vfxPending.BeginForEachIndex(entityIndexInQuery);

                if (identity.Faction == CombatFaction.None)
                {
                    Deactivate(active, renderActive);
                    EndStreams(ref pendingDamage, ref vfxPending);
                    return;
                }

                var candidates = new NativeHashSet<int>(4, Allocator.Temp);
                CollectCandidates(identity, collision, ref candidates);

                if (!candidates.IsEmpty)
                {
                    DynamicBuffer<CombatTargetElement> targets = Targets;
                    bool hitVfxEmitted = false;
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

                        float cooldown = !lifetimeEnabled.ValueRO ? 0f : hitGate.RepeatHitCooldownSeconds;
                        ResolveHit(
                            identity,
                            kinematics,
                            hitSpawn,
                            area,
                            target,
                            contactGates,
                            cooldown,
                            ref pendingDamage,
                            ref vfxPending,
                            ref hitVfxEmitted);
                    }
                }

                candidates.Dispose();

                if (!lifetimeEnabled.ValueRO)
                {
                    Deactivate(active, renderActive);
                }

                EndStreams(ref pendingDamage, ref vfxPending);
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
                ref NativeStream.Writer vfxPending,
                ref bool hitVfxEmitted)
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
                EmitHit(identity, kinematics, hitSpawn, area, target, ref pendingDamage, ref vfxPending, ref hitVfxEmitted);
            }

            private void EmitHit(
                AoeIdentityComponent identity,
                CombatKinematicsComponent kinematics,
                AoeHitSpawnComponent hitSpawn,
                AoeAreaComponent area,
                CombatTargetElement target,
                ref NativeStream.Writer pendingDamage,
                ref NativeStream.Writer vfxPending,
                ref bool hitVfxEmitted)
            {
                if (HasDamageEvent(hitSpawn))
                {
                    pendingDamage.Write(new DamageReplayEvent
                    {
                        Faction = identity.Faction,
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

                if (hitSpawn.ProjectileBurst.Enabled)
                {
                    ProjectileEventWriter.Enqueue(ProjectileSpawnPipeline.BuildBurstEvent(
                        identity.Faction, identity.AoeId, identity.TypeId, target.TargetId,
                        kinematics.Position, target.Position,
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
                EnabledRefRW<CombatRenderActiveTag> renderActive)
            {
                active.ValueRW = false;
                renderActive.ValueRW = false;
            }

            private static void EndStreams(
                ref NativeStream.Writer pendingDamage,
                ref NativeStream.Writer vfxPending)
            {
                pendingDamage.EndForEachIndex();
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
                        long key = CellKey(identity.Faction, cx, cy);
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
