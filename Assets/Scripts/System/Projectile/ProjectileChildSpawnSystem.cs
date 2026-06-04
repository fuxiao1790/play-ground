using PlayGround.System.Common;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    // Timed child spawns enqueue projectile spawn requests through ECB.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileMovementSystem))]
    [UpdateBefore(typeof(ProjectileLifetimeSystem))]
    public partial struct ProjectileChildSpawnSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            state.Dependency = new ProjectileChildSpawnEntityJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Ecb = ecb
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(ProjectileActiveTag), typeof(ProjectileChildSpawnerTag))]
        private partial struct ProjectileChildSpawnEntityJob : IJobEntity
        {
            public float DeltaTime;
            public EntityCommandBuffer.ParallelWriter Ecb;

            private void Execute(
                [ChunkIndexInQuery] int chunkIndex,
                ref ProjectileChildSpawnStateComponent childSpawnState,
                in ProjectileIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in ProjectileLifetimeComponent lifetime,
                in CombatHitComponent hit,
                in ProjectileChildSpawnerComponent spawner)
            {
                if (lifetime.RemainingLifetime <= 0f || identity.Scope == Entity.Null)
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
                        EnqueueChildSpawn(chunkIndex, identity, kinematics, hit, in spawner, tickIndex, childIndex);
                    }
                    cooldown += spawner.IntervalSeconds;
                }

                childSpawnState.ChildSpawnCooldownRemaining = cooldown;
                childSpawnState.ChildSpawnTickIndex = tickIndex;
            }

            private void EnqueueChildSpawn(
                int chunkIndex,
                ProjectileIdentityComponent parentIdentity,
                CombatKinematicsComponent parentKinematics,
                CombatHitComponent parentHit,
                in ProjectileChildSpawnerComponent spawner,
                int tickIndex,
                int childIndex)
            {
                float2 velocity = ComputeChildVelocity(parentKinematics, in spawner, childIndex);
                int targetMask = spawner.TargetMask != 0 ? spawner.TargetMask : parentHit.TargetMask;
                ProjectileHitPayload hitPayload = new(
                    parentHit.SourceNodeId,
                    spawner.DamageAmount,
                    spawner.DirectDamageEnabled,
                    spawner.ImpactAoe);

                ProjectileCollisionMath.ComputeWorldBounds(
                    parentKinematics.Position,
                    spawner.Radius,
                    spawner.HalfExtents,
                    spawner.RotationRadians,
                    spawner.ShapeType,
                    out float2 boundsMin,
                    out float2 boundsMax);

                int childProjectileId = ChildProjectileId(parentIdentity.ProjectileId, spawner.SpawnerId, tickIndex, childIndex);
                Ecb.AppendToBuffer(chunkIndex, parentIdentity.Scope, new ProjectileSpawnRequestElement
                {
                    ProjectileId = childProjectileId,
                    TypeId = spawner.TypeId,
                    TargetMask = targetMask,
                    PierceRemaining = spawner.PierceCount,
                    HasChildSpawner = 0,
                    RepeatHitCooldownSeconds = spawner.RepeatHitCooldownSeconds,
                    Lifetime = spawner.Lifetime,
                    Radius = spawner.Radius,
                    RotationRadians = spawner.RotationRadians,
                    Position = parentKinematics.Position,
                    Velocity = velocity,
                    HalfExtents = spawner.HalfExtents,
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax,
                    ShapeType = spawner.ShapeType,
                    HitPayload = hitPayload,
                    Tracking = new ProjectileTrackingComponent
                    {
                        TrackingEnabled = spawner.TrackingEnabled,
                        TrackingRangeSquared = spawner.TrackingRangeSquared,
                        TrackingTurnSpeedRadians = spawner.TrackingTurnSpeedRadians,
                        TrackingQueryCooldownRemaining = spawner.TrackingInitialQueryDelaySeconds,
                        TrackingQueryIntervalSeconds = spawner.TrackingQueryIntervalSeconds,
                        TrackedTargetId = 0,
                        TrackedTargetIndex = -1
                    },
                    Render = new CombatRenderComponent
                    {
                        IsRenderable = 1,
                        AlignToVelocity = 1,
                        VisualScale = new float2(spawner.VisualScale, spawner.VisualScale),
                        VisualRotationSin = spawner.VisualRotationSin,
                        VisualRotationCos = spawner.VisualRotationCos,
                        RenderZ = -0.25f - (childProjectileId % 1_000_000) * 0.000001f
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
                        // Mirror of ProjectileSideSpraySpawnPattern:
                        // even childIndex → left side, odd → right side.
                        // Each side fans its shots across SideSpreadDegrees.
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
                    default: // Forward
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
        }
    }
}
