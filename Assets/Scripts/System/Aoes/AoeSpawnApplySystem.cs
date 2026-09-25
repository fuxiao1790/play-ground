using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Audio;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace PlayGround.System.Combat.Aoes
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ImpactAoeSpawnExpansionSystem))]
    [UpdateAfter(typeof(ProjectileSpawnExpansionSystem))]
    public partial class ImpactAoeSpawnApplySystem : SystemBase
    {
        private static readonly ProfilerMarker SpawnMarker = new("ImpactAoeSpawnApplySystem");
        private static readonly ProfilerMarker ReuseJobMarker = new("ImpactAoeSpawnApplySystem.ReuseJob");
        private static readonly ProfilerMarker CreateSlotsMarker = new("ImpactAoeSpawnApplySystem.CreateSlots");
        private static readonly ProfilerCounterValue<int> SpawnTopUpCounter =
            new(ProfilerCategory.Scripts, "ImpactAoeSpawnApplySystem.TopUp", ProfilerMarkerDataUnit.Count);
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
                typeof(AoeVfxIds),
                typeof(VfxTimingData),
                typeof(CombatHitPayload),
                typeof(AoeAreaComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderAuthoring),
                typeof(CombatRenderKindId),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(Active),
                typeof(CombatCollisionActiveTag),
                typeof(ArmingTag),
                typeof(CombatArmingComponent));

            _deadSlotQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithDisabled<Active>()
                .WithNone<LingeringAoeTag>()
                .Build(this);
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();
            SpawnTemplateRegistryState registryState = SystemAPI.GetSingleton<SpawnTemplateRegistryState>();

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

            RefRW<SoundEventSingleton> sounds =
                SystemAPI.GetSingletonRW<SoundEventSingleton>();
            SoundEventLane.Reserve(ref sounds.ValueRW, totalRequests);

            using (SpawnMarker.Auto())
            {
                int created = SpawnPoolTopUp.EnsureDisabledSlots(
                    EntityManager,
                    _impactArchetype,
                    _deadSlotQuery,
                    totalRequests,
                    CreateSlotsMarker);
                int reuseCount = 0;

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
                        CollisionActiveHandle = GetComponentTypeHandle<CombatCollisionActiveTag>(false),
                        IdentityHandle = GetComponentTypeHandle<AoeIdentityComponent>(false),
                        KinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                        CollisionHandle = GetComponentTypeHandle<CombatCollisionComponent>(false),
                        HitGateHandle = GetComponentTypeHandle<AoeHitGateComponent>(false),
                        VfxIdsHandle = GetComponentTypeHandle<AoeVfxIds>(false),
                        VfxTimingHandle = GetComponentTypeHandle<VfxTimingData>(false),
                        HitPayloadHandle = GetComponentTypeHandle<CombatHitPayload>(false),
                        AreaHandle = GetComponentTypeHandle<AoeAreaComponent>(false),
                        RenderHandle = GetComponentTypeHandle<CombatRenderComponent>(false),
                        AuthoringHandle = GetComponentTypeHandle<CombatRenderAuthoring>(false),
                        RenderBatchIdHandle = GetComponentTypeHandle<CombatRenderKindId>(false),
                        ArmingHandle = GetComponentTypeHandle<CombatArmingComponent>(false),
                        ArmingTagHandle = GetComponentTypeHandle<ArmingTag>(false),
                        Deltas = registryState.Deltas.AsParallelWriter(),
                        SoundEventsByClip = sounds.ValueRO.EventsByClip,
                        SoundClipIds = sounds.ValueRO.ClipIds,
                    }.Schedule(default).Complete();

                    reuseCount = reused.Value;
                }

                SpawnReuseCounter.Value = reuseCount;
                SpawnTopUpCounter.Value = created;
                LastReuseCount = reuseCount;
                LastColdCreateCount = created;

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
            public ComponentTypeHandle<CombatCollisionActiveTag> CollisionActiveHandle;
            public ComponentTypeHandle<AoeIdentityComponent> IdentityHandle;
            public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            public ComponentTypeHandle<CombatCollisionComponent> CollisionHandle;
            public ComponentTypeHandle<AoeHitGateComponent> HitGateHandle;
            public ComponentTypeHandle<AoeVfxIds> VfxIdsHandle;
            public ComponentTypeHandle<VfxTimingData> VfxTimingHandle;
            public ComponentTypeHandle<CombatHitPayload> HitPayloadHandle;
            public ComponentTypeHandle<AoeAreaComponent> AreaHandle;
            public ComponentTypeHandle<CombatRenderComponent> RenderHandle;
            public ComponentTypeHandle<CombatRenderAuthoring> AuthoringHandle;
            public ComponentTypeHandle<CombatRenderKindId> RenderBatchIdHandle;
            public ComponentTypeHandle<CombatArmingComponent> ArmingHandle;
            public ComponentTypeHandle<ArmingTag> ArmingTagHandle;
            public NativeQueue<SpawnTemplateRefDelta>.ParallelWriter Deltas;
            public NativeParallelMultiHashMap<int, SoundEvent> SoundEventsByClip;
            public NativeParallelHashSet<int> SoundClipIds;

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
                    EnabledMask armingMask = chunk.GetEnabledMask(ref ArmingTagHandle);

                    NativeArray<AoeIdentityComponent> identities =
                        chunk.GetNativeArray(ref IdentityHandle);
                    NativeArray<CombatKinematicsComponent> kinematics =
                        chunk.GetNativeArray(ref KinematicsHandle);
                    NativeArray<CombatCollisionComponent> collisions =
                        chunk.GetNativeArray(ref CollisionHandle);
                    NativeArray<AoeHitGateComponent> hitGates =
                        chunk.GetNativeArray(ref HitGateHandle);
                    NativeArray<AoeVfxIds> vfxIds = chunk.GetNativeArray(ref VfxIdsHandle);
                    NativeArray<VfxTimingData> vfxTimings =
                        chunk.GetNativeArray(ref VfxTimingHandle);
                    NativeArray<CombatHitPayload> hitPayloads =
                        chunk.GetNativeArray(ref HitPayloadHandle);
                    NativeArray<AoeAreaComponent> areas = chunk.GetNativeArray(ref AreaHandle);
                    NativeArray<CombatRenderComponent> renders =
                        chunk.GetNativeArray(ref RenderHandle);
                    NativeArray<CombatRenderAuthoring> authorings =
                        chunk.GetNativeArray(ref AuthoringHandle);
                    NativeArray<CombatRenderKindId> batchIds =
                        chunk.GetNativeArray(ref RenderBatchIdHandle);
                    NativeArray<CombatArmingComponent> armings =
                        chunk.GetNativeArray(ref ArmingHandle);

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
                            vfxIds,
                            vfxTimings,
                            hitPayloads,
                            areas,
                            renders,
                            authorings,
                            batchIds,
                            i);

                        AoeSpawnApplyUtility.SpawnState spawnState =
                            AoeSpawnApplyUtility.SpawnStateFor(cfg, isLingering: false);
                        activeMask[i] = spawnState.Active;
                        collisionActiveMask[i] = spawnState.Collision;
                        armings[i] = AoeSpawnApplyUtility.ArmingFor(cfg);
                        armingMask[i] = AoeSpawnApplyUtility.IsArming(cfg);
                        if (cfg.ArmSeconds <= 0f)
                        {
                            SoundEmit.Enqueue(
                                cfg.SoundIds.SpawnId,
                                cfg.Position,
                                cfg.SpawnSoundRadius,
                                SoundCategory.Spawn,
                                SoundEventsByClip,
                                SoundClipIds);
                        }

                        // An impact AOE with nothing to collide against materializes already
                        // dead, so it never reaches a death site and must not claim a
                        // reference. Impact archetypes carry no TimedSpawnComponent.
                        if (spawnState.Active)
                        {
                            SpawnTemplateRefEmit.AcquireAoe(
                                default,
                                hitPayloads[i],
                                Deltas);
                        }
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
        private static readonly ProfilerMarker CreateSlotsMarker = new("LingeringAoeSpawnApplySystem.CreateSlots");
        private static readonly ProfilerCounterValue<int> SpawnTopUpCounter =
            new(ProfilerCategory.Scripts, "LingeringAoeSpawnApplySystem.TopUp", ProfilerMarkerDataUnit.Count);
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
                typeof(LingeringAoeTag),
                typeof(AoeIdentityComponent),
                typeof(CombatLifetimeComponent),
                typeof(AoeHitGateComponent),
                typeof(AoeVfxIds),
                typeof(VfxTimingData),
                typeof(CombatHitPayload),
                typeof(AoeAreaComponent),
                typeof(AoePulseVfxComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderAuthoring),
                typeof(CombatRenderKindId),
                typeof(CombatKinematicsComponent),
                typeof(CombatCollisionComponent),
                typeof(Active),
                typeof(CombatCollisionActiveTag),
                typeof(ArmingTag),
                typeof(CombatArmingComponent),
                typeof(TimedSpawnComponent),
                typeof(TimedSpawnStateComponent));

            _deadSlotQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AoeTag>()
                .WithAll<LingeringAoeTag>()
                .WithDisabled<Active>()
                .Build(this);
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();
            SpawnTemplateRegistryState registryState = SystemAPI.GetSingleton<SpawnTemplateRegistryState>();

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

            RefRW<SoundEventSingleton> sounds =
                SystemAPI.GetSingletonRW<SoundEventSingleton>();
            SoundEventLane.Reserve(ref sounds.ValueRW, totalRequests);

            using (SpawnMarker.Auto())
            {
                int created = SpawnPoolTopUp.EnsureDisabledSlots(
                    EntityManager,
                    _lingeringArchetype,
                    _deadSlotQuery,
                    totalRequests,
                    CreateSlotsMarker);
                int reuseCount = 0;

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
                        CollisionActiveHandle = GetComponentTypeHandle<CombatCollisionActiveTag>(false),
                        IdentityHandle = GetComponentTypeHandle<AoeIdentityComponent>(false),
                        KinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                        CollisionHandle = GetComponentTypeHandle<CombatCollisionComponent>(false),
                        LifetimeHandle = GetComponentTypeHandle<CombatLifetimeComponent>(false),
                        HitGateHandle = GetComponentTypeHandle<AoeHitGateComponent>(false),
                        VfxIdsHandle = GetComponentTypeHandle<AoeVfxIds>(false),
                        VfxTimingHandle = GetComponentTypeHandle<VfxTimingData>(false),
                        HitPayloadHandle = GetComponentTypeHandle<CombatHitPayload>(false),
                        AreaHandle = GetComponentTypeHandle<AoeAreaComponent>(false),
                        PulseVfxHandle = GetComponentTypeHandle<AoePulseVfxComponent>(false),
                        RenderHandle = GetComponentTypeHandle<CombatRenderComponent>(false),
                        AuthoringHandle = GetComponentTypeHandle<CombatRenderAuthoring>(false),
                        RenderBatchIdHandle = GetComponentTypeHandle<CombatRenderKindId>(false),
                        ArmingHandle = GetComponentTypeHandle<CombatArmingComponent>(false),
                        ArmingTagHandle = GetComponentTypeHandle<ArmingTag>(false),
                        TimedSpawnHandle = GetComponentTypeHandle<TimedSpawnComponent>(false),
                        TimedSpawnStateHandle = GetComponentTypeHandle<TimedSpawnStateComponent>(false),
                        Deltas = registryState.Deltas.AsParallelWriter(),
                        SoundEventsByClip = sounds.ValueRO.EventsByClip,
                        SoundClipIds = sounds.ValueRO.ClipIds,
                    }.Schedule(default).Complete();

                    reuseCount = reused.Value;
                }

                SpawnReuseCounter.Value = reuseCount;
                SpawnTopUpCounter.Value = created;
                LastReuseCount = reuseCount;
                LastColdCreateCount = created;

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
            public ComponentTypeHandle<CombatCollisionActiveTag> CollisionActiveHandle;
            public ComponentTypeHandle<AoeIdentityComponent> IdentityHandle;
            public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            public ComponentTypeHandle<CombatCollisionComponent> CollisionHandle;
            public ComponentTypeHandle<CombatLifetimeComponent> LifetimeHandle;
            public ComponentTypeHandle<AoeHitGateComponent> HitGateHandle;
            public ComponentTypeHandle<AoeVfxIds> VfxIdsHandle;
            public ComponentTypeHandle<VfxTimingData> VfxTimingHandle;
            public ComponentTypeHandle<CombatHitPayload> HitPayloadHandle;
            public ComponentTypeHandle<AoeAreaComponent> AreaHandle;
            public ComponentTypeHandle<AoePulseVfxComponent> PulseVfxHandle;
            public ComponentTypeHandle<CombatRenderComponent> RenderHandle;
            public ComponentTypeHandle<CombatRenderAuthoring> AuthoringHandle;
            public ComponentTypeHandle<CombatRenderKindId> RenderBatchIdHandle;
            public ComponentTypeHandle<CombatArmingComponent> ArmingHandle;
            public ComponentTypeHandle<ArmingTag> ArmingTagHandle;
            public ComponentTypeHandle<TimedSpawnComponent> TimedSpawnHandle;
            public ComponentTypeHandle<TimedSpawnStateComponent> TimedSpawnStateHandle;
            public NativeQueue<SpawnTemplateRefDelta>.ParallelWriter Deltas;
            public NativeParallelMultiHashMap<int, SoundEvent> SoundEventsByClip;
            public NativeParallelHashSet<int> SoundClipIds;

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
                    EnabledMask armingMask = chunk.GetEnabledMask(ref ArmingTagHandle);
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
                    NativeArray<AoeVfxIds> vfxIds = chunk.GetNativeArray(ref VfxIdsHandle);
                    NativeArray<VfxTimingData> vfxTimings =
                        chunk.GetNativeArray(ref VfxTimingHandle);
                    NativeArray<CombatHitPayload> hitPayloads =
                        chunk.GetNativeArray(ref HitPayloadHandle);
                    NativeArray<AoeAreaComponent> areas = chunk.GetNativeArray(ref AreaHandle);
                    NativeArray<AoePulseVfxComponent> pulseVfxs =
                        chunk.GetNativeArray(ref PulseVfxHandle);
                    NativeArray<CombatRenderComponent> renders =
                        chunk.GetNativeArray(ref RenderHandle);
                    NativeArray<CombatRenderAuthoring> authorings =
                        chunk.GetNativeArray(ref AuthoringHandle);
                    NativeArray<CombatRenderKindId> batchIds =
                        chunk.GetNativeArray(ref RenderBatchIdHandle);
                    NativeArray<CombatArmingComponent> armings =
                        chunk.GetNativeArray(ref ArmingHandle);
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
                            vfxIds,
                            vfxTimings,
                            hitPayloads,
                            areas,
                            renders,
                            authorings,
                            batchIds,
                            i);

                        lifetimes[i] = new CombatLifetimeComponent { Remaining = cfg.Lifetime };
                        pulseVfxs[i] = AoeSpawnApplyUtility.PulseVfxFor(cfg);

                        AoeSpawnApplyUtility.SpawnState spawnState =
                            AoeSpawnApplyUtility.SpawnStateFor(cfg, isLingering: true);
                        bool hasTimedSpawner = spawnState.Timed;
                        timedSpawns[i] = hasTimedSpawner ? cfg.TimedSpawn : default;
                        timedSpawnStates[i] = hasTimedSpawner
                            ? AoeSpawnApplyUtility.InitialTimedSpawnStateFor(cfg)
                            : default;
                        timedSpawnMask[i] = spawnState.Timed;

                        activeMask[i] = spawnState.Active;
                        collisionActiveMask[i] = spawnState.Collision;
                        armings[i] = AoeSpawnApplyUtility.ArmingFor(cfg);
                        armingMask[i] = AoeSpawnApplyUtility.IsArming(cfg);
                        if (cfg.ArmSeconds <= 0f)
                        {
                            SoundEmit.Enqueue(
                                cfg.SoundIds.SpawnId,
                                cfg.Position,
                                cfg.SpawnSoundRadius,
                                SoundCategory.Spawn,
                                SoundEventsByClip,
                                SoundClipIds);
                        }

                        // Spawn event, emitted after every component is written so it reads the
                        // entity's own state and stays the mirror image of the release the death
                        // path emits. Lingering AOEs are always materialized active.
                        if (spawnState.Active)
                        {
                            SpawnTemplateRefEmit.AcquireAoe(
                                timedSpawns[i],
                                hitPayloads[i],
                                Deltas);
                        }
                    }
                }

                ReuseCount.Value = commandIndex;
            }
        }
    }

    internal static class AoeSpawnApplyUtility
    {
        public static void WriteCommon(
            in AoeSpawnCommand cfg,
            NativeArray<AoeIdentityComponent> identities,
            NativeArray<CombatKinematicsComponent> kinematics,
            NativeArray<CombatCollisionComponent> collisions,
            NativeArray<AoeHitGateComponent> hitGates,
            NativeArray<AoeVfxIds> vfxIds,
            NativeArray<VfxTimingData> vfxTimings,
            NativeArray<CombatHitPayload> hitPayloads,
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
            vfxIds[index] = cfg.VfxIds;
            vfxTimings[index] = VfxTimingFor(cfg);
            hitPayloads[index] = HitPayloadFor(cfg.HitPayload, cfg.Faction);
            areas[index] = AreaFor(cfg);
            renders[index] = render;
            authorings[index] = cfg.Authoring;
            batchIds[index] = new CombatRenderKindId { Value = cfg.RenderTypeId };
        }

        public static bool NeedsCollision(in AoeSpawnCommand cmd) =>
            cmd.HitPayload.DirectDamageEnabled
            || cmd.HitPayload.HitEnergy.Enabled;

        public static bool HasTimedSpawner(in AoeSpawnCommand cmd) =>
            cmd.Lifetime > 0f && cmd.HasTimedSpawner != 0;

        public static CombatArmingComponent ArmingFor(in AoeSpawnCommand cmd) =>
            new() { Remaining = cmd.ArmSeconds };

        public static bool IsArming(in AoeSpawnCommand cmd) => cmd.ArmSeconds > 0f;

        // Single source of truth for a freshly-spawned AOE's enable-gate state
        // (Active / collision / timed-spawn). The reuse fill derives both AOE
        // archetypes' enable bits here, so any new lifecycle phase changes this
        // one function instead of duplicated materialization sites.
        internal readonly struct SpawnState
        {
            public readonly bool Active;
            public readonly bool Collision;
            public readonly bool Timed;

            public SpawnState(bool active, bool collision, bool timed)
            {
                Active = active;
                Collision = collision;
                Timed = timed;
            }
        }

        public static SpawnState SpawnStateFor(in AoeSpawnCommand cmd, bool isLingering)
        {
            bool collision = NeedsCollision(cmd);
            return isLingering
                ? new SpawnState(active: true, collision: collision, timed: HasTimedSpawner(cmd))
                : new SpawnState(active: collision, collision: collision, timed: false);
        }

        public static TimedSpawnStateComponent InitialTimedSpawnStateFor(in AoeSpawnCommand cmd) =>
            new()
            {
                EnergyAccumulated = TimedSpawnInitialEnergy.Roll(cmd.TimedSpawn),
                TickIndex = 0
            };

        public static AoePulseVfxComponent PulseVfxFor(in AoeSpawnCommand cmd)
        {
            float interval = cmd.RepeatHitCooldownSeconds > 0f ? cmd.RepeatHitCooldownSeconds : 0f;
            return new AoePulseVfxComponent { Interval = interval, RemainingInterval = interval };
        }

        public static VfxTimingData VfxTimingFor(in AoeSpawnCommand cmd) =>
            new()
            {
                Duration = cmd.Lifetime,
                TickInterval = cmd.RepeatHitCooldownSeconds
            };

        private static AoeIdentityComponent IdentityFor(in AoeSpawnCommand cmd) =>
            new()
            {
                Faction = cmd.Faction,
                AoeId = cmd.AoeId,
                TypeId = cmd.TypeId,
                SoundIds = cmd.SoundIds,
                SpawnSoundRadius = cmd.SpawnSoundRadius
            };

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
            new() { RepeatHitCooldownSeconds = cmd.RepeatHitCooldownSeconds, Remaining = 0f };

        private static CombatHitPayload HitPayloadFor(CombatHitPayload hitPayload, CombatFaction faction)
        {
            HitEnergyPayload hitEnergy = hitPayload.HitEnergy;
            HitEnergySpawn spawn = hitEnergy.Spawn;
            spawn.Faction = faction;
            hitEnergy.Spawn = spawn;
            hitPayload.HitEnergy = hitEnergy;
            return hitPayload;
        }

        private static AoeAreaComponent AreaFor(in AoeSpawnCommand cmd) =>
            new() { Size = cmd.AreaSize > 0f ? cmd.AreaSize : 1f };

    }
}
