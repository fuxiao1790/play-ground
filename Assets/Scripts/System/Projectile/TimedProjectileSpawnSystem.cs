using PlayGround.System.Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    // Replaces ProjectileChildSpawnSystem: enqueues ProjectileSpawnEvent into the expansion
    // queue instead of ECB-appending ProjectileSpawnRequestElement to the scope buffer.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileMovementSystem))]
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

            state.Dependency = new ProjectileChildSpawnEntityJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                EventQueue = expansion.EventQueue.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(ProjectileActiveTag), typeof(ProjectileChildSpawnerTag))]
        private partial struct ProjectileChildSpawnEntityJob : IJobEntity
        {
            public float DeltaTime;
            public NativeQueue<ProjectileSpawnEvent>.ParallelWriter EventQueue;

            private void Execute(
                ref ProjectileChildSpawnStateComponent childSpawnState,
                in ProjectileIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatLifetimeComponent lifetime,
                in ProjectileChildSpawnerComponent spawner)
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
                    for (int childIndex = 0; childIndex < spawner.ChildCountPerTick; childIndex++)
                    {
                        EnqueueChildSpawn(identity, kinematics, in spawner, tickIndex, childIndex);
                    }
                    cooldown += NextIntervalSeconds(identity.ProjectileId, in spawner, tickIndex);
                }

                childSpawnState.ChildSpawnCooldownRemaining = cooldown;
                childSpawnState.ChildSpawnTickIndex = tickIndex;
            }

            private void EnqueueChildSpawn(
                ProjectileIdentityComponent parentIdentity,
                CombatKinematicsComponent parentKinematics,
                in ProjectileChildSpawnerComponent spawner,
                int tickIndex,
                int childIndex)
            {
                float2 velocity = ComputeChildVelocity(parentKinematics, in spawner, childIndex);
                float speed = math.length(velocity);
                float2 baseDirection = speed > 0.0001f
                    ? velocity / speed
                    : math.normalizesafe(parentKinematics.Velocity, new float2(1f, 0f));

                int childProjectileId = ChildProjectileId(parentIdentity.ProjectileId, spawner.SpawnerId, tickIndex, childIndex);

                var hitPayload = new ProjectileHitPayload(
                    new CombatHitPayload
                    {
                        DamageAmount = spawner.DamageAmount,
                        DirectDamageEnabled = spawner.DirectDamageEnabled,
                        SourceNodeId = spawner.SourceNodeId,
                        StackEffect = spawner.StackEffect
                    },
                    spawner.ImpactAoe,
                    spawner.ImpactProjectile);

                EventQueue.Enqueue(new ProjectileSpawnEvent
                {
                    Faction = parentIdentity.Faction,
                    BaseProjectileId = childProjectileId,
                    TypeId = spawner.TypeId,
                    HasChildSpawner = 0,
                    SeedContactGateTargetId = 0,
                    Position = parentKinematics.Position,
                    BaseDirection = baseDirection,
                    Speed = speed,
                    Count = 1,
                    SpreadDegrees = 0f,
                    JitterDegrees = 0f,
                    JitterSeed = 0u,
                    PierceRemaining = spawner.PierceCount,
                    RepeatHitCooldownSeconds = spawner.RepeatHitCooldownSeconds,
                    Lifetime = spawner.Lifetime,
                    Radius = spawner.Radius,
                    RotationRadians = spawner.RotationRadians,
                    HalfExtents = spawner.HalfExtents,
                    ShapeType = spawner.ShapeType,
                    HitPayload = hitPayload,
                    Tracking = new ProjectileTrackingComponent
                    {
                        TrackingEnabled = spawner.TrackingEnabled,
                        TrackingTurnSpeedRadians = spawner.TrackingTurnSpeedRadians,
                        TrackingQueryCooldownRemaining = spawner.TrackingInitialQueryDelaySeconds,
                        TrackingQueryIntervalSeconds = spawner.TrackingQueryIntervalSeconds,
                        TrackedTargetId = 0,
                        TrackedTargetIndex = -1,
                        TrackedTargetPosition = default,
                        TrackingRandomState = 0
                    },
                    Render = new CombatRenderComponent
                    {
                        IsRenderable = 1,
                        AlignToVelocity = 1,
                        VisualScale = new float2(spawner.VisualScale, spawner.VisualScale),
                        VisualRotationSin = spawner.VisualRotationSin,
                        VisualRotationCos = spawner.VisualRotationCos,
                        RenderZ = 0f // expansion overrides per-id
                    }
                });
            }

            private static float2 ComputeChildVelocity(
                CombatKinematicsComponent parentKinematics,
                in ProjectileChildSpawnerComponent spawner,
                int childIndex)
            {
                float2 forward = math.normalizesafe(parentKinematics.Velocity, new float2(1f, 0f));
                float2 dir;
                switch (spawner.SpawnPatternType)
                {
                    case ProjectileChildSpawnPatternType.SideSpray:
                    {
                        float2 left  = new float2(-forward.y,  forward.x);
                        float2 right = new float2( forward.y, -forward.x);
                        bool isLeft  = (childIndex & 1) == 0;
                        int leftCount  = (spawner.ChildCountPerTick + 1) / 2;
                        int rightCount =  spawner.ChildCountPerTick / 2;
                        int sideIndex  = childIndex / 2;
                        int sideCount  = isLeft ? leftCount : rightCount;
                        float2 sideDir = isLeft ? left : right;
                        float spreadRad = math.radians(spawner.SideSpreadDegrees);
                        float angle = SideSpreadAngle(spreadRad, sideIndex, sideCount);
                        dir = Rotate(sideDir, angle);
                        break;
                    }
                    default:
                        dir = forward;
                        break;
                }
                return spawner.Speed > 0f ? dir * spawner.Speed : dir * math.length(parentKinematics.Velocity);
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
                in ProjectileChildSpawnerComponent spawner,
                int tickIndex)
            {
                return spawner.IntervalSeconds
                    + DeterministicJitter(parentProjectileId, spawner.SpawnerId, tickIndex, spawner.IntervalJitterSeconds);
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
