using PlayGround.System.Common;
using PlayGround.System.Aoe;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    // Replaces ProjectileChildSpawnSystem: enqueues ProjectileSpawnEvent into the expansion
    // queue instead of ECB-appending ProjectileSpawnRequestElement to the scope buffer.
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

            JobHandle handle = new ProjectileChildSpawnEntityJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                ProjectileEventQueue = expansion.EventQueue.AsParallelWriter(),
                AoeEventQueue = aoeExpansion != null ? aoeExpansion.EventQueue.AsParallelWriter() : default,
                HasAoeEventQueue = aoeExpansion != null
            }.ScheduleParallel(state.Dependency);

            state.Dependency = handle;

            // Expansion reads EventQueue on the main thread and only completes its own
            // component-derived dependency; forward this write job so it waits on us.
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
            public bool HasAoeEventQueue;

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
                        int childCount = math.max(1, aoeSpawner.Child.Count);
                        for (int childIndex = 0; childIndex < childCount; childIndex++)
                        {
                            EnqueueAoeChildSpawn(identity, kinematics, in aoeSpawner, tickIndex, childIndex);
                        }
                        cooldown += NextIntervalSeconds(
                            identity.ProjectileId,
                            aoeSpawner.SpawnerId,
                            tickIndex,
                            aoeSpawner.IntervalSeconds,
                            aoeSpawner.IntervalJitterSeconds);
                    }
                    else
                    {
                        int childCount = math.max(1, spawner.Child.ChildCountPerTick);
                        for (int childIndex = 0; childIndex < childCount; childIndex++)
                        {
                            EnqueueProjectileChildSpawn(identity, kinematics, in spawner, tickIndex, childIndex);
                        }
                        cooldown += NextIntervalSeconds(
                            identity.ProjectileId,
                            spawner.SpawnerId,
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
                int tickIndex,
                int childIndex)
            {
                IntervalProjectileChild child = spawner.Child;
                float2 velocity = ComputeChildVelocity(parentKinematics, in spawner, childIndex);
                float speed = math.length(velocity);
                float2 baseDirection = speed > 0.0001f
                    ? velocity / speed
                    : math.normalizesafe(parentKinematics.Velocity, new float2(1f, 0f));

                int childProjectileId = ChildProjectileId(parentIdentity.ProjectileId, spawner.SpawnerId, tickIndex, childIndex);

                var hitPayload = new ProjectileHitPayload(
                    new CombatHitPayload
                    {
                        DamageAmount = child.DamageAmount,
                        DirectDamageEnabled = child.DirectDamageEnabled,
                        SourceNodeId = child.SourceNodeId,
                        StackEffect = child.StackEffect
                    },
                    child.ImpactAoe,
                    child.ImpactProjectile);

                ProjectileEventQueue.Enqueue(new ProjectileSpawnEvent
                {
                    Faction = parentIdentity.Faction,
                    BaseProjectileId = childProjectileId,
                    TypeId = child.TypeId,
                    HasChildSpawner = 0,
                    SeedContactGateTargetId = 0,
                    Position = parentKinematics.Position,
                    BaseDirection = baseDirection,
                    Speed = speed,
                    Count = 1,
                    SpreadDegrees = 0f,
                    JitterDegrees = 0f,
                    JitterSeed = 0u,
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
                        RenderZ = 0f // expansion overrides per-id
                    }
                });
            }

            private void EnqueueAoeChildSpawn(
                ProjectileIdentityComponent parentIdentity,
                CombatKinematicsComponent parentKinematics,
                in AoeIntervalSpawnerComponent spawner,
                int tickIndex,
                int childIndex)
            {
                if (!HasAoeEventQueue)
                {
                    return;
                }

                IntervalAoeChild child = spawner.Child;
                AoeEventQueue.Enqueue(new AoeSpawnEvent
                {
                    Faction = parentIdentity.Faction,
                    AoeId = ChildProjectileId(parentIdentity.ProjectileId, spawner.SpawnerId, tickIndex, childIndex),
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
                    Render = child.Render,
                    ProjectileBurst = child.ProjectileBurst,
                    AoeSpawn = child.AoeSpawn
                });
            }

            private static float2 ComputeChildVelocity(
                CombatKinematicsComponent parentKinematics,
                in ProjectileChildSpawnerComponent spawner,
                int childIndex)
            {
                IntervalProjectileChild child = spawner.Child;
                float2 forward = math.normalizesafe(parentKinematics.Velocity, new float2(1f, 0f));
                float2 dir;
                switch (child.SpawnPatternType)
                {
                    case ProjectileChildSpawnPatternType.SideSpray:
                    {
                        float2 left  = new float2(-forward.y,  forward.x);
                        float2 right = new float2( forward.y, -forward.x);
                        bool isLeft  = (childIndex & 1) == 0;
                        int leftCount  = (child.ChildCountPerTick + 1) / 2;
                        int rightCount =  child.ChildCountPerTick / 2;
                        int sideIndex  = childIndex / 2;
                        int sideCount  = isLeft ? leftCount : rightCount;
                        float2 sideDir = isLeft ? left : right;
                        float spreadRad = math.radians(child.SideSpreadDegrees);
                        float angle = SideSpreadAngle(spreadRad, sideIndex, sideCount);
                        dir = Rotate(sideDir, angle);
                        break;
                    }
                    default:
                        dir = forward;
                        break;
                }
                return child.Speed > 0f ? dir * child.Speed : dir * math.length(parentKinematics.Velocity);
            }

            private static float SideSpreadAngle(float totalRad, int shotIndex, int shotCount)
            {
                if (shotCount <= 1) return 0f;
                return -totalRad * 0.5f + totalRad / (shotCount - 1) * shotIndex;
            }

            private static float2 Rotate(float2 v, float radians)
            {
                math.sincos(radians, out float s, out float c);
                return new float2(c * v.x - s * v.y, s * v.x + c * v.y);
            }

            private static int ChildProjectileId(int parentProjectileId, int spawnerId, int tickIndex, int childIndex)
            {
                unchecked
                {
                    int hash = parentProjectileId;
                    hash = (hash * 397) ^ spawnerId;
                    hash = (hash * 397) ^ tickIndex;
                    hash = (hash * 397) ^ childIndex;
                    hash &= int.MaxValue;
                    return hash == 0 ? 1 : hash;
                }
            }

            private static float NextIntervalSeconds(
                int parentProjectileId,
                int spawnerId,
                int tickIndex,
                float intervalSeconds,
                float intervalJitterSeconds)
            {
                return intervalSeconds
                    + DeterministicJitter(parentProjectileId, spawnerId, tickIndex, intervalJitterSeconds);
            }

            private static float DeterministicJitter(
                int parentProjectileId,
                int spawnerId,
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
                    hash = (hash * 397u) ^ (uint)spawnerId;
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
