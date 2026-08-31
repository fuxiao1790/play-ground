using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Broadphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Projectiles
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSimulationSystem))]
    [UpdateAfter(typeof(TimedSpawnSystem))]
    [UpdateBefore(typeof(ProjectileMovementSystem))]
    public partial struct ProjectileTrackingSystem : ISystem
    {
        private const int ForwardAcquisitionLateralCellRadius = 1;
        private const int MissedTargetSearchCooldownIndex = -2;

        public void OnUpdate(ref SystemState state)
        {
            TargetSpatialHashSingleton hash = SystemAPI.GetSingleton<TargetSpatialHashSingleton>();
            state.Dependency = JobHandle.CombineDependencies(state.Dependency, hash.BuildHandle);

            var trackingJob = new ProjectileTrackingJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                TargetEntities = hash.TargetEntities.AsArray(),
                TargetPositions = hash.TargetPositions.AsArray(),
                TargetFactions = hash.TargetFactions.AsArray(),
                TargetIndicesById = hash.TrackingIndicesById,
                TargetCells = hash.TrackingCells
            };

            JobHandle trackingHandle = trackingJob.ScheduleParallel(state.Dependency);
            RefRW<TargetSpatialHashSingleton> hashRw = SystemAPI.GetSingletonRW<TargetSpatialHashSingleton>();
            hashRw.ValueRW.ConsumerHandle = JobHandle.CombineDependencies(
                hashRw.ValueRW.ConsumerHandle,
                trackingHandle);

            state.Dependency = trackingHandle;
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(Active))]
        [WithDisabled(typeof(ArmingTag))]
        private partial struct ProjectileTrackingJob : IJobEntity
        {
            public float DeltaTime;
            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            [ReadOnly] public NativeArray<TargetFaction> TargetFactions;
            [ReadOnly] public NativeParallelHashMap<long, int> TargetIndicesById;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> TargetCells;

            private void Execute(
                ref ProjectileTrackingComponent tracking,
                ref CombatKinematicsComponent kinematics,
                in ProjectileIdentityComponent identity,
                in CombatLifetimeComponent lifetime)
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

                if (identity.Faction != CombatFaction.None && !ShouldSkipAfterMissedTargetSearch(ref tracking))
                {
                    if (TryRefreshTrackedTarget(ref tracking, identity))
                    {
                        tracking.TrackingQueryCooldownRemaining = math.max(0f, tracking.TrackingQueryCooldownRemaining - DeltaTime);
                    }
                    else if (TryAcquireTrackedTarget(ref tracking, identity, kinematics, speed, lifetime.Remaining))
                    {
                        tracking.TrackingQueryCooldownRemaining = tracking.TrackingQueryIntervalSeconds;
                    }
                }

                Steer(ref kinematics, tracking, speed);
            }

            private void Steer(ref CombatKinematicsComponent kinematics, in ProjectileTrackingComponent tracking, float speed)
            {
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

                float cross = currentDirection.x * desiredDirection.y - currentDirection.y * desiredDirection.x;
                float dot = math.clamp(math.dot(currentDirection, desiredDirection), -1f, 1f);
                float angle = math.atan2(cross, dot);
                float absoluteAngle = math.abs(angle);
                if (absoluteAngle <= maxTurnRadians)
                {
                    return desiredDirection;
                }

                float turnAngle = math.select(maxTurnRadians, -maxTurnRadians, angle < 0f);
                float sin = math.sin(turnAngle);
                float cos = math.cos(turnAngle);
                return new float2(
                    currentDirection.x * cos - currentDirection.y * sin,
                    currentDirection.x * sin + currentDirection.y * cos);
            }

            private bool ShouldSkipAfterMissedTargetSearch(ref ProjectileTrackingComponent tracking)
            {
                if (tracking.TrackedTargetId != 0
                    || tracking.TrackedTargetIndex != MissedTargetSearchCooldownIndex)
                {
                    return false;
                }

                tracking.TrackingQueryCooldownRemaining = math.max(
                    0f,
                    tracking.TrackingQueryCooldownRemaining - DeltaTime);
                if (tracking.TrackingQueryCooldownRemaining > 0f)
                {
                    return true;
                }

                tracking.TrackedTargetIndex = -1;
                return false;
            }

            private bool TryRefreshTrackedTarget(
                ref ProjectileTrackingComponent tracking,
                ProjectileIdentityComponent identity)
            {
                if (tracking.TrackedTargetId == 0)
                {
                    tracking.TrackedTargetIndex = -1;
                    return false;
                }

                int cachedIndex = tracking.TrackedTargetIndex;
                if (cachedIndex >= 0
                    && cachedIndex < TargetEntities.Length
                    && TargetKey(TargetEntities[cachedIndex]) == tracking.TrackedTargetId
                    && TargetFactions[cachedIndex].Value != identity.Faction)
                {
                    tracking.TrackedTargetPosition = TargetPositions[cachedIndex].Value;
                    return true;
                }

                if (TargetIndicesById.TryGetValue(
                        TargetIdKey(tracking.TrackedTargetId),
                        out int mappedIndex)
                    && mappedIndex >= 0
                    && mappedIndex < TargetEntities.Length
                    && TargetKey(TargetEntities[mappedIndex]) == tracking.TrackedTargetId
                    && TargetFactions[mappedIndex].Value != identity.Faction)
                {
                    tracking.TrackedTargetIndex = mappedIndex;
                    tracking.TrackedTargetPosition = TargetPositions[mappedIndex].Value;
                    return true;
                }

                tracking.TrackedTargetId = 0;
                tracking.TrackedTargetIndex = -1;
                tracking.TrackedTargetPosition = default;
                return false;
            }

            private bool TryAcquireTrackedTarget(
                ref ProjectileTrackingComponent tracking,
                ProjectileIdentityComponent identity,
                CombatKinematicsComponent kinematics,
                float speed,
                float remainingLifetime)
            {
                tracking.TrackedTargetId = 0;
                tracking.TrackedTargetIndex = -1;
                int selectedTargetIndex = -1;
                int validTargetCount = 0;
                uint randomState = tracking.TrackingRandomState != 0
                    ? tracking.TrackingRandomState
                    : SeedFor(identity.ProjectileId, kinematics.Position);
                float reachableDistance = remainingLifetime * speed;
                float reachableAngle = tracking.TrackingTurnSpeedRadians * remainingLifetime;
                float minimumDot = math.cos(math.min(reachableAngle, math.PI * 0.5f));
                float minimumDotSquared = minimumDot * minimumDot;
                float2 forward = kinematics.Velocity / speed;

                SearchAcquisitionCone(
                    ref randomState,
                    ref validTargetCount,
                    ref selectedTargetIndex,
                    identity.Faction,
                    kinematics,
                    forward,
                    reachableDistance,
                    minimumDotSquared,
                    stopAfterFirstDistanceBandWithTarget: false);

                if (selectedTargetIndex < 0)
                {
                    SearchAcquisitionCone(
                        ref randomState,
                        ref validTargetCount,
                        ref selectedTargetIndex,
                        identity.Faction,
                        kinematics,
                        -forward,
                        reachableDistance,
                        minimumDotSquared,
                        stopAfterFirstDistanceBandWithTarget: true);
                }

                tracking.TrackingRandomState = randomState;
                if (selectedTargetIndex < 0)
                {
                    tracking.TrackedTargetIndex = MissedTargetSearchCooldownIndex;
                    tracking.TrackingQueryCooldownRemaining = tracking.TrackingQueryIntervalSeconds;
                    return false;
                }

                Entity target = TargetEntities[selectedTargetIndex];
                tracking.TrackedTargetId = TargetKey(target);
                tracking.TrackedTargetIndex = selectedTargetIndex;
                tracking.TrackedTargetPosition = TargetPositions[selectedTargetIndex].Value;
                return true;
            }

            private void SearchAcquisitionCone(
                ref uint randomState,
                ref int validTargetCount,
                ref int selectedTargetIndex,
                CombatFaction projectileFaction,
                CombatKinematicsComponent kinematics,
                float2 searchDirection,
                float reachableDistance,
                float minimumDotSquared,
                bool stopAfterFirstDistanceBandWithTarget)
            {
                float2 side = new(-searchDirection.y, searchDirection.x);

                for (float forwardDistance = 0f; forwardDistance <= reachableDistance; forwardDistance += CombatSpatialHash.TrackingCellSize)
                {
                    int validTargetCountBeforeDistance = validTargetCount;
                    for (int lane = 0; lane <= ForwardAcquisitionLateralCellRadius * 2; lane++)
                    {
                        int lateralOffset = LaneToLateralOffset(lane);
                        float2 probePosition = kinematics.Position
                            + searchDirection * forwardDistance
                            + side * lateralOffset * CombatSpatialHash.TrackingCellSize;
                        int2 cell = CombatSpatialHash.FloorCell(
                            probePosition,
                            CombatSpatialHash.TrackingCellSize);
                        TrySelectRandomTargetInCell(
                            ref randomState,
                            ref validTargetCount,
                            ref selectedTargetIndex,
                            projectileFaction,
                            kinematics,
                            searchDirection,
                            minimumDotSquared,
                            CombatSpatialHash.CellKey(cell.x, cell.y));
                    }

                    if (stopAfterFirstDistanceBandWithTarget
                        && validTargetCount > validTargetCountBeforeDistance)
                    {
                        return;
                    }
                }
            }

            private bool TrySelectRandomTargetInCell(
                ref uint randomState,
                ref int validTargetCount,
                ref int selectedTargetIndex,
                CombatFaction projectileFaction,
                CombatKinematicsComponent kinematics,
                float2 forward,
                float minimumDotSquared,
                long cellKey)
            {
                if (!TargetCells.TryGetFirstValue(
                        cellKey,
                        out int targetIndex,
                        out NativeParallelMultiHashMapIterator<long> iterator))
                {
                    return false;
                }

                do
                {
                    if (targetIndex < 0 || targetIndex >= TargetPositions.Length)
                    {
                        continue;
                    }

                    if (TargetFactions[targetIndex].Value == projectileFaction)
                    {
                        continue;
                    }

                    TargetPosition target = TargetPositions[targetIndex];
                    if (!IsValidAcquisitionTarget(kinematics, target, forward, minimumDotSquared))
                    {
                        continue;
                    }

                    validTargetCount++;
                    randomState = NextRandomState(randomState);
                    if (randomState % (uint)validTargetCount == 0u)
                    {
                        selectedTargetIndex = targetIndex;
                    }
                }
                while (TargetCells.TryGetNextValue(out targetIndex, ref iterator));

                return selectedTargetIndex >= 0;
            }

            private static int LaneToLateralOffset(int lane)
            {
                if (lane == 0)
                {
                    return 0;
                }

                return (lane & 1) == 1 ? (lane + 1) / 2 : -(lane / 2);
            }

            private static bool IsValidAcquisitionTarget(
                CombatKinematicsComponent kinematics,
                TargetPosition target,
                float2 searchDirection,
                float minimumDotSquared)
            {
                float2 toTarget = target.Value - kinematics.Position;
                float distanceSquared = math.lengthsq(toTarget);
                if (distanceSquared <= ProjectileSimulationConstants.MinimumDirectionLengthSquared)
                {
                    return true;
                }

                float forwardDistance = math.dot(toTarget, searchDirection);
                if (forwardDistance <= 0f)
                {
                    return false;
                }

                if (forwardDistance * forwardDistance < distanceSquared * minimumDotSquared)
                {
                    return false;
                }

                return true;
            }

            private static uint SeedFor(int projectileId, float2 position)
            {
                unchecked
                {
                    uint hash = (uint)projectileId * 0x9E3779B9u;
                    hash ^= math.asuint(position.x) + 0x85EBCA6Bu + (hash << 6) + (hash >> 2);
                    hash ^= math.asuint(position.y) + 0xC2B2AE35u + (hash << 6) + (hash >> 2);
                    hash ^= hash >> 16;
                    hash *= 0x7FEB352Du;
                    hash ^= hash >> 15;
                    hash *= 0x846CA68Bu;
                    hash ^= hash >> 16;
                    return hash == 0u ? 1u : hash;
                }
            }

            private static uint NextRandomState(uint state)
            {
                unchecked
                {
                    state ^= state << 13;
                    state ^= state >> 17;
                    state ^= state << 5;
                    return state == 0u ? 1u : state;
                }
            }
        }

        private static int TargetKey(Entity entity)
        {
            unchecked
            {
                int key = ((entity.Index + 1) * 397) ^ entity.Version;
                key &= 0x7fffffff;
                return key == 0 ? 1 : key;
            }
        }

        private static long TargetIdKey(int targetId)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ (uint)targetId) * 1099511628211UL;
                return (long)hash;
            }
        }
    }
}
