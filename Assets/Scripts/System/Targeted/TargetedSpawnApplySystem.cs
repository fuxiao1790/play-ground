using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Profiling;
using PlayGround.System.Combat.Application;

namespace PlayGround.System.Combat.Targeted
{
    internal static class TargetedSpawnApplyUtility
    {
        public static void WriteCommon(
            in TargetedSpawnCommand cfg,
            NativeArray<TargetedIdentityComponent> identities,
            NativeArray<TargetedChainComponent> chains,
            NativeArray<TargetedResolveConfig> resolveConfigs,
            NativeArray<CombatHitPayload> hitPayloads,
            NativeArray<TargetedVfxIds> vfxIds,
            NativeArray<TargetedVfxSizeComponent> vfxSizes,
            NativeArray<VfxTimingData> vfxTimings,
            NativeArray<CombatKinematicsComponent> kinematics,
            NativeArray<CombatRenderComponent> renders,
            NativeArray<CombatRenderAuthoring> authorings,
            NativeArray<CombatRenderKindId> renderKindIds,
            int index)
        {
            identities[index] = new TargetedIdentityComponent
            {
                Faction = cfg.Faction,
                TargetedId = cfg.TargetedId,
                TypeId = cfg.TypeId,
                InstanceIndex = cfg.InstanceIndex
            };
            chains[index] = new TargetedChainComponent
            {
                Origin = cfg.Origin,
                AcquireAnchor = cfg.AcquireAnchor,
                LinkSource = cfg.Origin,
                LinkTarget = cfg.Origin,
                LastTargetKey = 0,
                LinkIndex = 0,
                LinkGateRemaining = 0f
            };
            resolveConfigs[index] = cfg.Resolve;
            hitPayloads[index] = HitPayloadFor(in cfg.HitPayload, cfg.Faction);
            vfxIds[index] = cfg.VfxIds;
            vfxSizes[index] = cfg.VfxSize;
            vfxTimings[index] = TargetedVfxUtility.TimingFor(cfg);
            kinematics[index] = new CombatKinematicsComponent { Position = cfg.Origin };
            renders[index] = cfg.Render;
            authorings[index] = cfg.Authoring;
            renderKindIds[index] = new CombatRenderKindId { Value = cfg.RenderTypeId };
        }

