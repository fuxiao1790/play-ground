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

            private void Execute(
                ref ProjectileKinematicsComponent kinematics,
                ref ProjectileTrackingComponent tracking,
                in ProjectileIdentityComponent identity,
                in ProjectileHitComponent hit)
            {
                if (!tracking.TrackingEnabled || identity.Scope == Entity.Null || !Targets.HasBuffer(identity.Scope))
                {
                    return;
                }

                DynamicBuffer<ProjectileTargetElement> targets = Targets[identity.Scope];
                float speed = math.length(kinematics.Velocity);
                if (speed <= 0.0001f)
                {
                    return;
                }

                float2 currentDirection = kinematics.Velocity / speed;
                tracking.TrackingQueryCooldownRemaining = math.max(0f, tracking.TrackingQueryCooldownRemaining - DeltaTime);
                bool hasTrackedTarget = TryRefreshTrackedTarget(ref tracking, kinematics, hit, targets);
                if (tracking.TrackingQueryCooldownRemaining <= 0f)
                {
                    hasTrackedTarget = TryAcquireTrackedTarget(ref tracking, kinematics, hit, targets, currentDirection);
                    tracking.TrackingQueryCooldownRemaining = tracking.TrackingQueryIntervalSeconds;
                }
                else if (!hasTrackedTarget)
                {
                    return;
                }

                int trackedTargetIndex = tracking.TrackedTargetIndex;
                if (trackedTargetIndex < 0 || trackedTargetIndex >= targets.Length)
                {
                    return;
                }

                float2 toTarget = targets[trackedTargetIndex].Position - kinematics.Position;
                if (math.lengthsq(toTarget) <= ProjectileSimulationConstants.MinimumDirectionLengthSquared)
                {
                    return;
                }

                float2 desiredDirection = math.normalize(toTarget);
                float maxTurnRadians = tracking.TrackingTurnSpeedRadians * DeltaTime;
                kinematics.Velocity = SteerDirection(currentDirection, desiredDirection, maxTurnRadians) * speed;
            }

            private static bool TryRefreshTrackedTarget(
                ref ProjectileTrackingComponent tracking,
                ProjectileKinematicsComponent kinematics,
                ProjectileHitComponent hit,
                DynamicBuffer<ProjectileTargetElement> targets)
            {
                if (tracking.TrackedTargetId == 0)
                {
                    tracking.TrackedTargetIndex = -1;
                    return false;
                }

                int cachedIndex = tracking.TrackedTargetIndex;
                if (cachedIndex >= 0
                    && cachedIndex < targets.Length
                    && targets[cachedIndex].TargetId == tracking.TrackedTargetId
                    && IsValidTrackedTarget(kinematics, tracking, hit, targets[cachedIndex]))
                {
                    return true;
                }

                for (int i = 0; i < targets.Length; i++)
                {
                    if (targets[i].TargetId != tracking.TrackedTargetId)
                    {
                        continue;
                    }

                    if (!IsValidTrackedTarget(kinematics, tracking, hit, targets[i]))
                    {
                        break;
                    }

                    tracking.TrackedTargetIndex = i;
                    return true;
                }

                tracking.TrackedTargetId = 0;
                tracking.TrackedTargetIndex = -1;
                return false;
            }

            private static bool TryAcquireTrackedTarget(
                ref ProjectileTrackingComponent tracking,
                ProjectileKinematicsComponent kinematics,
                ProjectileHitComponent hit,
                DynamicBuffer<ProjectileTargetElement> targets,
                float2 currentDirection)
            {
                tracking.TrackedTargetId = 0;
                tracking.TrackedTargetIndex = -1;
                float bestDistanceSquared = float.MaxValue;

                for (int i = 0; i < targets.Length; i++)
                {
                    ProjectileTargetElement target = targets[i];
                    if ((hit.TargetMask & target.TargetMask) == 0)
                    {
                        continue;
                    }

                    float2 toTarget = target.Position - kinematics.Position;
                    float distanceSquared = math.lengthsq(toTarget);
                    if (distanceSquared > tracking.TrackingRangeSquared)
                    {
                        continue;
                    }

                    if (distanceSquared <= ProjectileSimulationConstants.MinimumDirectionLengthSquared)
                    {
                        tracking.TrackedTargetId = target.TargetId;
                        tracking.TrackedTargetIndex = i;
                        return true;
                    }

                    float forwardDot = math.dot(currentDirection, toTarget * math.rsqrt(distanceSquared));
                    if (forwardDot <= 0f || distanceSquared >= bestDistanceSquared)
                    {
                        continue;
                    }

                    bestDistanceSquared = distanceSquared;
                    tracking.TrackedTargetId = target.TargetId;
                    tracking.TrackedTargetIndex = i;
                }

                return tracking.TrackedTargetId != 0;
            }

            private static bool IsValidTrackedTarget(
                ProjectileKinematicsComponent kinematics,
                ProjectileTrackingComponent tracking,
                ProjectileHitComponent hit,
                ProjectileTargetElement target)
            {
                if ((hit.TargetMask & target.TargetMask) == 0)
                {
                    return false;
                }

                float distanceSquared = math.lengthsq(target.Position - kinematics.Position);
                return distanceSquared <= tracking.TrackingRangeSquared;
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
