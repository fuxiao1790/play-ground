using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSimulationSystem))]
    public partial struct ProjectileTrackingSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new ProjectileTrackingJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Targets = SystemAPI.GetBufferLookup<ProjectileTargetElement>(true)
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileActiveTag))]
        private partial struct ProjectileTrackingJob : IJobEntity
        {
            public float DeltaTime;
            [ReadOnly] public BufferLookup<ProjectileTargetElement> Targets;

            private void Execute(ref ProjectileComponent projectile)
            {
                if (!projectile.TrackingEnabled || projectile.Scope == Entity.Null || !Targets.HasBuffer(projectile.Scope))
                {
                    return;
                }

                DynamicBuffer<ProjectileTargetElement> targets = Targets[projectile.Scope];
                float speed = math.length(projectile.Velocity);
                if (speed <= 0.0001f)
                {
                    return;
                }

                float2 currentDirection = projectile.Velocity / speed;
                projectile.TrackingQueryCooldownRemaining = math.max(0f, projectile.TrackingQueryCooldownRemaining - DeltaTime);
                bool hasTrackedTarget = TryRefreshTrackedTarget(ref projectile, targets);
                if (projectile.TrackingQueryCooldownRemaining <= 0f)
                {
                    hasTrackedTarget = TryAcquireTrackedTarget(ref projectile, targets, currentDirection);
                    projectile.TrackingQueryCooldownRemaining = projectile.TrackingQueryIntervalSeconds;
                }
                else if (!hasTrackedTarget)
                {
                    return;
                }

                int trackedTargetIndex = projectile.TrackedTargetIndex;
                if (trackedTargetIndex < 0 || trackedTargetIndex >= targets.Length)
                {
                    return;
                }

                float2 toTarget = targets[trackedTargetIndex].Position - projectile.Position;
                if (math.lengthsq(toTarget) <= ProjectileSimulationConstants.MinimumDirectionLengthSquared)
                {
                    return;
                }

                float2 desiredDirection = math.normalize(toTarget);
                float maxTurnRadians = projectile.TrackingTurnSpeedRadians * DeltaTime;
                projectile.Velocity = SteerDirection(currentDirection, desiredDirection, maxTurnRadians) * speed;
            }

            private static bool TryRefreshTrackedTarget(ref ProjectileComponent projectile, DynamicBuffer<ProjectileTargetElement> targets)
            {
                if (projectile.TrackedTargetId == 0)
                {
                    projectile.TrackedTargetIndex = -1;
                    return false;
                }

                int cachedIndex = projectile.TrackedTargetIndex;
                if (cachedIndex >= 0
                    && cachedIndex < targets.Length
                    && targets[cachedIndex].TargetId == projectile.TrackedTargetId
                    && IsValidTrackedTarget(projectile, targets[cachedIndex]))
                {
                    return true;
                }

                for (int i = 0; i < targets.Length; i++)
                {
                    if (targets[i].TargetId != projectile.TrackedTargetId)
                    {
                        continue;
                    }

                    if (!IsValidTrackedTarget(projectile, targets[i]))
                    {
                        break;
                    }

                    projectile.TrackedTargetIndex = i;
                    return true;
                }

                projectile.TrackedTargetId = 0;
                projectile.TrackedTargetIndex = -1;
                return false;
            }

            private static bool TryAcquireTrackedTarget(
                ref ProjectileComponent projectile,
                DynamicBuffer<ProjectileTargetElement> targets,
                float2 currentDirection)
            {
                projectile.TrackedTargetId = 0;
                projectile.TrackedTargetIndex = -1;
                float bestDistanceSquared = float.MaxValue;

                for (int i = 0; i < targets.Length; i++)
                {
                    ProjectileTargetElement target = targets[i];
                    if ((projectile.TargetMask & target.TargetMask) == 0)
                    {
                        continue;
                    }

                    float2 toTarget = target.Position - projectile.Position;
                    float distanceSquared = math.lengthsq(toTarget);
                    if (distanceSquared > projectile.TrackingRangeSquared)
                    {
                        continue;
                    }

                    if (distanceSquared <= ProjectileSimulationConstants.MinimumDirectionLengthSquared)
                    {
                        projectile.TrackedTargetId = target.TargetId;
                        projectile.TrackedTargetIndex = i;
                        return true;
                    }

                    float forwardDot = math.dot(currentDirection, toTarget * math.rsqrt(distanceSquared));
                    if (forwardDot <= 0f || distanceSquared >= bestDistanceSquared)
                    {
                        continue;
                    }

                    bestDistanceSquared = distanceSquared;
                    projectile.TrackedTargetId = target.TargetId;
                    projectile.TrackedTargetIndex = i;
                }

                return projectile.TrackedTargetId != 0;
            }

            private static bool IsValidTrackedTarget(ProjectileComponent projectile, ProjectileTargetElement target)
            {
                if ((projectile.TargetMask & target.TargetMask) == 0)
                {
                    return false;
                }

                float distanceSquared = math.lengthsq(target.Position - projectile.Position);
                return distanceSquared <= projectile.TrackingRangeSquared;
            }

            private static float2 SteerDirection(float2 currentDirection, float2 desiredDirection, float maxTurnRadians)
            {
                if (maxTurnRadians <= 0f)
                {
                    return currentDirection;
                }

                float2 directionDelta = desiredDirection - currentDirection;
                float deltaLengthSquared = math.lengthsq(directionDelta);
                if (deltaLengthSquared <= ProjectileSimulationConstants.MinimumDirectionLengthSquared)
                {
                    return desiredDirection;
                }

                float deltaLength = math.sqrt(deltaLengthSquared);
                if (deltaLength > maxTurnRadians)
                {
                    directionDelta *= maxTurnRadians / deltaLength;
                }

                float2 steered = currentDirection + directionDelta;
                return math.lengthsq(steered) <= ProjectileSimulationConstants.MinimumDirectionLengthSquared
                    ? currentDirection
                    : math.normalize(steered);
            }
        }
    }
}
