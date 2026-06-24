using PlayGround.System.Common;
using PlayGround.System.Projectile;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Aoe
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CombatLifetimeSystem))]
    [UpdateBefore(typeof(AoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    public partial struct TimedAoeSpawnSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var projectileExpansion = state.World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
            var aoeExpansion = state.World.GetExistingSystemManaged<AoeSpawnExpansionSystem>();
            if (projectileExpansion == null && aoeExpansion == null)
            {
                return;
            }

            bool hasProjectileTemplates = SystemAPI.TryGetSingleton(out ProjectileSpawnTemplate projectileTemplates);
            bool hasAoeTemplates = SystemAPI.TryGetSingleton(out AoeSpawnTemplate aoeTemplates);

            JobHandle handle = new AoeIntervalSpawnJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                ProjectileEventQueue = projectileExpansion != null
                    ? projectileExpansion.EventQueue.AsParallelWriter()
                    : default,
                AoeEventQueue = aoeExpansion != null
                    ? aoeExpansion.EventQueue.AsParallelWriter()
                    : default,
                HasProjectileEventQueue = projectileExpansion != null,
                HasAoeEventQueue = aoeExpansion != null,
                ProjectileTemplates = hasProjectileTemplates ? projectileTemplates.Map : default,
                AoeTemplates = hasAoeTemplates ? aoeTemplates.Map : default,
                HasProjectileTemplates = hasProjectileTemplates,
                HasAoeTemplates = hasAoeTemplates
            }.ScheduleParallel(state.Dependency);

            state.Dependency = handle;

            if (projectileExpansion != null)
            {
                projectileExpansion.ProducerHandle =
                    JobHandle.CombineDependencies(projectileExpansion.ProducerHandle, handle);
            }

            if (aoeExpansion != null)
            {
                aoeExpansion.ProducerHandle =
                    JobHandle.CombineDependencies(aoeExpansion.ProducerHandle, handle);
            }
        }

        [BurstCompile]
        [WithAll(typeof(AoeTag), typeof(Active), typeof(CombatLifetimeComponent), typeof(AoeIntervalSpawnerTag))]
        private partial struct AoeIntervalSpawnJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventQueue;
            public NativeQueue<AoeSpawnEvent>.ParallelWriter AoeEventQueue;
            [ReadOnly] public NativeHashMap<Unity.Entities.Hash128, ProjectileSpawnTemplateData> ProjectileTemplates;
            [ReadOnly] public NativeHashMap<Unity.Entities.Hash128, AoeSpawnTemplateData> AoeTemplates;
            public bool HasProjectileEventQueue;
            public bool HasAoeEventQueue;
            public bool HasProjectileTemplates;
            public bool HasAoeTemplates;

            private void Execute(
                ref AoeIntervalSpawnStateComponent state,
                in AoeIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatLifetimeComponent lifetime,
                in AoeIntervalSpawnerComponent spawner)
            {
                if (lifetime.Remaining <= 0f || identity.Faction == CombatFaction.None)
                {
                    return;
                }

                float cooldown = state.CooldownRemaining - DeltaTime;
                int tickIndex = state.TickIndex;
                while (cooldown <= 0f)
                {
                    tickIndex++;
                    if (spawner.ChildKind == IntervalChildKind.Aoe)
                    {
                        if (HasAoeEventQueue
                            && HasAoeTemplates
                            && AoeTemplates.TryGetValue(spawner.TemplateKey, out AoeSpawnTemplateData child))
                        {
                            EnqueueAoeChildSpawn(identity, kinematics, in spawner, in child, tickIndex);
                        }
                    }
                    else
                    {
                        if (HasProjectileEventQueue
                            && HasProjectileTemplates
                            && ProjectileTemplates.TryGetValue(spawner.TemplateKey, out ProjectileSpawnTemplateData child))
                        {
                            EnqueueProjectileChildSpawn(identity, kinematics, in spawner, in child, tickIndex);
                        }
                    }

                    cooldown += NextIntervalSeconds(
                        identity.AoeId,
                        spawner.JitterSeed,
                        tickIndex,
                        spawner.IntervalSeconds,
                        spawner.IntervalJitterSeconds);
                }

                state.CooldownRemaining = cooldown;
                state.TickIndex = tickIndex;
            }

            private void EnqueueProjectileChildSpawn(
                AoeIdentityComponent parentIdentity,
                CombatKinematicsComponent parentKinematics,
                in AoeIntervalSpawnerComponent spawner,
                in ProjectileSpawnTemplateData child,
                int tickIndex)
            {
                var hitPayload = new ProjectileHitPayload(
                    new CombatHitPayload
                    {
                        DamageAmount = child.DamageAmount,
                        DirectDamageEnabled = child.DirectDamageEnabled,
                        SourceNodeId = default,
                        StackEffect = child.StackEffect
                    },
                    child.ImpactAoe,
                    child.ImpactProjectile);

                ProjectileEventQueue.Enqueue(new ProjectileSpawnEvent
                {
                    Faction = parentIdentity.Faction,
                    BaseProjectileId = parentIdentity.AoeId,
                    TypeId = child.TypeId,
                    HasChildSpawner = 0,
                    SeedContactGateTargetId = 0,
                    Position = parentKinematics.Position,
                    BaseDirection = new float2(1f, 0f),
                    Speed = child.Speed,
                    Count = math.max(1, child.ChildCountPerTick),
                    SpreadDegrees = 0f,
                    JitterDegrees = 0f,
                    JitterSeed = (uint)spawner.JitterSeed,
                    SpawnPatternType = ProjectileChildSpawnPatternType.Radial,
                    DeterministicIdTickIndex = tickIndex,
                    PierceRemaining = child.PierceCount,
                    RepeatHitCooldownSeconds = child.RepeatHitCooldownSeconds,
                    Lifetime = child.Lifetime,
                    Radius = child.Radius,
                    RotationRadians = child.RotationRadians,
                    HalfExtents = child.HalfExtents,
                    ShapeType = child.ShapeType,
                    HitPayload = hitPayload,
                    Tracking = new ProjectileTrackingComponent
                    {
                        TrackingEnabled = child.TrackingEnabled,
                        TrackingTurnSpeedRadians = child.TrackingTurnSpeedRadians,
                        TrackingQueryCooldownRemaining = child.TrackingInitialQueryDelaySeconds,
                        TrackingQueryIntervalSeconds = child.TrackingQueryIntervalSeconds,
                        TrackedTargetId = 0,
                        TrackedTargetIndex = -1,
                        TrackedTargetPosition = default,
                        TrackingRandomState = 0
                    },
                    Render = new CombatRenderComponent
                    {
                        IsRenderable = 1,
                        AlignToVelocity = 1,
                        VisualScale = new float2(child.VisualScale, child.VisualScale),
                        VisualRotationSin = child.VisualRotationSin,
                        VisualRotationCos = child.VisualRotationCos,
                        RenderZ = 0f
                    }
                });
            }

            private void EnqueueAoeChildSpawn(
                AoeIdentityComponent parentIdentity,
                CombatKinematicsComponent parentKinematics,
                in AoeIntervalSpawnerComponent spawner,
                in AoeSpawnTemplateData child,
                int tickIndex)
            {
                AoeEventQueue.Enqueue(new AoeSpawnEvent
                {
                    Faction = parentIdentity.Faction,
                    AoeId = parentIdentity.AoeId,
                    TypeId = child.TypeId,
                    Lifetime = child.Lifetime,
                    RepeatHitCooldownSeconds = child.RepeatHitCooldownSeconds,
                    HitPayload = child.HitPayload,
                    AreaSize = child.AreaSize,
                    Radius = child.Radius,
                    RotationRadians = child.RotationRadians,
                    Position = parentKinematics.Position,
                    HalfExtents = child.HalfExtents,
                    BoundsMin = default,
                    BoundsMax = default,
                    ShapeType = child.ShapeType,
                    Count = math.max(1, child.Count),
                    JitterSeed = (uint)spawner.JitterSeed,
                    DeterministicIdTickIndex = tickIndex,
                    Render = child.Render,
                    ProjectileBurst = child.ProjectileBurst,
                    AoeSpawn = child.AoeSpawn
                });
            }

            private static float NextIntervalSeconds(
                int parentAoeId,
                int jitterSeed,
                int tickIndex,
                float intervalSeconds,
                float intervalJitterSeconds)
            {
                return intervalSeconds
                    + DeterministicJitter(parentAoeId, jitterSeed, tickIndex, intervalJitterSeconds);
            }

            private static float DeterministicJitter(
                int parentAoeId,
                int jitterSeed,
                int tickIndex,
                float maxOffsetSeconds)
            {
                if (maxOffsetSeconds <= 0f)
                {
                    return 0f;
                }

                unchecked
                {
                    uint hash = (uint)parentAoeId;
                    hash = (hash * 397u) ^ (uint)jitterSeed;
                    hash = (hash * 397u) ^ (uint)tickIndex;
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
}
