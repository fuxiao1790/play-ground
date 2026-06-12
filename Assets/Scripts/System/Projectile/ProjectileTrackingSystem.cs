using PlayGround.System.Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSimulationSystem))]
    public partial struct ProjectileTrackingSystem : ISystem
    {
        private const float TrackingSpatialHashCellSize = 64f;

        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.CompleteDependencyBeforeRO<CombatTargetElement>();

            int totalTargetCount = 0;
            foreach (DynamicBuffer<CombatTargetElement> targets in
                SystemAPI.Query<DynamicBuffer<CombatTargetElement>>().WithAll<ProjectileScope>())
            {
                totalTargetCount += targets.Length;
            }

            var targetIndicesById = new NativeParallelHashMap<long, int>(
                math.max(1, totalTargetCount), Allocator.TempJob);
            var targetCells = new NativeParallelMultiHashMap<long, int>(
                math.max(1, totalTargetCount), Allocator.TempJob);

            foreach ((DynamicBuffer<CombatTargetElement> targets, Entity scope) in
                SystemAPI.Query<DynamicBuffer<CombatTargetElement>>()
                    .WithAll<ProjectileScope>()
                    .WithEntityAccess())
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    CombatTargetElement target = targets[i];
                    targetIndicesById.TryAdd(TargetIdKey(scope, target.TargetId), i);
                    int2 cell = FloorCell(target.Position);
                    targetCells.Add(CellKey(scope, cell.x, cell.y), i);
                }
            }

            var acquisitionJob = new ProjectileTargetAcquisitionJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                Targets = SystemAPI.GetBufferLookup<CombatTargetElement>(true),
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
            state.Dependency = JobHandle.CombineDependencies(
                steeringHandle,
                JobHandle.CombineDependencies(disposeIdHandle, disposeCellsHandle));
        }

        [BurstCompile]
        [WithAll(typeof(ProjectileTag), typeof(ProjectileActiveTag))]
        private partial struct ProjectileTargetAcquisitionJob : IJobEntity
        {
            public float DeltaTime;
            [ReadOnly] public BufferLookup<CombatTargetElement> Targets;
            [ReadOnly] public NativeParallelHashMap<long, int> TargetIndicesById;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> TargetCells;

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
                    bool hasTrackedTarget = TryRefreshTrackedTarget(ref tracking, identity, kinematics, hit, targets);
                    if (!hasTrackedTarget)
                    {
                        hasTrackedTarget = TryAcquireTrackedTarget(ref tracking, identity, kinematics, hit, targets);
                    }

                    tracking.TrackingQueryCooldownRemaining = tracking.TrackingQueryIntervalSeconds;
                    if (!hasTrackedTarget)
                    {
                        return;
                    }
                }
            }

            private bool TryRefreshTrackedTarget(
                ref ProjectileTrackingComponent tracking,
                ProjectileIdentityComponent identity,
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

                if (TargetIndicesById.TryGetValue(
                        TargetIdKey(identity.Scope, tracking.TrackedTargetId),
                        out int mappedIndex)
                    && mappedIndex >= 0
                    && mappedIndex < targets.Length
                    && targets[mappedIndex].TargetId == tracking.TrackedTargetId
                    && IsValidTrackedTarget(kinematics, tracking, hit, targets[mappedIndex]))
                {
                    tracking.TrackedTargetIndex = mappedIndex;
                    tracking.TrackedTargetPosition = targets[mappedIndex].Position;
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
                CombatHitComponent hit,
                DynamicBuffer<CombatTargetElement> targets)
            {
                tracking.TrackedTargetId = 0;
                tracking.TrackedTargetIndex = -1;
                float bestDistanceSquared = float.MaxValue;
                float range = math.sqrt(tracking.TrackingRangeSquared);
                int2 cellMin = FloorCell(kinematics.Position - range);
                int2 cellMax = FloorCell(kinematics.Position + range);

                for (int cy = cellMin.y; cy <= cellMax.y; cy++)
                {
                    for (int cx = cellMin.x; cx <= cellMax.x; cx++)
                    {
                        long key = CellKey(identity.Scope, cx, cy);
                        if (!TargetCells.TryGetFirstValue(
                                key,
                                out int targetIndex,
                                out NativeParallelMultiHashMapIterator<long> iterator))
                        {
                            continue;
                        }

                        do
                        {
                            if (targetIndex < 0 || targetIndex >= targets.Length)
                            {
                                continue;
                            }

                            CombatTargetElement target = targets[targetIndex];
                            if (!TryGetTargetDistanceSquared(
                                    kinematics,
                                    tracking,
                                    hit,
                                    target,
                                    out float distanceSquared))
                            {
                                continue;
                            }

                            if (distanceSquared <= ProjectileSimulationConstants.MinimumDirectionLengthSquared)
                            {
                                tracking.TrackedTargetId = target.TargetId;
                                tracking.TrackedTargetIndex = targetIndex;
                                tracking.TrackedTargetPosition = target.Position;
                                return true;
                            }

                            if (distanceSquared >= bestDistanceSquared)
                            {
                                continue;
                            }

                            bestDistanceSquared = distanceSquared;
                            tracking.TrackedTargetId = target.TargetId;
                            tracking.TrackedTargetIndex = targetIndex;
                            tracking.TrackedTargetPosition = target.Position;
                        }
                        while (TargetCells.TryGetNextValue(out targetIndex, ref iterator));
                    }
                }

                return tracking.TrackedTargetId != 0;
            }

            private static bool IsValidTrackedTarget(
                CombatKinematicsComponent kinematics,
                ProjectileTrackingComponent tracking,
                CombatHitComponent hit,
                CombatTargetElement target)
            {
                return TryGetTargetDistanceSquared(
                    kinematics,
                    tracking,
                    hit,
                    target,
                    out _);
            }

            private static bool TryGetTargetDistanceSquared(
                CombatKinematicsComponent kinematics,
                ProjectileTrackingComponent tracking,
                CombatHitComponent hit,
                CombatTargetElement target,
                out float distanceSquared)
            {
                distanceSquared = float.MaxValue;
                if ((hit.TargetMask & target.TargetMask) == 0)
                {
                    return false;
                }

                float2 toTarget = target.Position - kinematics.Position;
                distanceSquared = math.lengthsq(toTarget);
                if (distanceSquared > tracking.TrackingRangeSquared)
                {
                    return false;
                }

                return true;
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

        private static int2 FloorCell(float2 pos)
        {
            return new int2(
                (int)math.floor(pos.x / TrackingSpatialHashCellSize),
                (int)math.floor(pos.y / TrackingSpatialHashCellSize));
        }

        private static long CellKey(Entity scope, int x, int y)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ (uint)scope.Index) * 1099511628211UL;
                hash = (hash ^ (uint)scope.Version) * 1099511628211UL;
                hash = (hash ^ (uint)x) * 1099511628211UL;
                hash = (hash ^ (uint)y) * 1099511628211UL;
                return (long)hash;
            }
        }

        private static long TargetIdKey(Entity scope, int targetId)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                hash = (hash ^ (uint)scope.Index) * 1099511628211UL;
                hash = (hash ^ (uint)scope.Version) * 1099511628211UL;
                hash = (hash ^ (uint)targetId) * 1099511628211UL;
                return (long)hash;
            }
        }
    }
}
