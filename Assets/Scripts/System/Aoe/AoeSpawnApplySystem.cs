using PlayGround.System.Common;
using PlayGround.System.Projectile;
using PlayGround.System.Stats;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace PlayGround.System.Aoe
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ImpactAoeSpawnExpansionSystem))]
    [UpdateAfter(typeof(ProjectileSpawnExpansionSystem))]
    public partial class ImpactAoeSpawnApplySystem : SystemBase
    {
        private static readonly ProfilerMarker SpawnMarker = new("ImpactAoeSpawnApplySystem");
        private static readonly ProfilerMarker ReuseJobMarker = new("ImpactAoeSpawnApplySystem.ReuseJob");
        private static readonly ProfilerCounterValue<int> SpawnColdCreateCounter =
            new(ProfilerCategory.Scripts, "ImpactAoeSpawnApplySystem.Cold", ProfilerMarkerDataUnit.Count);
        private static readonly ProfilerCounterValue<int> SpawnReuseCounter =
            new(ProfilerCategory.Scripts, "ImpactAoeSpawnApplySystem.Reuse", ProfilerMarkerDataUnit.Count);

        internal int LastColdCreateCount;
        internal int LastReuseCount;

        private EntityArchetype _impactArchetype;
        private EntityQuery _deadSlotQuery;

        protected override void OnCreate()
        {
            _impactArchetype = EntityManager.CreateArchetype(
                typeof(AoeTag),
                typeof(AoeIdentityComponent),
                typeof(AoeHitGateComponent),
                typeof(AoeHitSpawnComponent),
                typeof(AoeAreaComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderAuthoring),
                typeof(CombatRenderKindId),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(Active),
                typeof(AoeCollisionActiveTag),
                typeof(CombatRenderActiveTag));

            _deadSlotQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithDisabled<Active>()
                .WithNone<CombatLifetimeComponent>()
                .Build(this);
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();

            NativeArray<AoeSpawnCommand> commands = default;
            int totalRequests = 0;
            if (SystemAPI.TryGetSingleton<ImpactAoeSpawnEventSingleton>(
                out ImpactAoeSpawnEventSingleton impactLane))
            {
                impactLane.PendingHandle.Complete();
                if (impactLane.Commands.IsCreated)
                {
                    commands = impactLane.Commands.AsArray();
                    totalRequests = commands.Length;
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

                    new ImpactAoeSpawnJob
                    {
                        Configs = commands,
                        Chunks = chunks,
                        ReuseCount = reused,
                        ActiveHandle = GetComponentTypeHandle<Active>(false),
                        CollisionActiveHandle = GetComponentTypeHandle<AoeCollisionActiveTag>(false),
                        RenderActiveHandle = GetComponentTypeHandle<CombatRenderActiveTag>(false),
                        IdentityHandle = GetComponentTypeHandle<AoeIdentityComponent>(false),
                        KinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                        CollisionHandle = GetComponentTypeHandle<CombatCollisionComponent>(false),
                        HitGateHandle = GetComponentTypeHandle<AoeHitGateComponent>(false),
                        HitSpawnHandle = GetComponentTypeHandle<AoeHitSpawnComponent>(false),
                        AreaHandle = GetComponentTypeHandle<AoeAreaComponent>(false),
                        RenderHandle = GetComponentTypeHandle<CombatRenderComponent>(false),
                        AuthoringHandle = GetComponentTypeHandle<CombatRenderAuthoring>(false),
                        RenderBatchIdHandle = GetComponentTypeHandle<CombatRenderKindId>(false),
                    }.Schedule(default).Complete();

                    reuseCount = reused.Value;
                    for (int i = reuseCount; i < commands.Length; i++)
                    {
                        Entity entity = createEcb.CreateEntity(_impactArchetype);
                        AoeSpawnApplyUtility.RecordImpactReset(createEcb, entity, commands[i]);
                        coldCreateCount++;
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

        [BurstCompile]
        private struct ImpactAoeSpawnJob : IJob
        {
            [ReadOnly] public NativeArray<AoeSpawnCommand> Configs;
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            public NativeReference<int> ReuseCount;

            public ComponentTypeHandle<Active> ActiveHandle;
            public ComponentTypeHandle<AoeCollisionActiveTag> CollisionActiveHandle;
            public ComponentTypeHandle<CombatRenderActiveTag> RenderActiveHandle;
            public ComponentTypeHandle<AoeIdentityComponent> IdentityHandle;
            public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            public ComponentTypeHandle<CombatCollisionComponent> CollisionHandle;
            public ComponentTypeHandle<AoeHitGateComponent> HitGateHandle;
            public ComponentTypeHandle<AoeHitSpawnComponent> HitSpawnHandle;
            public ComponentTypeHandle<AoeAreaComponent> AreaHandle;
            public ComponentTypeHandle<CombatRenderComponent> RenderHandle;
            public ComponentTypeHandle<CombatRenderAuthoring> AuthoringHandle;
            public ComponentTypeHandle<CombatRenderKindId> RenderBatchIdHandle;

            public void Execute()
            {
                int commandIndex = 0;

                for (int chunkIndex = 0;
                     chunkIndex < Chunks.Length && commandIndex < Configs.Length;
                     chunkIndex++)
                {
                    ArchetypeChunk chunk = Chunks[chunkIndex];
                    EnabledMask activeMask = chunk.GetEnabledMask(ref ActiveHandle);
                    EnabledMask collisionActiveMask = chunk.GetEnabledMask(ref CollisionActiveHandle);
                    EnabledMask renderActiveMask = chunk.GetEnabledMask(ref RenderActiveHandle);

                    NativeArray<AoeIdentityComponent> identities =
                        chunk.GetNativeArray(ref IdentityHandle);
                    NativeArray<CombatKinematicsComponent> kinematics =
                        chunk.GetNativeArray(ref KinematicsHandle);
                    NativeArray<CombatCollisionComponent> collisions =
                        chunk.GetNativeArray(ref CollisionHandle);
                    NativeArray<AoeHitGateComponent> hitGates =
                        chunk.GetNativeArray(ref HitGateHandle);
                    NativeArray<AoeHitSpawnComponent> hitSpawns =
                        chunk.GetNativeArray(ref HitSpawnHandle);
                    NativeArray<AoeAreaComponent> areas = chunk.GetNativeArray(ref AreaHandle);
                    NativeArray<CombatRenderComponent> renders =
                        chunk.GetNativeArray(ref RenderHandle);
                    NativeArray<CombatRenderAuthoring> authorings =
                        chunk.GetNativeArray(ref AuthoringHandle);
                    NativeArray<CombatRenderKindId> batchIds =
                        chunk.GetNativeArray(ref RenderBatchIdHandle);

                    for (int i = 0; i < chunk.Count && commandIndex < Configs.Length; i++)
                    {
                        if (activeMask[i])
                        {
                            continue;
                        }

                        AoeSpawnCommand cfg = Configs[commandIndex++];
                        AoeSpawnApplyUtility.WriteCommon(
                            cfg,
                            identities,
                            kinematics,
                            collisions,
                            hitGates,
                            hitSpawns,
                            areas,
                            renders,
                            authorings,
                            batchIds,
                            i);

                        bool collisionEnabled = AoeSpawnApplyUtility.NeedsCollision(cfg);
                        activeMask[i] = collisionEnabled;
                        collisionActiveMask[i] = collisionEnabled;
                        renderActiveMask[i] = collisionEnabled;
                    }
                }

                ReuseCount.Value = commandIndex;
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(LingeringAoeSpawnExpansionSystem))]
    [UpdateAfter(typeof(ProjectileSpawnExpansionSystem))]
    public partial class LingeringAoeSpawnApplySystem : SystemBase
    {
        private static readonly ProfilerMarker SpawnMarker = new("LingeringAoeSpawnApplySystem");
        private static readonly ProfilerMarker ReuseJobMarker = new("LingeringAoeSpawnApplySystem.ReuseJob");
        private static readonly ProfilerCounterValue<int> SpawnColdCreateCounter =
            new(ProfilerCategory.Scripts, "LingeringAoeSpawnApplySystem.Cold", ProfilerMarkerDataUnit.Count);
        private static readonly ProfilerCounterValue<int> SpawnReuseCounter =
            new(ProfilerCategory.Scripts, "LingeringAoeSpawnApplySystem.Reuse", ProfilerMarkerDataUnit.Count);

        internal int LastColdCreateCount;
        internal int LastReuseCount;

        private EntityArchetype _lingeringArchetype;
        private EntityQuery _deadSlotQuery;

        protected override void OnCreate()
        {
            _lingeringArchetype = EntityManager.CreateArchetype(
                typeof(AoeTag),
                typeof(AoeIdentityComponent),
                typeof(CombatLifetimeComponent),
                typeof(AoeHitGateComponent),
                typeof(AoeHitSpawnComponent),
                typeof(AoeAreaComponent),
                typeof(AoePulseVfxComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderAuthoring),
                typeof(CombatRenderKindId),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(Active),
                typeof(AoeCollisionActiveTag),
                typeof(CombatRenderActiveTag),
                typeof(AoeContactGateElement),
                typeof(TimedSpawnComponent),
                typeof(TimedSpawnStateComponent));

            _deadSlotQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithAll<CombatLifetimeComponent>()
                .WithDisabled<Active>()
                .Build(this);
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();

            NativeArray<AoeSpawnCommand> commands = default;
            int totalRequests = 0;
            if (SystemAPI.TryGetSingleton<LingeringAoeSpawnEventSingleton>(
                out LingeringAoeSpawnEventSingleton lingeringLane))
            {
                lingeringLane.PendingHandle.Complete();
                if (lingeringLane.Commands.IsCreated)
                {
                    commands = lingeringLane.Commands.AsArray();
                    totalRequests = commands.Length;
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

                    new LingeringAoeSpawnJob
                    {
                        Configs = commands,
                        Chunks = chunks,
                        ReuseCount = reused,
                        ActiveHandle = GetComponentTypeHandle<Active>(false),
                        CollisionActiveHandle = GetComponentTypeHandle<AoeCollisionActiveTag>(false),
                        RenderActiveHandle = GetComponentTypeHandle<CombatRenderActiveTag>(false),
                        IdentityHandle = GetComponentTypeHandle<AoeIdentityComponent>(false),
                        KinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                        CollisionHandle = GetComponentTypeHandle<CombatCollisionComponent>(false),
                        LifetimeHandle = GetComponentTypeHandle<CombatLifetimeComponent>(false),
                        HitGateHandle = GetComponentTypeHandle<AoeHitGateComponent>(false),
                        HitSpawnHandle = GetComponentTypeHandle<AoeHitSpawnComponent>(false),
                        AreaHandle = GetComponentTypeHandle<AoeAreaComponent>(false),
                        PulseVfxHandle = GetComponentTypeHandle<AoePulseVfxComponent>(false),
                        RenderHandle = GetComponentTypeHandle<CombatRenderComponent>(false),
                        AuthoringHandle = GetComponentTypeHandle<CombatRenderAuthoring>(false),
                        RenderBatchIdHandle = GetComponentTypeHandle<CombatRenderKindId>(false),
                        ContactGateHandle = GetBufferTypeHandle<AoeContactGateElement>(false),
                        TimedSpawnHandle = GetComponentTypeHandle<TimedSpawnComponent>(false),
                        TimedSpawnStateHandle = GetComponentTypeHandle<TimedSpawnStateComponent>(false),
                    }.Schedule(default).Complete();

                    reuseCount = reused.Value;
                    for (int i = reuseCount; i < commands.Length; i++)
                    {
                        Entity entity = createEcb.CreateEntity(_lingeringArchetype);
                        AoeSpawnApplyUtility.RecordLingeringReset(createEcb, entity, commands[i]);
                        coldCreateCount++;
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

        [BurstCompile]
        private struct LingeringAoeSpawnJob : IJob
        {
            [ReadOnly] public NativeArray<AoeSpawnCommand> Configs;
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            public NativeReference<int> ReuseCount;

            public ComponentTypeHandle<Active> ActiveHandle;
            public ComponentTypeHandle<AoeCollisionActiveTag> CollisionActiveHandle;
            public ComponentTypeHandle<CombatRenderActiveTag> RenderActiveHandle;
            public ComponentTypeHandle<AoeIdentityComponent> IdentityHandle;
            public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            public ComponentTypeHandle<CombatCollisionComponent> CollisionHandle;
            public ComponentTypeHandle<CombatLifetimeComponent> LifetimeHandle;
            public ComponentTypeHandle<AoeHitGateComponent> HitGateHandle;
            public ComponentTypeHandle<AoeHitSpawnComponent> HitSpawnHandle;
            public ComponentTypeHandle<AoeAreaComponent> AreaHandle;
            public ComponentTypeHandle<AoePulseVfxComponent> PulseVfxHandle;
            public ComponentTypeHandle<CombatRenderComponent> RenderHandle;
            public ComponentTypeHandle<CombatRenderAuthoring> AuthoringHandle;
            public ComponentTypeHandle<CombatRenderKindId> RenderBatchIdHandle;
            public BufferTypeHandle<AoeContactGateElement> ContactGateHandle;
            public ComponentTypeHandle<TimedSpawnComponent> TimedSpawnHandle;
            public ComponentTypeHandle<TimedSpawnStateComponent> TimedSpawnStateHandle;

            public void Execute()
            {
                int commandIndex = 0;

                for (int chunkIndex = 0;
                     chunkIndex < Chunks.Length && commandIndex < Configs.Length;
                     chunkIndex++)
                {
                    ArchetypeChunk chunk = Chunks[chunkIndex];
                    EnabledMask activeMask = chunk.GetEnabledMask(ref ActiveHandle);
                    EnabledMask collisionActiveMask = chunk.GetEnabledMask(ref CollisionActiveHandle);
                    EnabledMask renderActiveMask = chunk.GetEnabledMask(ref RenderActiveHandle);
                    EnabledMask lifetimeMask = chunk.GetEnabledMask(ref LifetimeHandle);
                    EnabledMask timedSpawnMask = chunk.GetEnabledMask(ref TimedSpawnHandle);

                    NativeArray<AoeIdentityComponent> identities =
                        chunk.GetNativeArray(ref IdentityHandle);
                    NativeArray<CombatKinematicsComponent> kinematics =
                        chunk.GetNativeArray(ref KinematicsHandle);
                    NativeArray<CombatCollisionComponent> collisions =
                        chunk.GetNativeArray(ref CollisionHandle);
                    NativeArray<CombatLifetimeComponent> lifetimes =
                        chunk.GetNativeArray(ref LifetimeHandle);
                    NativeArray<AoeHitGateComponent> hitGates =
                        chunk.GetNativeArray(ref HitGateHandle);
                    NativeArray<AoeHitSpawnComponent> hitSpawns =
                        chunk.GetNativeArray(ref HitSpawnHandle);
                    NativeArray<AoeAreaComponent> areas = chunk.GetNativeArray(ref AreaHandle);
                    NativeArray<AoePulseVfxComponent> pulseVfxs =
                        chunk.GetNativeArray(ref PulseVfxHandle);
                    NativeArray<CombatRenderComponent> renders =
                        chunk.GetNativeArray(ref RenderHandle);
                    NativeArray<CombatRenderAuthoring> authorings =
                        chunk.GetNativeArray(ref AuthoringHandle);
                    NativeArray<CombatRenderKindId> batchIds =
                        chunk.GetNativeArray(ref RenderBatchIdHandle);
                    BufferAccessor<AoeContactGateElement> gates =
                        chunk.GetBufferAccessor(ref ContactGateHandle);
                    NativeArray<TimedSpawnComponent> timedSpawns =
                        chunk.GetNativeArray(ref TimedSpawnHandle);
                    NativeArray<TimedSpawnStateComponent> timedSpawnStates =
                        chunk.GetNativeArray(ref TimedSpawnStateHandle);

                    for (int i = 0; i < chunk.Count && commandIndex < Configs.Length; i++)
                    {
                        if (activeMask[i])
                        {
                            continue;
                        }

                        AoeSpawnCommand cfg = Configs[commandIndex++];
                        AoeSpawnApplyUtility.WriteCommon(
                            cfg,
                            identities,
                            kinematics,
                            collisions,
                            hitGates,
                            hitSpawns,
                            areas,
                            renders,
                            authorings,
                            batchIds,
                            i);

                        lifetimes[i] = new CombatLifetimeComponent { Remaining = cfg.Lifetime };
                        lifetimeMask[i] = true;
                        pulseVfxs[i] = AoeSpawnApplyUtility.PulseVfxFor(cfg);
                        gates[i].Clear();

                        bool hasTimedSpawner = AoeSpawnApplyUtility.HasTimedSpawner(cfg);
                        timedSpawns[i] = hasTimedSpawner ? cfg.TimedSpawn : default;
                        timedSpawnStates[i] = hasTimedSpawner
                            ? AoeSpawnApplyUtility.InitialTimedSpawnStateFor(cfg)
                            : default;
                        timedSpawnMask[i] = hasTimedSpawner;

                        bool collisionEnabled = AoeSpawnApplyUtility.NeedsCollision(cfg);
                        activeMask[i] = true;
                        collisionActiveMask[i] = collisionEnabled;
                        renderActiveMask[i] = true;
                    }
                }

                ReuseCount.Value = commandIndex;
            }
        }
    }

    internal static class AoeSpawnApplyUtility
    {
        public static void RecordImpactReset(
            EntityCommandBuffer ecb,
            Entity entity,
            in AoeSpawnCommand cmd)
        {
            CombatKinematicsComponent kinematics = KinematicsFor(cmd);
            CombatRenderComponent render = cmd.Render;
            ecb.SetComponent(entity, IdentityFor(cmd));
            ecb.SetComponent(entity, kinematics);
            ecb.SetComponent(entity, CollisionFor(cmd));
            ecb.SetComponent(entity, HitGateFor(cmd));
            ecb.SetComponent(entity, HitSpawnFor(cmd));
            ecb.SetComponent(entity, AreaFor(cmd));
            ecb.SetComponent(entity, render);
            ecb.SetComponent(entity, cmd.Authoring);
            ecb.SetComponent(entity, new CombatRenderKindId { Value = cmd.RenderTypeId });
            bool collisionEnabled = NeedsCollision(cmd);
            ecb.SetComponentEnabled<Active>(entity, collisionEnabled);
            ecb.SetComponentEnabled<AoeCollisionActiveTag>(entity, collisionEnabled);
            ecb.SetComponentEnabled<CombatRenderActiveTag>(entity, collisionEnabled);
        }

        public static void RecordLingeringReset(
            EntityCommandBuffer ecb,
            Entity entity,
            in AoeSpawnCommand cmd)
        {
            CombatKinematicsComponent kinematics = KinematicsFor(cmd);
            CombatRenderComponent render = cmd.Render;
            ecb.SetComponent(entity, IdentityFor(cmd));
            ecb.SetComponent(entity, kinematics);
            ecb.SetComponent(entity, CollisionFor(cmd));
            ecb.SetComponent(entity, HitGateFor(cmd));
            ecb.SetComponent(entity, HitSpawnFor(cmd));
            ecb.SetComponent(entity, AreaFor(cmd));
            ecb.SetComponent(entity, new CombatLifetimeComponent { Remaining = cmd.Lifetime });
            ecb.SetComponentEnabled<CombatLifetimeComponent>(entity, true);
            ecb.SetComponent(entity, PulseVfxFor(cmd));
            bool hasTimedSpawner = HasTimedSpawner(cmd);
            ecb.SetComponent(entity, hasTimedSpawner ? cmd.TimedSpawn : default);
            ecb.SetComponent(entity, hasTimedSpawner ? InitialTimedSpawnStateFor(cmd) : default);
            ecb.SetComponentEnabled<TimedSpawnComponent>(entity, hasTimedSpawner);
            ecb.SetComponent(entity, render);
            ecb.SetComponent(entity, cmd.Authoring);
            ecb.SetComponent(entity, new CombatRenderKindId { Value = cmd.RenderTypeId });
            bool collisionEnabled = NeedsCollision(cmd);
            ecb.SetComponentEnabled<Active>(entity, true);
            ecb.SetComponentEnabled<AoeCollisionActiveTag>(entity, collisionEnabled);
            ecb.SetComponentEnabled<CombatRenderActiveTag>(entity, true);
        }

        public static void WriteCommon(
            in AoeSpawnCommand cfg,
            NativeArray<AoeIdentityComponent> identities,
            NativeArray<CombatKinematicsComponent> kinematics,
            NativeArray<CombatCollisionComponent> collisions,
            NativeArray<AoeHitGateComponent> hitGates,
            NativeArray<AoeHitSpawnComponent> hitSpawns,
            NativeArray<AoeAreaComponent> areas,
            NativeArray<CombatRenderComponent> renders,
            NativeArray<CombatRenderAuthoring> authorings,
            NativeArray<CombatRenderKindId> batchIds,
            int index)
        {
            CombatKinematicsComponent kin = KinematicsFor(cfg);
            CombatRenderComponent render = cfg.Render;
            identities[index] = IdentityFor(cfg);
            kinematics[index] = kin;
            collisions[index] = CollisionFor(cfg);
            hitGates[index] = HitGateFor(cfg);
            hitSpawns[index] = HitSpawnFor(cfg);
            areas[index] = AreaFor(cfg);
            renders[index] = render;
            authorings[index] = cfg.Authoring;
            batchIds[index] = new CombatRenderKindId { Value = cfg.RenderTypeId };
        }

        public static bool NeedsCollision(in AoeSpawnCommand cmd) =>
            cmd.HitPayload.DirectDamageEnabled
            || cmd.HitPayload.StackEffect.Enabled
            || cmd.OnHitSpawn.Enabled;

        public static bool HasTimedSpawner(in AoeSpawnCommand cmd) =>
            cmd.Lifetime > 0f && cmd.HasTimedSpawner != 0;

        public static TimedSpawnStateComponent InitialTimedSpawnStateFor(in AoeSpawnCommand cmd) =>
            new()
            {
                CooldownRemaining = cmd.TimedSpawn.IntervalSeconds
                    + DeterministicJitter(
                        cmd.AoeId,
                        cmd.TimedSpawn.JitterSeed,
                        cmd.TimedSpawn.IntervalJitterSeconds),
                TickIndex = 0
            };

        public static AoePulseVfxComponent PulseVfxFor(in AoeSpawnCommand cmd)
        {
            float interval = cmd.RepeatHitCooldownSeconds > 0f ? cmd.RepeatHitCooldownSeconds : 0f;
            return new AoePulseVfxComponent { Interval = interval, RemainingInterval = interval };
        }

        private static AoeIdentityComponent IdentityFor(in AoeSpawnCommand cmd) =>
            new() { Faction = cmd.Faction, AoeId = cmd.AoeId, TypeId = cmd.TypeId };

        private static CombatKinematicsComponent KinematicsFor(in AoeSpawnCommand cmd) =>
            new() { Position = cmd.Position, Velocity = default };

        private static CombatCollisionComponent CollisionFor(in AoeSpawnCommand cmd) =>
            new()
            {
                ShapeType = cmd.ShapeType,
                Radius = cmd.Radius,
                HalfExtents = cmd.HalfExtents,
                RotationRadians = cmd.RotationRadians,
                BoundsMin = cmd.BoundsMin,
                BoundsMax = cmd.BoundsMax
            };

        private static AoeHitGateComponent HitGateFor(in AoeSpawnCommand cmd) =>
            new() { RepeatHitCooldownSeconds = cmd.RepeatHitCooldownSeconds };

        private static AoeHitSpawnComponent HitSpawnFor(in AoeSpawnCommand cmd) =>
            new()
            {
                HitPayload = HitPayloadFor(cmd.HitPayload, cmd.Faction),
                OnHitSpawn = cmd.OnHitSpawn
            };

        private static CombatHitPayload HitPayloadFor(CombatHitPayload hitPayload, CombatFaction faction)
        {
            StackEffectSnapshot stack = hitPayload.StackEffect;
            stack.Faction = faction;
            hitPayload.StackEffect = stack;
            return hitPayload;
        }

        private static AoeAreaComponent AreaFor(in AoeSpawnCommand cmd) =>
            new() { Size = cmd.AreaSize > 0f ? cmd.AreaSize : 1f };

        private static float DeterministicJitter(int aoeId, int jitterSeed, float maxOffsetSeconds)
        {
            if (maxOffsetSeconds <= 0f)
            {
                return 0f;
            }

            unchecked
            {
                uint hash = (uint)aoeId;
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
    }
}
