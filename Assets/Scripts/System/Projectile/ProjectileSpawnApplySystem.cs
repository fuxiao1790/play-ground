using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Stats;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSpawnExpansionSystem))]
    [UpdateAfter(typeof(AoeSpawnExpansionSystem))]
    public sealed partial class ProjectileSpawnApplySystem : SystemBase
    {
        private static readonly ProfilerMarker SpawnMarker = new("ProjectileSpawnApplySystem");
        private static readonly ProfilerMarker CompleteDependencyMarker =
            new("ProjectileSpawnApplySystem.CompleteDependency");
        private static readonly ProfilerMarker DrainCommandsMarker =
            new("ProjectileSpawnApplySystem.DrainCommands");
        private static readonly ProfilerMarker ReuseJobMarker =
            new("ProjectileSpawnApplySystem.ReuseJob");
        private static readonly ProfilerMarker ColdCreateMarker =
            new("ProjectileSpawnApplySystem.ColdCreate");
        private static readonly ProfilerCounterValue<int> SpawnColdCreateCounter =
            new(ProfilerCategory.Scripts, "ProjectileSpawnApplySystem.Cold", ProfilerMarkerDataUnit.Count);
        private static readonly ProfilerCounterValue<int> SpawnReuseCounter =
            new(ProfilerCategory.Scripts, "ProjectileSpawnApplySystem.Reuse", ProfilerMarkerDataUnit.Count);

        internal int LastColdCreateCount;
        internal int LastReuseCount;

        private EntityArchetype _archetype;
        private EntityQuery _deadSlotQuery;

        protected override void OnCreate()
        {
            _archetype = EntityManager.CreateArchetype(
                typeof(ProjectileTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatLifetimeComponent),
                typeof(ProjectileHitComponent),
                typeof(ProjectileTrackingComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderKindId),
                typeof(Active),
                typeof(ProjectileCollisionActiveTag),
                typeof(CombatRenderActiveTag),
                typeof(ProjectileContactGateElement),
                typeof(TimedSpawnComponent),
                typeof(TimedSpawnStateComponent));

            _deadSlotQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ProjectileTag>()
                .WithDisabled<Active>()
                .Build(this);
        }

        protected override void OnDestroy()
        {
            Dependency.Complete();
        }

        protected override void OnUpdate()
        {
            using (CompleteDependencyMarker.Auto())
            {
                Dependency.Complete();
            }

            int totalRequests = 0;
            NativeArray<ProjectileSpawnCommand> commands = default;
            using (DrainCommandsMarker.Auto())
            {
                var expansionSys = World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
                if (expansionSys != null)
                {
                    expansionSys.PendingHandle.Complete();
                    if (expansionSys.ProjectileCommands.IsCreated)
                    {
                        commands = expansionSys.ProjectileCommands.AsArray();
                        totalRequests = commands.Length;
                    }
                }
            }

            if (totalRequests == 0)
            {
                LastReuseCount = 0;
                LastColdCreateCount = 0;
                return;
            }

            using (SpawnMarker.Auto())
            {
                using var createEcb = new EntityCommandBuffer(Allocator.Temp);
                int reuseCount = 0;
                int coldCreateCount = 0;

                using (ReuseJobMarker.Auto())
                {
                    using NativeArray<ArchetypeChunk> chunks =
                        _deadSlotQuery.ToArchetypeChunkArray(Allocator.TempJob);
                    using var reused = new NativeReference<int>(Allocator.TempJob);

                    JobHandle spawnHandle = new ProjectileSpawnJob
                    {
                        Commands = commands,
                        Chunks = chunks,
                        ReuseCount = reused,
                        ActiveHandle = GetComponentTypeHandle<Active>(false),
                        CollisionActiveHandle = GetComponentTypeHandle<ProjectileCollisionActiveTag>(false),
                        RenderActiveHandle = GetComponentTypeHandle<CombatRenderActiveTag>(false),
                        IdentityHandle = GetComponentTypeHandle<ProjectileIdentityComponent>(false),
                        KinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                        CollisionHandle = GetComponentTypeHandle<CombatCollisionComponent>(false),
                        LifetimeHandle = GetComponentTypeHandle<CombatLifetimeComponent>(false),
                        HitHandle = GetComponentTypeHandle<ProjectileHitComponent>(false),
                        TrackingHandle = GetComponentTypeHandle<ProjectileTrackingComponent>(false),
                        RenderHandle = GetComponentTypeHandle<CombatRenderComponent>(false),
                        RenderBatchIdHandle = GetComponentTypeHandle<CombatRenderKindId>(false),
                        ContactGateHandle = GetBufferTypeHandle<ProjectileContactGateElement>(false),
                        TimedSpawnHandle = GetComponentTypeHandle<TimedSpawnComponent>(false),
                        TimedSpawnStateHandle = GetComponentTypeHandle<TimedSpawnStateComponent>(false),
                    }.Schedule(default);

                    spawnHandle.Complete();
                    reuseCount = reused.Value;

                    using (ColdCreateMarker.Auto())
                    {
                        for (int i = reuseCount; i < commands.Length; i++)
                        {
                            CreateProjectileEntity(commands[i], createEcb);
                            coldCreateCount++;
                        }
                    }
                }

                if (coldCreateCount > 0)
                {
                    createEcb.Playback(EntityManager);
                }

                SpawnReuseCounter.Value = reuseCount;
                SpawnColdCreateCounter.Value = totalRequests - reuseCount;
                LastReuseCount = reuseCount;
                LastColdCreateCount = totalRequests - reuseCount;

                if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(out RefRW<CombatStatsSingleton> stats))
                {
                    stats.ValueRW.EntitiesSpawnedViaReuse += LastReuseCount;
                    stats.ValueRW.EntitiesSpawnedViaEcb += LastColdCreateCount;
                }
            }

        }

        private void CreateProjectileEntity(ProjectileSpawnCommand cmd, EntityCommandBuffer ecb)
        {
            Entity entity = ecb.CreateEntity(_archetype);
            RecordCommonProjectileReset(ecb, entity, cmd.Faction, cmd);
            RecordTimedSpawnReset(ecb, entity, cmd);
        }

        private static void RecordCommonProjectileReset(
            EntityCommandBuffer ecb,
            Entity entity,
            CombatFaction faction,
            ProjectileSpawnCommand cmd)
        {
            ecb.SetComponent(entity, new ProjectileIdentityComponent
            {
                Faction = faction,
                ProjectileId = cmd.ProjectileId,
                TypeId = cmd.TypeId
            });
            ecb.SetComponent(entity, new CombatKinematicsComponent
            {
                Position = cmd.Position,
                Velocity = cmd.Velocity
            });
            ecb.SetComponent(entity, new CombatCollisionComponent
            {
                ShapeType = cmd.ShapeType,
                Radius = cmd.Radius,
                HalfExtents = cmd.HalfExtents,
                RotationRadians = cmd.RotationRadians,
                BoundsMin = cmd.BoundsMin,
                BoundsMax = cmd.BoundsMax
            });
            ecb.SetComponent(entity, new CombatLifetimeComponent { Remaining = cmd.Lifetime });
            ecb.SetComponentEnabled<CombatLifetimeComponent>(entity, true);
            ecb.SetComponent(entity, new ProjectileHitComponent
            {
                PierceRemaining = cmd.PierceRemaining,
                RepeatHitCooldownSeconds = cmd.RepeatHitCooldownSeconds,
                HitPayload = HitPayloadFor(in cmd, faction)
            });
            ecb.SetComponent(entity, cmd.Tracking);
            ecb.SetComponentEnabled<ProjectileTrackingComponent>(entity, cmd.Tracking.TrackingEnabled);
            ecb.SetComponent(entity, cmd.Render);
            ecb.SetComponent(entity, new CombatRenderKindId { Value = cmd.RenderTypeId });

            if (cmd.SeedContactGateTargetId > 0)
            {
                ecb.AppendToBuffer(entity, new ProjectileContactGateElement
                {
                    TargetId = cmd.SeedContactGateTargetId,
                    CooldownRemaining = math.max(0.1f, cmd.RepeatHitCooldownSeconds)
                });
            }

            ecb.SetComponentEnabled<Active>(entity, true);
            ecb.SetComponentEnabled<ProjectileCollisionActiveTag>(entity, NeedsCollision(cmd.HitPayload));
            ecb.SetComponentEnabled<CombatRenderActiveTag>(entity, true);
        }

        private static void RecordTimedSpawnReset(
            EntityCommandBuffer ecb,
            Entity entity,
            in ProjectileSpawnCommand cmd)
        {
            bool hasTimedSpawner = cmd.HasTimedSpawner != 0;
            ecb.SetComponent(entity, hasTimedSpawner ? cmd.TimedSpawn : default);
            ecb.SetComponent(entity, hasTimedSpawner ? InitialTimedSpawnStateFor(cmd) : default);
            ecb.SetComponentEnabled<TimedSpawnComponent>(entity, hasTimedSpawner);
        }

        private static bool NeedsCollision(in ProjectileHitPayload payload) =>
            payload.DirectDamageEnabled
            || payload.StackEffect.Enabled
            || payload.OnHitSpawn.Enabled;

        internal static ProjectileHitPayload HitPayloadFor(in ProjectileSpawnCommand cmd, CombatFaction faction)
        {
            CombatHitPayload hitPayload = cmd.HitPayload.HitPayload;
            StackEffectSnapshot stack = hitPayload.StackEffect;
            stack.Faction = faction;
            hitPayload.StackEffect = stack;
            return new ProjectileHitPayload(hitPayload, cmd.HitPayload.OnHitSpawn);
        }

        private static TimedSpawnStateComponent InitialTimedSpawnStateFor(in ProjectileSpawnCommand cmd)
        {
            TimedSpawnComponent timedSpawn = cmd.TimedSpawn;
            return new TimedSpawnStateComponent
            {
                CooldownRemaining = timedSpawn.IntervalSeconds
                    + DeterministicJitter(
                        cmd.ProjectileId,
                        timedSpawn.JitterSeed,
                        timedSpawn.IntervalJitterSeconds),
                TickIndex = 0
            };
        }

        private static float DeterministicJitter(int projectileId, int jitterSeed, float maxOffsetSeconds)
        {
            if (maxOffsetSeconds <= 0f)
            {
                return 0f;
            }

            unchecked
            {
                uint hash = (uint)projectileId;
                hash = (hash * 397u) ^ (uint)jitterSeed;
                hash *= 0x9E3779B9u;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
                return ((hash & 0x00FFFFFFu) + 1u) / 16777217f * maxOffsetSeconds;
            }
        }

        [BurstCompile]
        private struct ProjectileSpawnJob : IJob
        {
            [ReadOnly] public NativeArray<ProjectileSpawnCommand> Commands;
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            public NativeReference<int> ReuseCount;

            public ComponentTypeHandle<Active> ActiveHandle;
            public ComponentTypeHandle<ProjectileCollisionActiveTag> CollisionActiveHandle;
            public ComponentTypeHandle<CombatRenderActiveTag> RenderActiveHandle;
            public ComponentTypeHandle<ProjectileIdentityComponent> IdentityHandle;
            public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            public ComponentTypeHandle<CombatCollisionComponent> CollisionHandle;
            public ComponentTypeHandle<CombatLifetimeComponent> LifetimeHandle;
            public ComponentTypeHandle<ProjectileHitComponent> HitHandle;
            public ComponentTypeHandle<ProjectileTrackingComponent> TrackingHandle;
            public ComponentTypeHandle<CombatRenderComponent> RenderHandle;
            public ComponentTypeHandle<CombatRenderKindId> RenderBatchIdHandle;
            public BufferTypeHandle<ProjectileContactGateElement> ContactGateHandle;
            public ComponentTypeHandle<TimedSpawnComponent> TimedSpawnHandle;
            public ComponentTypeHandle<TimedSpawnStateComponent> TimedSpawnStateHandle;

            public void Execute()
            {
                int commandIndex = 0;

                for (int chunkIndex = 0;
                     chunkIndex < Chunks.Length && commandIndex < Commands.Length;
                     chunkIndex++)
                {
                    ArchetypeChunk chunk = Chunks[chunkIndex];
                    EnabledMask activeMask = chunk.GetEnabledMask(ref ActiveHandle);
                    EnabledMask collisionActiveMask = chunk.GetEnabledMask(ref CollisionActiveHandle);
                    EnabledMask renderActiveMask = chunk.GetEnabledMask(ref RenderActiveHandle);
                    EnabledMask trackingMask = chunk.GetEnabledMask(ref TrackingHandle);
                    EnabledMask lifetimeMask = chunk.GetEnabledMask(ref LifetimeHandle);
                    EnabledMask timedSpawnMask = chunk.GetEnabledMask(ref TimedSpawnHandle);

                    NativeArray<ProjectileIdentityComponent> identities =
                        chunk.GetNativeArray(ref IdentityHandle);
                    NativeArray<CombatKinematicsComponent> kinematics =
                        chunk.GetNativeArray(ref KinematicsHandle);
                    NativeArray<CombatCollisionComponent> collisions =
                        chunk.GetNativeArray(ref CollisionHandle);
                    NativeArray<CombatLifetimeComponent> lifetimes =
                        chunk.GetNativeArray(ref LifetimeHandle);
                    NativeArray<ProjectileHitComponent> hits = chunk.GetNativeArray(ref HitHandle);
                    NativeArray<ProjectileTrackingComponent> tracking =
                        chunk.GetNativeArray(ref TrackingHandle);
                    NativeArray<CombatRenderComponent> renders =
                        chunk.GetNativeArray(ref RenderHandle);
                    NativeArray<CombatRenderKindId> batchIds =
                        chunk.GetNativeArray(ref RenderBatchIdHandle);
                    BufferAccessor<ProjectileContactGateElement> gates =
                        chunk.GetBufferAccessor(ref ContactGateHandle);
                    NativeArray<TimedSpawnComponent> timedSpawns =
                        chunk.GetNativeArray(ref TimedSpawnHandle);
                    NativeArray<TimedSpawnStateComponent> timedSpawnStates =
                        chunk.GetNativeArray(ref TimedSpawnStateHandle);

                    for (int i = 0; i < chunk.Count && commandIndex < Commands.Length; i++)
                    {
                        if (activeMask[i])
                        {
                            continue;
                        }

                        ProjectileSpawnCommand cfg = Commands[commandIndex++];

                        identities[i] = new ProjectileIdentityComponent
                        {
                            Faction = cfg.Faction,
                            ProjectileId = cfg.ProjectileId,
                            TypeId = cfg.TypeId
                        };
                        kinematics[i] = new CombatKinematicsComponent
                        {
                            Position = cfg.Position,
                            Velocity = cfg.Velocity
                        };
                        collisions[i] = new CombatCollisionComponent
                        {
                            ShapeType = cfg.ShapeType,
                            Radius = cfg.Radius,
                            HalfExtents = cfg.HalfExtents,
                            RotationRadians = cfg.RotationRadians,
                            BoundsMin = cfg.BoundsMin,
                            BoundsMax = cfg.BoundsMax
                        };
                        lifetimes[i] = new CombatLifetimeComponent { Remaining = cfg.Lifetime };
                        lifetimeMask[i] = true;
                        hits[i] = new ProjectileHitComponent
                        {
                            PierceRemaining = cfg.PierceRemaining,
                            RepeatHitCooldownSeconds = cfg.RepeatHitCooldownSeconds,
                            HitPayload = HitPayloadFor(in cfg, cfg.Faction)
                        };
                        tracking[i] = cfg.Tracking;
                        trackingMask[i] = cfg.Tracking.TrackingEnabled;
                        renders[i] = cfg.Render;
                        batchIds[i] = new CombatRenderKindId { Value = cfg.RenderTypeId };

                        DynamicBuffer<ProjectileContactGateElement> gate = gates[i];
                        gate.Clear();
                        if (cfg.SeedContactGateTargetId > 0)
                        {
                            gate.Add(new ProjectileContactGateElement
                            {
                                TargetId = cfg.SeedContactGateTargetId,
                                CooldownRemaining = math.max(0.1f, cfg.RepeatHitCooldownSeconds)
                            });
                        }

                        bool hasTimedSpawner = cfg.HasTimedSpawner != 0;
                        timedSpawns[i] = hasTimedSpawner ? cfg.TimedSpawn : default;
                        timedSpawnStates[i] = hasTimedSpawner ? InitialTimedSpawnStateFor(cfg) : default;
                        timedSpawnMask[i] = hasTimedSpawner;

                        activeMask[i] = true;
                        collisionActiveMask[i] = NeedsCollision(cfg.HitPayload);
                        renderActiveMask[i] = true;
                    }
                }

                ReuseCount.Value = commandIndex;
            }
        }
    }
}
