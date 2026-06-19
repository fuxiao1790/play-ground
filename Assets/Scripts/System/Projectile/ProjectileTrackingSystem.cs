using PlayGround.System.Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSimulationSystem))]
    [UpdateAfter(typeof(TimedProjectileSpawnSystem))]
    [UpdateBefore(typeof(ProjectileMovementSystem))]
    public partial struct ProjectileTrackingSystem : ISystem
    {
        private const float TrackingSpatialHashCellSize = 64f;
        private const int ForwardAcquisitionLateralCellRadius = 1;
        private static readonly ProfilerMarker<int> TargetSpatialHashBuildMarker =
            new("ProjectileTrackingSystem.TargetSpatialHashBuild", "Targets");

        private EntityQuery targetQuery;

        public void OnCreate(ref SystemState state)
        {
            targetQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<TargetProxyTag>(),
                ComponentType.ReadOnly<TargetPosition>(),
                ComponentType.ReadOnly<TargetFaction>());
        }

        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.CompleteDependencyBeforeRO<TargetPosition>();
            state.EntityManager.CompleteDependencyBeforeRO<TargetFaction>();

            NativeArray<Entity> targetEntities = targetQuery.ToEntityArray(Allocator.TempJob);
            NativeArray<TargetPosition> targetPositions = targetQuery.ToComponentDataArray<TargetPosition>(Allocator.TempJob);
            NativeArray<TargetFaction> targetFactions = targetQuery.ToComponentDataArray<TargetFaction>(Allocator.TempJob);
            int totalTargetCount = targetEntities.Length;

            NativeParallelHashMap<long, int> targetIndicesById;
            NativeParallelMultiHashMap<long, int> targetCells;

            using (TargetSpatialHashBuildMarker.Auto(totalTargetCount))
            {
                targetIndicesById = new NativeParallelHashMap<long, int>(
                    math.max(1, totalTargetCount), Allocator.TempJob);
                targetCells = new NativeParallelMultiHashMap<long, int>(
                    math.max(1, totalTargetCount), Allocator.TempJob);

                for (int i = 0; i < targetEntities.Length; i++)
                {
                    Entity target = targetEntities[i];
                    TargetPosition position = targetPositions[i];
                    CombatFaction faction = targetFactions[i].Value;
                    targetIndicesById.TryAdd(TargetIdKey(faction, TargetKey(target)), i);
                    int2 cell = FloorCell(position.Value);
                    targetCells.Add(CellKey(faction, cell.x, cell.y), i);
                }
            }

            var acquisitionJob = new ProjectileTargetAcquisitionJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                TargetEntities = targetEntities,
                TargetPositions = targetPositions,
                TargetIndicesById = targetIndicesById,
                TargetCells = targetCells
            };

            JobHandle acquisitionHandle = acquisitionJob.ScheduleParallel(state.Dependency);

            var steeringJob = new ProjectileSteeringJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime
            };

            JobHandle steeringHandle = steeringJob.ScheduleParallel(acquisitionHandle);
            JobHandle disposeIdHandle = targetIndicesById.Dispose(acquisitionHandle);
            JobHandle disposeCellsHandle = targetCells.Dispose(acquisitionHandle);
            JobHandle disposeTargetsHandle = JobHandle.CombineDependencies(
                targetEntities.Dispose(acquisitionHandle),
                JobHandle.CombineDependencies(
                    targetPositions.Dispose(acquisitionHandle),
                    targetFactions.Dispose(acquisitionHandle)));
            state.Dependency = JobHandle.CombineDependencies(
                steeringHandle,
                JobHandle.CombineDependencies(
                    disposeTargetsHandle,
                    JobHandle.CombineDependencies(disposeIdHandle, disposeCellsHandle)));
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(Active))]
        private partial struct ProjectileTargetAcquisitionJob : IJobEntity
        {
            public float DeltaTime;
            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            [ReadOnly] public NativeParallelHashMap<long, int> TargetIndicesById;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> TargetCells;

            private void Execute(
                ref ProjectileTrackingComponent tracking,
                in ProjectileIdentityComponent identity,
                in CombatKinematicsComponent kinematics,
                in CombatLifetimeComponent lifetime)
            {
                if (!tracking.TrackingEnabled || identity.Faction == CombatFaction.None)
                {
                    return;
                }

                float speed = math.length(kinematics.Velocity);
                if (speed <= 0.0001f)
                {
                    return;
                }

                if (TryRefreshTrackedTarget(ref tracking, identity))
                {
                    tracking.TrackingQueryCooldownRemaining = math.max(0f, tracking.TrackingQueryCooldownRemaining - DeltaTime);
                    return;
                }

                if (TryAcquireTrackedTarget(ref tracking, identity, kinematics, speed, lifetime.Remaining))
                {
                    tracking.TrackingQueryCooldownRemaining = tracking.TrackingQueryIntervalSeconds;
                }
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
                    && TargetKey(TargetEntities[cachedIndex]) == tracking.TrackedTargetId)
                {
                    tracking.TrackedTargetPosition = TargetPositions[cachedIndex].Value;
                    return true;
                }

                if (TargetIndicesById.TryGetValue(
                        TargetIdKey(identity.Faction, tracking.TrackedTargetId),
                        out int mappedIndex)
                    && mappedIndex >= 0
                    && mappedIndex < TargetEntities.Length
                    && TargetKey(TargetEntities[mappedIndex]) == tracking.TrackedTargetId)
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
                float2 side = new(-forward.y, forward.x);

                for (float forwardDistance = 0f; forwardDistance <= reachableDistance; forwardDistance += TrackingSpatialHashCellSize)
                {
                    for (int lane = 0; lane <= ForwardAcquisitionLateralCellRadius * 2; lane++)
                    {
                        int lateralOffset = LaneToLateralOffset(lane);
                        float2 probePosition = kinematics.Position
                            + forward * forwardDistance
                            + side * lateralOffset * TrackingSpatialHashCellSize;
                        int2 cell = FloorCell(probePosition);
                        TrySelectRandomTargetInCell(
                                ref randomState,
                                ref validTargetCount,
                                ref selectedTargetIndex,
                                kinematics,
                                forward,
                                minimumDotSquared,
                                CellKey(identity.Faction, cell.x, cell.y));
                    }
                }

                tracking.TrackingRandomState = randomState;
                if (selectedTargetIndex < 0)
                {
                    return false;
                }

                Entity target = TargetEntities[selectedTargetIndex];
                tracking.TrackedTargetId = TargetKey(target);
                tracking.TrackedTargetIndex = selectedTargetIndex;
                tracking.TrackedTargetPosition = TargetPositions[selectedTargetIndex].Value;
                return true;
            }

            private bool TrySelectRandomTargetInCell(
                ref uint randomState,
                ref int validTargetCount,
                ref int selectedTargetIndex,
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

                    TargetPosition target = TargetPositions[targetIndex];
                    if (!IsValidForwardAcquisitionTarget(kinematics, target, forward, minimumDotSquared))
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

            private static bool IsValidForwardAcquisitionTarget(
                CombatKinematicsComponent kinematics,
                TargetPosition target,
                float2 forward,
                float minimumDotSquared)
            {
                float2 toTarget = target.Value - kinematics.Position;
                float distanceSquared = math.lengthsq(toTarget);
                if (distanceSquared <= ProjectileSimulationConstants.MinimumDirectionLengthSquared)
                {
                    return true;
                }

                float forwardDistance = math.dot(toTarget, forward);
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

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(Active))]
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

        private static int2 FloorCell(float2 pos)
        {
            return new int2(
                (int)math.floor(pos.x / TrackingSpatialHashCellSize),
                (int)math.floor(pos.y / TrackingSpatialHashCellSize));
        }

        private static long CellKey(CombatFaction faction, int x, int y)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ (byte)faction) * 1099511628211UL;
                hash = (hash ^ (uint)x) * 1099511628211UL;
                hash = (hash ^ (uint)y) * 1099511628211UL;
                return (long)hash;
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

        private static long TargetIdKey(CombatFaction faction, int targetId)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ (byte)faction) * 1099511628211UL;
                hash = (hash ^ (uint)targetId) * 1099511628211UL;
                return (long)hash;
            }
        }
    }
}
