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
                .Build(this);
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();
            SpawnTemplateRegistryState registryState = SystemAPI.GetSingleton<SpawnTemplateRegistryState>();
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
                ArmingHandle = GetComponentTypeHandle<CombatArmingComponent>(false),
                Deltas = registryState.Deltas.AsParallelWriter()
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
            public NativeQueue<SpawnTemplateRefDelta>.ParallelWriter Deltas;

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
                        identities[i] = new TargetedIdentityComponent
                        {
                            Faction = cfg.Faction,
                            TargetedId = cfg.TargetedId,
                            TypeId = cfg.TypeId,
                            InstanceIndex = cfg.InstanceIndex
                        };
                        chains[i] = new TargetedChainComponent
                        {
                            Origin = cfg.Origin,
                            AcquireAnchor = cfg.AcquireAnchor,
                            LinkSource = cfg.Origin,
                            LinkTarget = cfg.AcquireAnchor,
                            LastTargetKey = 0,
                            LinkIndex = 0,
                            LinkGateRemaining = 0f
                        };
                        resolveConfigs[i] = cfg.Resolve;
                        hitPayloads[i] = HitPayloadFor(in cfg.HitPayload, cfg.Faction);
                        vfxIds[i] = cfg.VfxIds;
                        vfxSizes[i] = cfg.VfxSize;
                        vfxTimings[i] = TargetedVfxUtility.TimingFor(cfg);
                        kinematics[i] = new CombatKinematicsComponent { Position = cfg.AcquireAnchor };
                        renders[i] = cfg.Render;
                        authorings[i] = cfg.Authoring;
                        renderKindIds[i] = new CombatRenderKindId { Value = cfg.RenderTypeId };
                        lifetimes[i] = new CombatLifetimeComponent { Remaining = cfg.LifetimeSeconds };
                        armings[i] = new CombatArmingComponent { Remaining = cfg.ArmSeconds };
                        activeMask[i] = true;
                        // An acquired chain starts on its target and can render immediately. An
                        // unacquired chain keeps the caster-facing placeholder hidden until its
                        // first link lands. Authored arming remains an independent pause overlay.
                        armingMask[i] = cfg.ArmSeconds > 0f || cfg.HasAcquiredTarget == 0;

                        // Spawn event, emitted after every component is written so it reads the
                        // entity's own state and stays the mirror image of the release the death
                        // path emits.
                        SpawnTemplateRefEmit.AcquireTargeted(hitPayloads[i], Deltas);
                    }
                }

                ReuseCount.Value = commandIndex;
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
    }
}
