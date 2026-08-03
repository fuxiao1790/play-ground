using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Profiling;

namespace PlayGround.System.Combat.Projectiles
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSpawnExpansionSystem))]
    [UpdateAfter(typeof(ImpactAoeSpawnExpansionSystem))]
    [UpdateAfter(typeof(LingeringAoeSpawnExpansionSystem))]
    public sealed partial class ProjectileContinuousSpawnApplySystem : SystemBase
    {
        private static readonly ProfilerMarker SpawnMarker = new("ProjectileContinuousSpawnApplySystem");
        private static readonly ProfilerMarker CompleteDependencyMarker =
            new("ProjectileContinuousSpawnApplySystem.CompleteDependency");
        private static readonly ProfilerMarker DrainCommandsMarker =
            new("ProjectileContinuousSpawnApplySystem.DrainCommands");
        private static readonly ProfilerMarker ReuseJobMarker =
            new("ProjectileContinuousSpawnApplySystem.ReuseJob");
        private static readonly ProfilerMarker CreateSlotsMarker =
            new("ProjectileContinuousSpawnApplySystem.CreateSlots");
        private static readonly ProfilerCounterValue<int> SpawnTopUpCounter =
            new(ProfilerCategory.Scripts, "ProjectileContinuousSpawnApplySystem.TopUp", ProfilerMarkerDataUnit.Count);
        private static readonly ProfilerCounterValue<int> SpawnReuseCounter =
            new(ProfilerCategory.Scripts, "ProjectileContinuousSpawnApplySystem.Reuse", ProfilerMarkerDataUnit.Count);

        internal int LastColdCreateCount;
        internal int LastReuseCount;

        private EntityArchetype _archetype;
        private EntityQuery _deadSlotQuery;

        protected override void OnCreate()
        {
            _archetype = EntityManager.CreateArchetype(
                typeof(ProjectileTag),
                typeof(ProjectileContinuousTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(ProjectileContinuousStepComponent),
                typeof(CombatCollisionComponent),
                typeof(CombatLifetimeComponent),
                typeof(ProjectileHitComponent),
                typeof(CombatHitPayload),
                typeof(CombatRenderComponent),
                typeof(CombatRenderAuthoring),
                typeof(CombatRenderKindId),
                typeof(Active),
                typeof(CombatCollisionActiveTag),
                typeof(ArmingTag),
                typeof(CombatArmingComponent),
                typeof(ProjectileContactGateElement),
                typeof(TimedSpawnComponent),
                typeof(TimedSpawnStateComponent));

            _deadSlotQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ProjectileTag>()
                .WithAll<ProjectileContinuousTag>()
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
                if (SystemAPI.TryGetSingleton<ProjectileSpawnEventSingleton>(
                    out ProjectileSpawnEventSingleton projectileLane))
                {
                    projectileLane.PendingHandle.Complete();
                    if (projectileLane.ContinuousCommands.IsCreated)
                    {
                        commands = projectileLane.ContinuousCommands.AsArray();
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

                    new ProjectileContinuousSpawnJob
                    {
                        Commands = commands,
                        Chunks = chunks,
                        ReuseCount = reused,
                        ActiveHandle = GetComponentTypeHandle<Active>(false),
                        CollisionActiveHandle = GetComponentTypeHandle<CombatCollisionActiveTag>(false),
                        IdentityHandle = GetComponentTypeHandle<ProjectileIdentityComponent>(false),
                        KinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                        StepHandle = GetComponentTypeHandle<ProjectileContinuousStepComponent>(false),
                        CollisionHandle = GetComponentTypeHandle<CombatCollisionComponent>(false),
                        LifetimeHandle = GetComponentTypeHandle<CombatLifetimeComponent>(false),
                        HitHandle = GetComponentTypeHandle<ProjectileHitComponent>(false),
                        HitPayloadHandle = GetComponentTypeHandle<CombatHitPayload>(false),
                        RenderHandle = GetComponentTypeHandle<CombatRenderComponent>(false),
                        AuthoringHandle = GetComponentTypeHandle<CombatRenderAuthoring>(false),
                        RenderBatchIdHandle = GetComponentTypeHandle<CombatRenderKindId>(false),
                        ContactGateHandle = GetBufferTypeHandle<ProjectileContactGateElement>(false),
                        ArmingHandle = GetComponentTypeHandle<CombatArmingComponent>(false),
                        ArmingTagHandle = GetComponentTypeHandle<ArmingTag>(false),
                        TimedSpawnHandle = GetComponentTypeHandle<TimedSpawnComponent>(false),
                        TimedSpawnStateHandle = GetComponentTypeHandle<TimedSpawnStateComponent>(false),
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
        private struct ProjectileContinuousSpawnJob : IJob
        {
            [ReadOnly] public NativeArray<ProjectileSpawnCommand> Commands;
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            public NativeReference<int> ReuseCount;

            public ComponentTypeHandle<Active> ActiveHandle;
            public ComponentTypeHandle<CombatCollisionActiveTag> CollisionActiveHandle;
            public ComponentTypeHandle<ProjectileIdentityComponent> IdentityHandle;
            public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            public ComponentTypeHandle<ProjectileContinuousStepComponent> StepHandle;
            public ComponentTypeHandle<CombatCollisionComponent> CollisionHandle;
            public ComponentTypeHandle<CombatLifetimeComponent> LifetimeHandle;
            public ComponentTypeHandle<ProjectileHitComponent> HitHandle;
            public ComponentTypeHandle<CombatHitPayload> HitPayloadHandle;
            public ComponentTypeHandle<CombatRenderComponent> RenderHandle;
            public ComponentTypeHandle<CombatRenderAuthoring> AuthoringHandle;
            public ComponentTypeHandle<CombatRenderKindId> RenderBatchIdHandle;
            public BufferTypeHandle<ProjectileContactGateElement> ContactGateHandle;
            public ComponentTypeHandle<CombatArmingComponent> ArmingHandle;
            public ComponentTypeHandle<ArmingTag> ArmingTagHandle;
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
                    EnabledMask armingMask = chunk.GetEnabledMask(ref ArmingTagHandle);
                    EnabledMask timedSpawnMask = chunk.GetEnabledMask(ref TimedSpawnHandle);

                    NativeArray<ProjectileIdentityComponent> identities =
                        chunk.GetNativeArray(ref IdentityHandle);
                    NativeArray<CombatKinematicsComponent> kinematics =
                        chunk.GetNativeArray(ref KinematicsHandle);
                    NativeArray<ProjectileContinuousStepComponent> steps =
                        chunk.GetNativeArray(ref StepHandle);
                    NativeArray<CombatCollisionComponent> collisions =
                        chunk.GetNativeArray(ref CollisionHandle);
                    NativeArray<CombatLifetimeComponent> lifetimes =
                        chunk.GetNativeArray(ref LifetimeHandle);
                    NativeArray<ProjectileHitComponent> hits = chunk.GetNativeArray(ref HitHandle);
                    NativeArray<CombatHitPayload> hitPayloads =
                        chunk.GetNativeArray(ref HitPayloadHandle);
                    NativeArray<CombatRenderComponent> renders =
                        chunk.GetNativeArray(ref RenderHandle);
                    NativeArray<CombatRenderAuthoring> authorings =
                        chunk.GetNativeArray(ref AuthoringHandle);
                    NativeArray<CombatRenderKindId> batchIds =
                        chunk.GetNativeArray(ref RenderBatchIdHandle);
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
                            gates,
                            armings,
                            timedSpawns,
                            timedSpawnStates,
                            activeMask,
                            collisionActiveMask,
                            armingMask,
                            timedSpawnMask,
                            i);
                        steps[i] = new ProjectileContinuousStepComponent { Origin = cfg.Position };
                    }
                }

                ReuseCount.Value = commandIndex;
            }
        }
    }
}
