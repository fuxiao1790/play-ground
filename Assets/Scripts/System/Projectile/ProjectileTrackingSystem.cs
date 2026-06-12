using PlayGround.System.Common;
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
            var acquisitionJob = new ProjectileTargetAcquisitionJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Targets = SystemAPI.GetBufferLookup<CombatTargetElement>(true)
            };

            state.Dependency = acquisitionJob.ScheduleParallel(state.Dependency);

            var steeringJob = new ProjectileSteeringJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            };

            state.Dependency = steeringJob.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(ProjectileActiveTag))]
        private partial struct ProjectileTargetAcquisitionJob : IJobEntity
        {
            public float DeltaTime;
            [ReadOnly] public BufferLookup<CombatTargetElement> Targets;

            private void Execute(
                ref ProjectileTrackingComponent tracking,
                in ProjectileIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatHitComponent hit)
            {
                if (!tracking.TrackingEnabled || identity.Scope == Entity.Null || !Targets.HasBuffer(identity.Scope))
                {
                    return;
                }

                DynamicBuffer<CombatTargetElement> targets = Targets[identity.Scope];
                float speed = math.length(kinematics.Velocity);
                if (speed <= 0.0001f)
                {
                    return;
                }

                tracking.TrackingQueryCooldownRemaining = math.max(0f, tracking.TrackingQueryCooldownRemaining - DeltaTime);
                if (tracking.TrackingQueryCooldownRemaining <= 0f)
                {
                    bool hasTrackedTarget = TryRefreshTrackedTarget(ref tracking, kinematics, hit, targets);
                    if (!hasTrackedTarget)
                    {
                        hasTrackedTarget = TryAcquireTrackedTarget(ref tracking, kinematics, hit, targets);
                    }

                    tracking.TrackingQueryCooldownRemaining = tracking.TrackingQueryIntervalSeconds;
                    if (!hasTrackedTarget)
                    {
                        return;
                    }
                }
            }

            private static bool TryRefreshTrackedTarget(
                ref ProjectileTrackingComponent tracking,
                CombatKinematicsComponent kinematics,
                CombatHitComponent hit,
                DynamicBuffer<CombatTargetElement> targets)
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
                    tracking.TrackedTargetPosition = targets[cachedIndex].Position;
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
                    tracking.TrackedTargetPosition = targets[i].Position;
                    return true;
                }

                tracking.TrackedTargetId = 0;
                tracking.TrackedTargetIndex = -1;
                tracking.TrackedTargetPosition = default;
                return false;
            }

            private static bool TryAcquireTrackedTarget(
                ref ProjectileTrackingComponent tracking,
                CombatKinematicsComponent kinematics,
                CombatHitComponent hit,
                DynamicBuffer<CombatTargetElement> targets)
            {
                tracking.TrackedTargetId = 0;
                tracking.TrackedTargetIndex = -1;
                float bestDistanceSquared = float.MaxValue;

                for (int i = 0; i < targets.Length; i++)
                {
                    CombatTargetElement target = targets[i];
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
                        tracking.TrackedTargetPosition = target.Position;
                        return true;
                    }

                    if (distanceSquared >= bestDistanceSquared)
                    {
                        continue;
                    }

                    bestDistanceSquared = distanceSquared;
                    tracking.TrackedTargetId = target.TargetId;
                    tracking.TrackedTargetIndex = i;
                    tracking.TrackedTargetPosition = target.Position;
                }

                return tracking.TrackedTargetId != 0;
            }

            private static bool IsValidTrackedTarget(
                CombatKinematicsComponent kinematics,
                ProjectileTrackingComponent tracking,
                CombatHitComponent hit,
                CombatTargetElement target)
            {
                if ((hit.TargetMask & target.TargetMask) == 0)
                {
                    return false;
                }

                float distanceSquared = math.lengthsq(target.Position - kinematics.Position);
                return distanceSquared <= tracking.TrackingRangeSquared;
            }
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(ProjectileActiveTag))]
        private partial struct ProjectileSteeringJob : IJobEntity
        {
            public float DeltaTime;

            private void Execute(
                ref CombatKinematicsComponent kinematics,
                in ProjectileTrackingComponent tracking)
            {
                if (!tracking.TrackingEnabled)
                {
                    return;
                }

                float speed = math.length(kinematics.Velocity);
                if (speed <= 0.0001f)
                {
                    return;
                }

                if (tracking.TrackedTargetId == 0)
                {
                    return;
                }

                float2 toTarget = tracking.TrackedTargetPosition - kinematics.Position;
                if (math.lengthsq(toTarget) <= ProjectileSimulationConstants.MinimumDirectionLengthSquared)
                {
                    return;
                }

                float2 currentDirection = kinematics.Velocity / speed;
                float2 desiredDirection = math.normalize(toTarget);
                float maxTurnRadians = tracking.TrackingTurnSpeedRadians * DeltaTime;
                kinematics.Velocity = SteerDirection(currentDirection, desiredDirection, maxTurnRadians) * speed;
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