        private static CombatHitPayload HitPayloadFor(
            in CombatHitPayload hitPayload,
            CombatFaction faction)
        {
            CombatHitPayload payload = hitPayload;
            StackEffectSnapshot stack = payload.StackEffect;
            stack.Faction = faction;
            payload.StackEffect = stack;
            return payload;
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetedSpawnExpansionSystem))]
    public partial class TargetedSpawnApplySystem : SystemBase
    {
        private static readonly ProfilerMarker CreateSlotsMarker =
            new("TargetedSpawnApplySystem.CreateSlots");

        private EntityArchetype _archetype;
        private EntityQuery _deadSlots;

        protected override void OnCreate()
        {
            _archetype = EntityManager.CreateArchetype(
                typeof(TargetedTag),
                typeof(TargetedIdentityComponent),
                typeof(TargetedChainComponent),
                typeof(TargetedResolveConfig),
                typeof(CombatHitPayload),
                typeof(TargetedVfxIds),
                typeof(TargetedVfxSizeComponent),
                typeof(VfxTimingData),
                typeof(CombatLifetimeComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderAuthoring),
                typeof(CombatRenderKindId),
                typeof(CombatKinematicsComponent),
                typeof(Active),
                typeof(ArmingTag),
                typeof(CombatArmingComponent));
            _deadSlots = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<TargetedTag>()
                .WithDisabled<Active>()
                .WithNone<LingeringTargetedTag>()
                .Build(this);
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();
            TargetedSpawnEventSingleton lane = SystemAPI.GetSingleton<TargetedSpawnEventSingleton>();
            lane.PendingHandle.Complete();
            if (!lane.Commands.IsCreated || lane.Commands.Length == 0)
            {
                return;
            }

            NativeArray<TargetedSpawnCommand> commands = lane.Commands.AsArray();
            int created = SpawnPoolTopUp.EnsureDisabledSlots(
                EntityManager, _archetype, _deadSlots, commands.Length, CreateSlotsMarker);
            using NativeArray<ArchetypeChunk> chunks = _deadSlots.ToArchetypeChunkArray(Allocator.TempJob);
            using var reused = new NativeReference<int>(Allocator.TempJob);
            new TargetedSpawnJob
            {
                Configs = commands,
                Chunks = chunks,
                ReuseCount = reused,
                ActiveHandle = GetComponentTypeHandle<Active>(false),
                ArmingTagHandle = GetComponentTypeHandle<ArmingTag>(false),
                IdentityHandle = GetComponentTypeHandle<TargetedIdentityComponent>(false),
                ChainHandle = GetComponentTypeHandle<TargetedChainComponent>(false),
                ResolveHandle = GetComponentTypeHandle<TargetedResolveConfig>(false),
                HitPayloadHandle = GetComponentTypeHandle<CombatHitPayload>(false),
                VfxIdsHandle = GetComponentTypeHandle<TargetedVfxIds>(false),
                VfxSizeHandle = GetComponentTypeHandle<TargetedVfxSizeComponent>(false),
                VfxTimingHandle = GetComponentTypeHandle<VfxTimingData>(false),
                KinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                RenderHandle = GetComponentTypeHandle<CombatRenderComponent>(false),
                AuthoringHandle = GetComponentTypeHandle<CombatRenderAuthoring>(false),
                RenderKindHandle = GetComponentTypeHandle<CombatRenderKindId>(false),
                LifetimeHandle = GetComponentTypeHandle<CombatLifetimeComponent>(false),
                ArmingHandle = GetComponentTypeHandle<CombatArmingComponent>(false)
            }.Schedule(default).Complete();

            // EnsureDisabledSlots creates the deficit before the job sees the disabled-slot
            // query. The job assigns every command, so its command count includes both reused
            // slots and the newly created cold slots. Only the pre-existing disabled slots are
            // reuse; cold creates are already counted separately.
            AddSpawnStats(commands.Length - created, created);
        }

        private void AddSpawnStats(int reuse, int created)
        {
            if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(out RefRW<CombatStatsSingleton> stats))
            {
                stats.ValueRW.EntitiesSpawnedViaReuse += reuse;
                stats.ValueRW.EntitiesSpawnedViaEcb += created;
                stats.ValueRW.TargetedEntitiesSpawned += reuse + created;
            }
        }

        [BurstCompile]
        private struct TargetedSpawnJob : IJob
        {
            [ReadOnly] public NativeArray<TargetedSpawnCommand> Configs;
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            public NativeReference<int> ReuseCount;
            public ComponentTypeHandle<Active> ActiveHandle;
            public ComponentTypeHandle<ArmingTag> ArmingTagHandle;
            public ComponentTypeHandle<TargetedIdentityComponent> IdentityHandle;
            public ComponentTypeHandle<TargetedChainComponent> ChainHandle;
            public ComponentTypeHandle<TargetedResolveConfig> ResolveHandle;
            public ComponentTypeHandle<CombatHitPayload> HitPayloadHandle;
            public ComponentTypeHandle<TargetedVfxIds> VfxIdsHandle;
            public ComponentTypeHandle<TargetedVfxSizeComponent> VfxSizeHandle;
            public ComponentTypeHandle<VfxTimingData> VfxTimingHandle;
            public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            public ComponentTypeHandle<CombatRenderComponent> RenderHandle;
            public ComponentTypeHandle<CombatRenderAuthoring> AuthoringHandle;
            public ComponentTypeHandle<CombatRenderKindId> RenderKindHandle;
            public ComponentTypeHandle<CombatLifetimeComponent> LifetimeHandle;
            public ComponentTypeHandle<CombatArmingComponent> ArmingHandle;

            public void Execute()
            {
                int commandIndex = 0;
                for (int chunkIndex = 0;
                     chunkIndex < Chunks.Length && commandIndex < Configs.Length;
                     chunkIndex++)
                {
                    ArchetypeChunk chunk = Chunks[chunkIndex];
                    EnabledMask activeMask = chunk.GetEnabledMask(ref ActiveHandle);
                    EnabledMask armingMask = chunk.GetEnabledMask(ref ArmingTagHandle);
                    NativeArray<TargetedIdentityComponent> identities = chunk.GetNativeArray(ref IdentityHandle);
                    NativeArray<TargetedChainComponent> chains = chunk.GetNativeArray(ref ChainHandle);
                    NativeArray<TargetedResolveConfig> resolveConfigs = chunk.GetNativeArray(ref ResolveHandle);
                    NativeArray<CombatHitPayload> hitPayloads = chunk.GetNativeArray(ref HitPayloadHandle);
                    NativeArray<TargetedVfxIds> vfxIds = chunk.GetNativeArray(ref VfxIdsHandle);
                    NativeArray<TargetedVfxSizeComponent> vfxSizes = chunk.GetNativeArray(ref VfxSizeHandle);
                    NativeArray<VfxTimingData> vfxTimings = chunk.GetNativeArray(ref VfxTimingHandle);
                    NativeArray<CombatKinematicsComponent> kinematics = chunk.GetNativeArray(ref KinematicsHandle);
                    NativeArray<CombatRenderComponent> renders = chunk.GetNativeArray(ref RenderHandle);
                    NativeArray<CombatRenderAuthoring> authorings = chunk.GetNativeArray(ref AuthoringHandle);
                    NativeArray<CombatRenderKindId> renderKindIds = chunk.GetNativeArray(ref RenderKindHandle);
                    NativeArray<CombatLifetimeComponent> lifetimes = chunk.GetNativeArray(ref LifetimeHandle);
                    NativeArray<CombatArmingComponent> armings = chunk.GetNativeArray(ref ArmingHandle);

                    for (int i = 0; i < chunk.Count && commandIndex < Configs.Length; i++)
                    {
                        if (activeMask[i])
                        {
                            continue;
                        }

                        TargetedSpawnCommand cfg = Configs[commandIndex++];
                        TargetedSpawnApplyUtility.WriteCommon(
                            cfg, identities, chains, resolveConfigs, hitPayloads, vfxIds, vfxSizes,
                            vfxTimings, kinematics, renders, authorings, renderKindIds, i);
                        lifetimes[i] = new CombatLifetimeComponent { Remaining = cfg.LifetimeSeconds };
                        armings[i] = new CombatArmingComponent { Remaining = cfg.ArmSeconds };
                        activeMask[i] = true;
                        armingMask[i] = cfg.ArmSeconds > 0f;
                    }
                }

                ReuseCount.Value = commandIndex;
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(LingeringTargetedSpawnExpansionSystem))]
    public partial class LingeringTargetedSpawnApplySystem : SystemBase
    {
        private static readonly ProfilerMarker CreateSlotsMarker =
            new("LingeringTargetedSpawnApplySystem.CreateSlots");

        private EntityArchetype _archetype;
        private EntityQuery _deadSlots;

        protected override void OnCreate()
        {
            _archetype = EntityManager.CreateArchetype(
                typeof(TargetedTag),
                typeof(LingeringTargetedTag),
                typeof(TargetedIdentityComponent),
                typeof(TargetedChainComponent),
                typeof(TargetedResolveConfig),
                typeof(CombatHitPayload),
                typeof(TargetedVfxIds),
                typeof(TargetedVfxSizeComponent),
                typeof(VfxTimingData),
                typeof(CombatLifetimeComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderAuthoring),
                typeof(CombatRenderKindId),
                typeof(CombatKinematicsComponent),
                typeof(Active),
                typeof(ArmingTag),
                typeof(CombatArmingComponent),
                typeof(TargetedTickGateComponent),
                typeof(TimedSpawnComponent),
                typeof(TimedSpawnStateComponent));
            _deadSlots = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<TargetedTag>()
                .WithAll<LingeringTargetedTag>()
                .WithDisabled<Active>()
                .Build(this);
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();
            LingeringTargetedSpawnEventSingleton lane =
                SystemAPI.GetSingleton<LingeringTargetedSpawnEventSingleton>();
            lane.PendingHandle.Complete();
            if (!lane.Commands.IsCreated || lane.Commands.Length == 0)
            {
                return;
            }

            NativeArray<TargetedSpawnCommand> commands = lane.Commands.AsArray();
            int created = SpawnPoolTopUp.EnsureDisabledSlots(
                EntityManager, _archetype, _deadSlots, commands.Length, CreateSlotsMarker);
            using NativeArray<ArchetypeChunk> chunks = _deadSlots.ToArchetypeChunkArray(Allocator.TempJob);
            using var reused = new NativeReference<int>(Allocator.TempJob);
            new LingeringTargetedSpawnJob
            {
                Configs = commands,
                Chunks = chunks,
                ReuseCount = reused,
                ActiveHandle = GetComponentTypeHandle<Active>(false),
                ArmingTagHandle = GetComponentTypeHandle<ArmingTag>(false),
                TimedSpawnHandle = GetComponentTypeHandle<TimedSpawnComponent>(false),
                IdentityHandle = GetComponentTypeHandle<TargetedIdentityComponent>(false),
                ChainHandle = GetComponentTypeHandle<TargetedChainComponent>(false),
                ResolveHandle = GetComponentTypeHandle<TargetedResolveConfig>(false),
                HitPayloadHandle = GetComponentTypeHandle<CombatHitPayload>(false),
                VfxIdsHandle = GetComponentTypeHandle<TargetedVfxIds>(false),
                VfxSizeHandle = GetComponentTypeHandle<TargetedVfxSizeComponent>(false),
                VfxTimingHandle = GetComponentTypeHandle<VfxTimingData>(false),
                KinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                RenderHandle = GetComponentTypeHandle<CombatRenderComponent>(false),
                AuthoringHandle = GetComponentTypeHandle<CombatRenderAuthoring>(false),
                RenderKindHandle = GetComponentTypeHandle<CombatRenderKindId>(false),
                LifetimeHandle = GetComponentTypeHandle<CombatLifetimeComponent>(false),
                ArmingHandle = GetComponentTypeHandle<CombatArmingComponent>(false),
                TickGateHandle = GetComponentTypeHandle<TargetedTickGateComponent>(false),
                TimedSpawnStateHandle = GetComponentTypeHandle<TimedSpawnStateComponent>(false)
            }.Schedule(default).Complete();

            if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(out RefRW<CombatStatsSingleton> stats))
            {
                stats.ValueRW.EntitiesSpawnedViaReuse += commands.Length - created;
                stats.ValueRW.EntitiesSpawnedViaEcb += created;
                stats.ValueRW.TargetedEntitiesSpawned += commands.Length;
            }
        }

        [BurstCompile]
        private struct LingeringTargetedSpawnJob : IJob
        {
            [ReadOnly] public NativeArray<TargetedSpawnCommand> Configs;
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            public NativeReference<int> ReuseCount;
            public ComponentTypeHandle<Active> ActiveHandle;
            public ComponentTypeHandle<ArmingTag> ArmingTagHandle;
            public ComponentTypeHandle<TimedSpawnComponent> TimedSpawnHandle;
            public ComponentTypeHandle<TargetedIdentityComponent> IdentityHandle;
            public ComponentTypeHandle<TargetedChainComponent> ChainHandle;
            public ComponentTypeHandle<TargetedResolveConfig> ResolveHandle;
            public ComponentTypeHandle<CombatHitPayload> HitPayloadHandle;
            public ComponentTypeHandle<TargetedVfxIds> VfxIdsHandle;
            public ComponentTypeHandle<TargetedVfxSizeComponent> VfxSizeHandle;
            public ComponentTypeHandle<VfxTimingData> VfxTimingHandle;
            public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            public ComponentTypeHandle<CombatRenderComponent> RenderHandle;
            public ComponentTypeHandle<CombatRenderAuthoring> AuthoringHandle;
            public ComponentTypeHandle<CombatRenderKindId> RenderKindHandle;
            public ComponentTypeHandle<CombatLifetimeComponent> LifetimeHandle;
            public ComponentTypeHandle<CombatArmingComponent> ArmingHandle;
            public ComponentTypeHandle<TargetedTickGateComponent> TickGateHandle;
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
                    EnabledMask armingMask = chunk.GetEnabledMask(ref ArmingTagHandle);
                    EnabledMask timedSpawnMask = chunk.GetEnabledMask(ref TimedSpawnHandle);
                    NativeArray<TargetedIdentityComponent> identities = chunk.GetNativeArray(ref IdentityHandle);
                    NativeArray<TargetedChainComponent> chains = chunk.GetNativeArray(ref ChainHandle);
                    NativeArray<TargetedResolveConfig> resolveConfigs = chunk.GetNativeArray(ref ResolveHandle);
                    NativeArray<CombatHitPayload> hitPayloads = chunk.GetNativeArray(ref HitPayloadHandle);
                    NativeArray<TargetedVfxIds> vfxIds = chunk.GetNativeArray(ref VfxIdsHandle);
                    NativeArray<TargetedVfxSizeComponent> vfxSizes = chunk.GetNativeArray(ref VfxSizeHandle);
                    NativeArray<VfxTimingData> vfxTimings = chunk.GetNativeArray(ref VfxTimingHandle);
                    NativeArray<CombatKinematicsComponent> kinematics = chunk.GetNativeArray(ref KinematicsHandle);
                    NativeArray<CombatRenderComponent> renders = chunk.GetNativeArray(ref RenderHandle);
                    NativeArray<CombatRenderAuthoring> authorings = chunk.GetNativeArray(ref AuthoringHandle);
                    NativeArray<CombatRenderKindId> renderKindIds = chunk.GetNativeArray(ref RenderKindHandle);
                    NativeArray<CombatLifetimeComponent> lifetimes = chunk.GetNativeArray(ref LifetimeHandle);
                    NativeArray<CombatArmingComponent> armings = chunk.GetNativeArray(ref ArmingHandle);
                    NativeArray<TargetedTickGateComponent> tickGates = chunk.GetNativeArray(ref TickGateHandle);
                    NativeArray<TimedSpawnComponent> timedSpawns = chunk.GetNativeArray(ref TimedSpawnHandle);
                    NativeArray<TimedSpawnStateComponent> timedSpawnStates =
                        chunk.GetNativeArray(ref TimedSpawnStateHandle);

                    for (int i = 0; i < chunk.Count && commandIndex < Configs.Length; i++)
                    {
                        if (activeMask[i])
                        {
                            continue;
                        }

                        TargetedSpawnCommand cfg = Configs[commandIndex++];
                        TargetedSpawnApplyUtility.WriteCommon(
                            cfg, identities, chains, resolveConfigs, hitPayloads, vfxIds, vfxSizes,
                            vfxTimings, kinematics, renders, authorings, renderKindIds, i);
                        lifetimes[i] = new CombatLifetimeComponent { Remaining = cfg.LifetimeSeconds };
                        armings[i] = new CombatArmingComponent { Remaining = cfg.ArmSeconds };
                        tickGates[i] = new TargetedTickGateComponent
                        {
                            TickIntervalSeconds = cfg.TickIntervalSeconds,
                            Remaining = 0f
                        };
                        bool hasTimedSpawner = cfg.LifetimeSeconds > 0f && cfg.HasTimedSpawner != 0;
                        timedSpawns[i] = hasTimedSpawner ? cfg.TimedSpawn : default;
                        timedSpawnStates[i] = default;
                        activeMask[i] = true;
                        armingMask[i] = cfg.ArmSeconds > 0f;
                        timedSpawnMask[i] = hasTimedSpawner;
                    }
                }

                ReuseCount.Value = commandIndex;
            }
        }
    }
}
