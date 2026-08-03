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
    public sealed partial class SweptProjectileSpawnApplySystem : SystemBase
    {
        private static readonly ProfilerMarker SpawnMarker = new("SweptProjectileSpawnApplySystem");
        private static readonly ProfilerMarker CompleteDependencyMarker =
            new("SweptProjectileSpawnApplySystem.CompleteDependency");
        private static readonly ProfilerMarker DrainCommandsMarker =
            new("SweptProjectileSpawnApplySystem.DrainCommands");
        private static readonly ProfilerMarker ReuseJobMarker =
            new("SweptProjectileSpawnApplySystem.ReuseJob");
        private static readonly ProfilerMarker CreateSlotsMarker =
            new("SweptProjectileSpawnApplySystem.CreateSlots");
        private static readonly ProfilerCounterValue<int> SpawnTopUpCounter =
            new(ProfilerCategory.Scripts, "SweptProjectileSpawnApplySystem.TopUp", ProfilerMarkerDataUnit.Count);
        private static readonly ProfilerCounterValue<int> SpawnReuseCounter =
            new(ProfilerCategory.Scripts, "SweptProjectileSpawnApplySystem.Reuse", ProfilerMarkerDataUnit.Count);

        internal int LastColdCreateCount;
        internal int LastReuseCount;

        private EntityArchetype _archetype;
        private EntityQuery _deadSlotQuery;

        protected override void OnCreate()
        {
            _archetype = EntityManager.CreateArchetype(
                typeof(ProjectileTag),
                typeof(SweptProjectileTag),
                typeof(ProjectileIdentityComponent),
                typeof(CombatKinematicsComponent),
                typeof(ProjectileSweepComponent),
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
                .WithAll<SweptProjectileTag>()
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
                    if (projectileLane.SweptCommands.IsCreated)
                    {
                        commands = projectileLane.SweptCommands.AsArray();
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

                    new SweptProjectileSpawnJob
                    {
                        Commands = commands,
                        Chunks = chunks,
                        ReuseCount = reused,
                        ActiveHandle = GetComponentTypeHandle<Active>(false),
                        CollisionActiveHandle = GetComponentTypeHandle<CombatCollisionActiveTag>(false),
                        IdentityHandle = GetComponentTypeHandle<ProjectileIdentityComponent>(false),
                        KinematicsHandle = GetComponentTypeHandle<CombatKinematicsComponent>(false),
                        SweepHandle = GetComponentTypeHandle<ProjectileSweepComponent>(false),
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
        private struct SweptProjectileSpawnJob : IJob
        {
            [ReadOnly] public NativeArray<ProjectileSpawnCommand> Commands;
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            public NativeReference<int> ReuseCount;

            public ComponentTypeHandle<Active> ActiveHandle;
            public ComponentTypeHandle<CombatCollisionActiveTag> CollisionActiveHandle;
            public ComponentTypeHandle<ProjectileIdentityComponent> IdentityHandle;
            public ComponentTypeHandle<CombatKinematicsComponent> KinematicsHandle;
            public ComponentTypeHandle<ProjectileSweepComponent> SweepHandle;
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
                    NativeArray<ProjectileSweepComponent> sweeps =
                        chunk.GetNativeArray(ref SweepHandle);
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
                        sweeps[i] = new ProjectileSweepComponent { Origin = cfg.Position };
                    }
                }

                ReuseCount.Value = commandIndex;
            }
        }
    }
}
