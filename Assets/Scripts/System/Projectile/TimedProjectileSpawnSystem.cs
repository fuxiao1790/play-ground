using PlayGround.System.Common;
using PlayGround.System.Aoe;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CombatLifetimeSystem))]
    [UpdateBefore(typeof(ProjectileTrackingSystem))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    public partial struct TimedProjectileSpawnSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var expansion = state.World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();
            if (expansion == null)
            {
                return;
            }

            var aoeExpansion = state.World.GetExistingSystemManaged<AoeSpawnExpansionSystem>();
            bool hasProjectileTemplates = SystemAPI.TryGetSingleton(out ProjectileSpawnTemplate projectileTemplates);
            bool hasAoeTemplates = SystemAPI.TryGetSingleton(out AoeSpawnTemplate aoeTemplates);

            JobHandle handle = new ProjectileChildSpawnEntityJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                ProjectileEventQueue = expansion.EventQueue.AsParallelWriter(),
                AoeEventQueue = aoeExpansion != null ? aoeExpansion.EventQueue.AsParallelWriter() : default,
                HasAoeEventQueue = aoeExpansion != null,
                ProjectileTemplates = hasProjectileTemplates ? projectileTemplates.Map : default,
                AoeTemplates = hasAoeTemplates ? aoeTemplates.Map : default,
                HasProjectileTemplates = hasProjectileTemplates,
                HasAoeTemplates = hasAoeTemplates
            }.ScheduleParallel(state.Dependency);

            state.Dependency = handle;
            expansion.ProducerHandle = JobHandle.CombineDependencies(expansion.ProducerHandle, handle);
            if (aoeExpansion != null)
            {
                aoeExpansion.ProducerHandle = JobHandle.CombineDependencies(aoeExpansion.ProducerHandle, handle);
            }
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(Active), typeof(ProjectileChildSpawnerTag))]
        private partial struct ProjectileChildSpawnEntityJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter ProjectileEventQueue;
            public NativeQueue<AoeSpawnEvent>.ParallelWriter AoeEventQueue;
            [ReadOnly] public NativeHashMap<Unity.Entities.Hash128, ProjectileSpawnTemplateData> ProjectileTemplates;
            [ReadOnly] public NativeHashMap<Unity.Entities.Hash128, AoeSpawnTemplateData> AoeTemplates;
            public bool HasAoeEventQueue;
            public bool HasProjectileTemplates;
            public bool HasAoeTemplates;

            private void Execute(
                ref ProjectileChildSpawnStateComponent childSpawnState,
                in ProjectileIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatLifetimeComponent lifetime,
                in ProjectileChildSpawnerComponent spawner,
                in AoeIntervalSpawnerComponent aoeSpawner)
            {
                if (lifetime.Remaining <= 0f || identity.Faction == CombatFaction.None)
                {
                    return;
                }

                float cooldown = childSpawnState.ChildSpawnCooldownRemaining - DeltaTime;
                int tickIndex = childSpawnState.ChildSpawnTickIndex;
                while (cooldown <= 0f)
                {
                    tickIndex++;
                    if (childSpawnState.ChildKind == IntervalChildKind.Aoe)
                    {
                        if (HasAoeEventQueue
                            && HasAoeTemplates
                            && AoeTemplates.TryGetValue(aoeSpawner.TemplateKey, out AoeSpawnTemplateData child))
                        {
                            EnqueueAoeChildSpawn(identity, kinematics, in aoeSpawner, in child, tickIndex);
                        }

                        cooldown += NextIntervalSeconds(
                            identity.ProjectileId,
                            aoeSpawner.JitterSeed,
                            tickIndex,
                            aoeSpawner.IntervalSeconds,
                            aoeSpawner.IntervalJitterSeconds);
                    }
                    else
                    {
                        if (HasProjectileTemplates
                            && ProjectileTemplates.TryGetValue(spawner.TemplateKey, out ProjectileSpawnTemplateData child))
                        {
                            EnqueueProjectileChildSpawn(identity, kinematics, in spawner, in child, tickIndex);
                        }

                        cooldown += NextIntervalSeconds(
                            identity.ProjectileId,
                            spawner.JitterSeed,
                            tickIndex,
                            spawner.IntervalSeconds,
                            spawner.IntervalJitterSeconds);
                    }
                }

                childSpawnState.ChildSpawnCooldownRemaining = cooldown;
                childSpawnState.ChildSpawnTickIndex = tickIndex;
            }

            private void EnqueueProjectileChildSpawn(
                ProjectileIdentityComponent parentIdentity,
                CombatKinematicsComponent parentKinematics,
                in ProjectileChildSpawnerComponent spawner,
                in ProjectileSpawnTemplateData child,
                int tickIndex)
            {
                float parentSpeed = math.length(parentKinematics.Velocity);
                float speed = child.Speed > 0f ? child.Speed : parentSpeed;
                float2 baseDirection = math.normalizesafe(parentKinematics.Velocity, new float2(1f, 0f));
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
                    BaseProjectileId = parentIdentity.ProjectileId,
                    TypeId = child.TypeId,
                    HasChildSpawner = 0,
                    SeedContactGateTargetId = 0,
                    Position = parentKinematics.Position,
                    BaseDirection = baseDirection,
                    Speed = speed,
                    Count = math.max(1, child.ChildCountPerTick),
                    SpreadDegrees = child.SideSpreadDegrees,
                    JitterDegrees = 0f,
                    JitterSeed = (uint)spawner.JitterSeed,
                    SpawnPatternType = child.SpawnPatternType,
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
                ProjectileIdentityComponent parentIdentity,
                CombatKinematicsComponent parentKinematics,
                in AoeIntervalSpawnerComponent spawner,
                in AoeSpawnTemplateData child,
                int tickIndex)
            {
                AoeEventQueue.Enqueue(new AoeSpawnEvent
                {
                    Faction = parentIdentity.Faction,
                    AoeId = parentIdentity.ProjectileId,
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
                int parentProjectileId,
                int jitterSeed,
                int tickIndex,
                float intervalSeconds,
                float intervalJitterSeconds)
            {
                return intervalSeconds
                    + DeterministicJitter(parentProjectileId, jitterSeed, tickIndex, intervalJitterSeconds);
            }

            private static float DeterministicJitter(
                int parentProjectileId,
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
                    uint hash = (uint)parentProjectileId;
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
