using PlayGround.System.Combat.Aoes;
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
using PlayGround.System.Combat.Stats;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace PlayGround.System.Combat.Projectiles
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSpawnExpansionSystem))]
    [UpdateAfter(typeof(ImpactAoeSpawnExpansionSystem))]
    [UpdateAfter(typeof(LingeringAoeSpawnExpansionSystem))]
    public sealed partial class ProjectileDiscreteSpawnApplySystem : SystemBase
    {
        private static readonly ProfilerMarker SpawnMarker = new("ProjectileDiscreteSpawnApplySystem");
        private static readonly ProfilerMarker CompleteDependencyMarker =
            new("ProjectileDiscreteSpawnApplySystem.CompleteDependency");
        private static readonly ProfilerMarker DrainCommandsMarker =
            new("ProjectileDiscreteSpawnApplySystem.DrainCommands");
        private static readonly ProfilerMarker ReuseJobMarker =
            new("ProjectileDiscreteSpawnApplySystem.ReuseJob");
        private static readonly ProfilerMarker CreateSlotsMarker =
            new("ProjectileDiscreteSpawnApplySystem.CreateSlots");
        private static readonly ProfilerCounterValue<int> SpawnTopUpCounter =
            new(ProfilerCategory.Scripts, "ProjectileDiscreteSpawnApplySystem.TopUp", ProfilerMarkerDataUnit.Count);
        private static readonly ProfilerCounterValue<int> SpawnReuseCounter =
            new(ProfilerCategory.Scripts, "ProjectileDiscreteSpawnApplySystem.Reuse", ProfilerMarkerDataUnit.Count);

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
                typeof(CombatHitPayload),
                typeof(ProjectileTrackingComponent),
                typeof(CombatRenderComponent),
                typeof(CombatRenderAuthoring),
                typeof(CombatRenderKindId),
                typeof(ProjectileTrailVfxComponent),
                typeof(Active),
                typeof(CombatCollisionActiveTag),
                typeof(ArmingTag),
                typeof(CombatArmingComponent),
                typeof(ProjectileContactGateElement),
                typeof(TimedSpawnComponent),
                typeof(TimedSpawnStateComponent));

            _deadSlotQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ProjectileTag>()
                .WithNone<ProjectileContinuousTag>()
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

            SpawnTemplateRegistryState registryState = SystemAPI.GetSingleton<SpawnTemplateRegistryState>();

            int totalRequests = 0;
            NativeArray<ProjectileSpawnCommand> commands = default;
            using (DrainCommandsMarker.Auto())
            {
                if (SystemAPI.TryGetSingleton<ProjectileSpawnEventSingleton>(
                    out ProjectileSpawnEventSingleton projectileLane))
                {
                    projectileLane.PendingHandle.Complete();
                    if (projectileLane.DiscreteCommands.IsCreated)
                    {
                        commands = projectileLane.DiscreteCommands.AsArray();
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

            RefRW<SoundEventSingleton> sounds =
                SystemAPI.GetSingletonRW<SoundEventSingleton>();
            SoundEventLane.Reserve(ref sounds.ValueRW, totalRequests);

            using (SpawnMarker.Auto())
            {
                int created = SpawnPoolTopUp.EnsureDisabledSlots(
                    EntityManager,
                    _archetype,
                    _deadSlotQuery,
                    totalRequests,
                    CreateSlotsMarker);
                int reuseCount = 0;

                using (ReuseJobMarker.Auto())
                {
                    using NativeArray<ArchetypeChunk> chunks =
                        _deadSlotQuery.ToArchetypeChunkArray(Allocator.TempJob);
                    using var reused = new NativeReference<int>(Allocator.TempJob);

                    new ProjectileSpawnJob
                    {
                        Commands = commands,
                        Chunks = chunks,
                        ReuseCount = reused,
                        ActiveHandle = GetComponentTypeHandle<Active>(false),
                        CollisionActiveHandle = GetComponentTypeHandle<CombatCollisionActiveTag>(false),
                        IdentityHandle = GetComponentTypeHandle<ProjectileIdentityComponent>(false),
                        KinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                        CollisionHandle = GetComponentTypeHandle<CombatCollisionComponent>(false),
                        LifetimeHandle = GetComponentTypeHandle<CombatLifetimeComponent>(false),
                        HitHandle = GetComponentTypeHandle<ProjectileHitComponent>(false),
                        HitPayloadHandle = GetComponentTypeHandle<CombatHitPayload>(false),
                        TrackingHandle = GetComponentTypeHandle<ProjectileTrackingComponent>(false),
                        RenderHandle = GetComponentTypeHandle<CombatRenderComponent>(false),
                        AuthoringHandle = GetComponentTypeHandle<CombatRenderAuthoring>(false),
                        RenderBatchIdHandle = GetComponentTypeHandle<CombatRenderKindId>(false),
                        TrailVfxHandle = GetComponentTypeHandle<ProjectileTrailVfxComponent>(false),
                        ContactGateHandle = GetBufferTypeHandle<ProjectileContactGateElement>(false),
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
        private struct ProjectileSpawnJob : IJob
        {
            [ReadOnly] public NativeArray<ProjectileSpawnCommand> Commands;
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            public NativeReference<int> ReuseCount;

            public ComponentTypeHandle<Active> ActiveHandle;
            public ComponentTypeHandle<CombatCollisionActiveTag> CollisionActiveHandle;
            public ComponentTypeHandle<ProjectileIdentityComponent> IdentityHandle;
            public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            public ComponentTypeHandle<CombatCollisionComponent> CollisionHandle;
            public ComponentTypeHandle<CombatLifetimeComponent> LifetimeHandle;
            public ComponentTypeHandle<ProjectileHitComponent> HitHandle;
            public ComponentTypeHandle<CombatHitPayload> HitPayloadHandle;
            public ComponentTypeHandle<ProjectileTrackingComponent> TrackingHandle;
            public ComponentTypeHandle<CombatRenderComponent> RenderHandle;
            public ComponentTypeHandle<CombatRenderAuthoring> AuthoringHandle;
            public ComponentTypeHandle<CombatRenderKindId> RenderBatchIdHandle;
            public ComponentTypeHandle<ProjectileTrailVfxComponent> TrailVfxHandle;
            public BufferTypeHandle<ProjectileContactGateElement> ContactGateHandle;
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
                     chunkIndex < Chunks.Length && commandIndex < Commands.Length;
                     chunkIndex++)
                {
                    ArchetypeChunk chunk = Chunks[chunkIndex];
                    EnabledMask activeMask = chunk.GetEnabledMask(ref ActiveHandle);
                    EnabledMask collisionActiveMask = chunk.GetEnabledMask(ref CollisionActiveHandle);
                    EnabledMask trackingMask = chunk.GetEnabledMask(ref TrackingHandle);
                    EnabledMask armingMask = chunk.GetEnabledMask(ref ArmingTagHandle);
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
                    NativeArray<CombatHitPayload> hitPayloads =
                        chunk.GetNativeArray(ref HitPayloadHandle);
                    NativeArray<ProjectileTrackingComponent> tracking =
                        chunk.GetNativeArray(ref TrackingHandle);
                    NativeArray<CombatRenderComponent> renders =
                        chunk.GetNativeArray(ref RenderHandle);
                    NativeArray<CombatRenderAuthoring> authorings =
                        chunk.GetNativeArray(ref AuthoringHandle);
                    NativeArray<CombatRenderKindId> batchIds =
                        chunk.GetNativeArray(ref RenderBatchIdHandle);
                    NativeArray<ProjectileTrailVfxComponent> trailVfx =
                        chunk.GetNativeArray(ref TrailVfxHandle);
                    BufferAccessor<ProjectileContactGateElement> gates =
                        chunk.GetBufferAccessor(ref ContactGateHandle);
                    NativeArray<CombatArmingComponent> armings =
                        chunk.GetNativeArray(ref ArmingHandle);
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

                        ProjectileSpawnApplyUtility.WriteCommon(
                            cfg,
                            identities,
                            kinematics,
                            collisions,
                            lifetimes,
                            hits,
                            hitPayloads,
                            renders,
                            authorings,
                            batchIds,
                            trailVfx,
                            gates,
                            armings,
                            timedSpawns,
                            timedSpawnStates,
                            activeMask,
                            collisionActiveMask,
                            armingMask,
                            timedSpawnMask,
                            Deltas,
                            i);
                        tracking[i] = cfg.Tracking;
                        ProjectileSpawnApplyUtility.SpawnState spawnState =
                            ProjectileSpawnApplyUtility.SpawnStateFor(cfg);
                        trackingMask[i] = spawnState.Tracking;
                        SoundEmit.Enqueue(
                            cfg.SoundIds.SpawnId,
                            cfg.Position,
                            cfg.SpawnSoundRadius,
                            SoundCategory.Spawn,
                            SoundEventsByClip,
                            SoundClipIds);
                    }
                }

                ReuseCount.Value = commandIndex;
            }
        }
    }

    // Shared materialization for projectile apply lanes. Tracking state stays in the discrete
    // lane because the continuous archetype does not contain ProjectileTrackingComponent.
    internal static class ProjectileSpawnApplyUtility
    {
        public static void WriteCommon(
            in ProjectileSpawnCommand cfg,
            NativeArray<ProjectileIdentityComponent> identities,
            NativeArray<CombatKinematicsComponent> kinematics,
            NativeArray<CombatCollisionComponent> collisions,
            NativeArray<CombatLifetimeComponent> lifetimes,
            NativeArray<ProjectileHitComponent> hits,
            NativeArray<CombatHitPayload> hitPayloads,
            NativeArray<CombatRenderComponent> renders,
            NativeArray<CombatRenderAuthoring> authorings,
            NativeArray<CombatRenderKindId> batchIds,
            NativeArray<ProjectileTrailVfxComponent> trailVfx,
            BufferAccessor<ProjectileContactGateElement> gates,
            NativeArray<CombatArmingComponent> armings,
            NativeArray<TimedSpawnComponent> timedSpawns,
            NativeArray<TimedSpawnStateComponent> timedSpawnStates,
            EnabledMask activeMask,
            EnabledMask collisionActiveMask,
            EnabledMask armingMask,
            EnabledMask timedSpawnMask,
            NativeQueue<SpawnTemplateRefDelta>.ParallelWriter deltas,
            int index)
        {
            identities[index] = new ProjectileIdentityComponent
            {
                Faction = cfg.Faction,
                ProjectileId = cfg.ProjectileId,
                TypeId = cfg.TypeId
            };
            kinematics[index] = new CombatKinematicsComponent
            {
                Position = cfg.Position,
                Velocity = cfg.Velocity
            };
            collisions[index] = new CombatCollisionComponent
            {
                ShapeType = cfg.ShapeType,
                Radius = cfg.Radius,
                HalfExtents = cfg.HalfExtents,
                RotationRadians = cfg.RotationRadians,
                BoundsMin = cfg.BoundsMin,
                BoundsMax = cfg.BoundsMax
            };
            lifetimes[index] = new CombatLifetimeComponent { Remaining = cfg.Lifetime };
            hits[index] = new ProjectileHitComponent
            {
                PierceRemaining = cfg.PierceRemaining,
                RepeatHitCooldownSeconds = cfg.RepeatHitCooldownSeconds,
                OnHitSpawn = cfg.HitPayload.OnHitSpawn
            };
            hitPayloads[index] = HitPayloadFor(in cfg, cfg.Faction);
            renders[index] = cfg.Render;
            authorings[index] = cfg.Authoring;
            batchIds[index] = new CombatRenderKindId { Value = cfg.RenderTypeId };
            trailVfx[index] = new ProjectileTrailVfxComponent
            {
                TrailId = cfg.TrailVfxId,
                Width = cfg.TrailWidth,
                StepDistance = cfg.TrailStepDistance,
                LastEmitPosition = cfg.Position
            };

            DynamicBuffer<ProjectileContactGateElement> gate = gates[index];
            gate.Clear();
            if (cfg.SeedContactGateTargetId > 0)
            {
                gate.Add(new ProjectileContactGateElement
                {
                    TargetId = cfg.SeedContactGateTargetId,
                    CooldownRemaining = math.max(0.1f, cfg.RepeatHitCooldownSeconds)
                });
            }

            SpawnState spawnState = SpawnStateFor(cfg);
            bool hasTimedSpawner = spawnState.Timed;
            timedSpawns[index] = hasTimedSpawner ? cfg.TimedSpawn : default;
            timedSpawnStates[index] = hasTimedSpawner ? InitialTimedSpawnStateFor(cfg) : default;
            timedSpawnMask[index] = spawnState.Timed;
            activeMask[index] = spawnState.Active;
            collisionActiveMask[index] = spawnState.Collision;
            armings[index] = ArmingFor(cfg);
            armingMask[index] = IsArming(cfg);

            // Spawn event, emitted after every component is written so it reads the entity's
            // own state and stays the mirror image of the release the death path emits. The
            // slot's previous occupant already released its own keys when it died.
            if (spawnState.Active)
            {
                SpawnTemplateRefEmit.AcquireProjectile(
                    hits[index],
                    timedSpawns[index],
                    hitPayloads[index],
                    deltas);
            }
        }

        public static bool NeedsCollision(in ProjectileHitPayload payload) =>
            payload.DirectDamageEnabled
            || payload.StackEffect.Enabled
            || payload.OnHitSpawn.Enabled;

        public readonly struct SpawnState
        {
            public readonly bool Active;
            public readonly bool Collision;
            public readonly bool Tracking;
            public readonly bool Timed;

            public SpawnState(bool active, bool collision, bool tracking, bool timed)
            {
                Active = active;
                Collision = collision;
                Tracking = tracking;
                Timed = timed;
            }
        }

        public static SpawnState SpawnStateFor(in ProjectileSpawnCommand cmd) =>
            new(
                active: true,
                collision: NeedsCollision(cmd.HitPayload),
                tracking: cmd.Tracking.TrackingEnabled,
                timed: cmd.HasTimedSpawner != 0);

        public static CombatArmingComponent ArmingFor(in ProjectileSpawnCommand cmd) =>
            new() { Remaining = cmd.ArmSeconds };

        public static bool IsArming(in ProjectileSpawnCommand cmd) => cmd.ArmSeconds > 0f;

        public static CombatHitPayload HitPayloadFor(in ProjectileSpawnCommand cmd, CombatFaction faction)
        {
            CombatHitPayload hitPayload = cmd.HitPayload.HitPayload;
            StackEffectSnapshot stack = hitPayload.StackEffect;
            stack.Faction = faction;
            hitPayload.StackEffect = stack;
            return hitPayload;
        }

        public static TimedSpawnStateComponent InitialTimedSpawnStateFor(in ProjectileSpawnCommand cmd) =>
            new()
            {
                EnergyAccumulated = TimedSpawnInitialEnergy.Roll(cmd.TimedSpawn),
                TickIndex = 0
            };
    }
}
