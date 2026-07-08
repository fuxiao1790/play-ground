using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace PlayGround.System.Combat.Collision
{
    // ECS Lifecycle: singleton target spatial hash data; created by TargetSpatialHashSystem on create,
    // rebuilt every simulation update, and disposed by TargetSpatialHashSystem on destroy.
    public struct TargetSpatialHashSingleton : IComponentData
    {
        public NativeParallelMultiHashMap<long, int> ProjectileCollisionCells;
        public NativeParallelMultiHashMap<long, int> TrackingCells;
        public NativeParallelHashMap<long, int> TrackingIndicesById;
        public NativeParallelMultiHashMap<long, int> AoeOccupiedCells;
        public NativeReference<float> MaxTargetRadius;
        public NativeList<Entity> TargetEntities;
        public NativeList<TargetPosition> TargetPositions;
        public NativeList<TargetCollisionShape> TargetShapes;
        public NativeList<TargetFaction> TargetFactions;
        public int TargetCount;
        public JobHandle BuildHandle;
        public JobHandle ConsumerHandle;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ProjectileTrackingSystem))]
    [UpdateBefore(typeof(ProjectileCollisionSystem))]
    [UpdateBefore(typeof(LingeringAoeCollisionSystem))]
    [UpdateBefore(typeof(ImpactAoeCollisionSystem))]
    public partial struct TargetSpatialHashSystem : ISystem
    {
        private static readonly ProfilerMarker<int> GatherMarker =
            new("TargetSpatialHashSystem.Gather", "Targets");
        private static readonly ProfilerMarker<int> BuildMarker =
            new("TargetSpatialHashSystem.Build", "Targets");

        private Entity singletonEntity;
        private EntityQuery targetQuery;

        public void OnCreate(ref SystemState state)
        {
            targetQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<TargetProxyTag>(),
                ComponentType.ReadOnly<TargetPosition>(),
                ComponentType.ReadOnly<TargetCollisionShape>(),
                ComponentType.ReadOnly<TargetFaction>());

            TargetSpatialHashSingleton singleton = new()
            {
                ProjectileCollisionCells = new NativeParallelMultiHashMap<long, int>(1, Allocator.Persistent),
                TrackingCells = new NativeParallelMultiHashMap<long, int>(1, Allocator.Persistent),
                TrackingIndicesById = new NativeParallelHashMap<long, int>(1, Allocator.Persistent),
                AoeOccupiedCells = new NativeParallelMultiHashMap<long, int>(1, Allocator.Persistent),
                MaxTargetRadius = new NativeReference<float>(Allocator.Persistent),
                TargetEntities = new NativeList<Entity>(1, Allocator.Persistent),
                TargetPositions = new NativeList<TargetPosition>(1, Allocator.Persistent),
                TargetShapes = new NativeList<TargetCollisionShape>(1, Allocator.Persistent),
                TargetFactions = new NativeList<TargetFaction>(1, Allocator.Persistent)
            };

            singleton.MaxTargetRadius.Value = 0f;
            singletonEntity = state.EntityManager.CreateEntity(typeof(TargetSpatialHashSingleton));
            state.EntityManager.SetComponentData(singletonEntity, singleton);
        }

        public void OnUpdate(ref SystemState state)
        {
            TargetSpatialHashSingleton singleton =
                state.EntityManager.GetComponentData<TargetSpatialHashSingleton>(singletonEntity);

            // Consumers add read jobs to ConsumerHandle after depending on BuildHandle. Completing it
            // here makes the persistent maps safe to clear and rebuild for this frame.
            singleton.ConsumerHandle.Complete();
            singleton.BuildHandle.Complete();
            singleton.ConsumerHandle = default;
            singleton.BuildHandle = default;

            int targetCount = targetQuery.CalculateEntityCount();
            singleton.TargetCount = targetCount;

            if (targetCount == 0)
            {
                ClearContainers(ref singleton);
                singleton.TargetEntities.Clear();
                singleton.TargetPositions.Clear();
                singleton.TargetShapes.Clear();
                singleton.TargetFactions.Clear();
                singleton.MaxTargetRadius.Value = 0f;
                singleton.BuildHandle = state.Dependency;
                state.EntityManager.SetComponentData(singletonEntity, singleton);
                return;
            }

            using (GatherMarker.Auto(targetCount))
            {
                GatherTargets(ref state, ref singleton, targetCount);
            }

            int aoeCapacity = CalculateAoeCapacity(singleton.TargetShapes.AsArray());
            ClearContainers(ref singleton);
            EnsureCapacity(ref singleton.ProjectileCollisionCells, targetCount);
            EnsureCapacity(ref singleton.TrackingCells, targetCount);
            EnsureCapacity(ref singleton.TrackingIndicesById, targetCount);
            EnsureCapacity(ref singleton.AoeOccupiedCells, aoeCapacity);

            JobHandle dependency = state.Dependency;
            JobHandle projectileHandle;
            JobHandle trackingHandle;
            JobHandle aoeHandle;

            using (BuildMarker.Auto(targetCount))
            {
                projectileHandle = new BuildProjectileCollisionHashJob
                {
                    TargetPositions = singleton.TargetPositions.AsArray(),
                    TargetShapes = singleton.TargetShapes.AsArray(),
                    ProjectileCollisionCells = singleton.ProjectileCollisionCells,
                    MaxTargetRadius = singleton.MaxTargetRadius
                }.Schedule(dependency);

                trackingHandle = new BuildTrackingHashJob
                {
                    TargetEntities = singleton.TargetEntities.AsArray(),
                    TargetPositions = singleton.TargetPositions.AsArray(),
                    TrackingCells = singleton.TrackingCells,
                    TrackingIndicesById = singleton.TrackingIndicesById
                }.Schedule(dependency);

                aoeHandle = new BuildAoeOccupiedHashJob
                {
                    TargetShapes = singleton.TargetShapes.AsArray(),
                    AoeOccupiedCells = singleton.AoeOccupiedCells
                }.Schedule(dependency);
            }

            singleton.BuildHandle = JobHandle.CombineDependencies(projectileHandle, trackingHandle, aoeHandle);
            state.Dependency = singleton.BuildHandle;
            state.EntityManager.SetComponentData(singletonEntity, singleton);
        }

        public void OnDestroy(ref SystemState state)
        {
            TargetSpatialHashSingleton singleton = default;
            if (singletonEntity != Entity.Null
                && state.EntityManager.Exists(singletonEntity)
                && state.EntityManager.HasComponent<TargetSpatialHashSingleton>(singletonEntity))
            {
                singleton = state.EntityManager.GetComponentData<TargetSpatialHashSingleton>(singletonEntity);
            }

            singleton.BuildHandle.Complete();
            singleton.ConsumerHandle.Complete();
            DisposeIfCreated(ref singleton.ProjectileCollisionCells);
            DisposeIfCreated(ref singleton.TrackingCells);
            DisposeIfCreated(ref singleton.TrackingIndicesById);
            DisposeIfCreated(ref singleton.AoeOccupiedCells);
            DisposeIfCreated(ref singleton.MaxTargetRadius);
            DisposeIfCreated(ref singleton.TargetEntities);
            DisposeIfCreated(ref singleton.TargetPositions);
            DisposeIfCreated(ref singleton.TargetShapes);
            DisposeIfCreated(ref singleton.TargetFactions);
        }

        private void GatherTargets(ref SystemState state, ref TargetSpatialHashSingleton singleton, int targetCount)
        {
            state.EntityManager.CompleteDependencyBeforeRO<TargetPosition>();
            state.EntityManager.CompleteDependencyBeforeRO<TargetCollisionShape>();
            state.EntityManager.CompleteDependencyBeforeRO<TargetFaction>();

            ResizeSnapshotLists(ref singleton, targetCount);

            using NativeArray<Entity> entities = targetQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<TargetPosition> positions = targetQuery.ToComponentDataArray<TargetPosition>(Allocator.Temp);
            using NativeArray<TargetCollisionShape> shapes = targetQuery.ToComponentDataArray<TargetCollisionShape>(Allocator.Temp);
            using NativeArray<TargetFaction> factions = targetQuery.ToComponentDataArray<TargetFaction>(Allocator.Temp);

            for (int i = 0; i < targetCount; i++)
            {
                singleton.TargetEntities[i] = entities[i];
                singleton.TargetPositions[i] = positions[i];
                singleton.TargetShapes[i] = shapes[i];
                singleton.TargetFactions[i] = factions[i];
            }
        }

        private static void ResizeSnapshotLists(ref TargetSpatialHashSingleton singleton, int targetCount)
        {
            EnsureCapacity(ref singleton.TargetEntities, targetCount);
            EnsureCapacity(ref singleton.TargetPositions, targetCount);
            EnsureCapacity(ref singleton.TargetShapes, targetCount);
            EnsureCapacity(ref singleton.TargetFactions, targetCount);
            singleton.TargetEntities.ResizeUninitialized(targetCount);
            singleton.TargetPositions.ResizeUninitialized(targetCount);
            singleton.TargetShapes.ResizeUninitialized(targetCount);
            singleton.TargetFactions.ResizeUninitialized(targetCount);
        }

        private static int CalculateAoeCapacity(NativeArray<TargetCollisionShape> targetShapes)
        {
            int capacity = 0;
            for (int i = 0; i < targetShapes.Length; i++)
            {
                TargetCollisionShape target = targetShapes[i];
                int2 min = CombatSpatialHash.MinCell(target.BoundsMin, CombatSpatialHash.AoeCellSize);
                int2 max = CombatSpatialHash.MaxCell(target.BoundsMax, CombatSpatialHash.AoeCellSize);
                capacity += ((max.x - min.x) + 1) * ((max.y - min.y) + 1);
            }

            return math.max(1, capacity);
        }

        private static void ClearContainers(ref TargetSpatialHashSingleton singleton)
        {
            singleton.ProjectileCollisionCells.Clear();
            singleton.TrackingCells.Clear();
            singleton.TrackingIndicesById.Clear();
            singleton.AoeOccupiedCells.Clear();
        }

        private static void EnsureCapacity<T>(ref NativeList<T> list, int capacity)
            where T : unmanaged
        {
            int required = math.max(1, capacity);
            if (list.Capacity < required)
            {
                list.Capacity = required;
            }
        }

        private static void EnsureCapacity(ref NativeParallelMultiHashMap<long, int> map, int capacity)
        {
            int required = math.max(1, capacity);
            if (map.Capacity < required)
            {
                map.Capacity = required;
            }
        }

        private static void EnsureCapacity(ref NativeParallelHashMap<long, int> map, int capacity)
        {
            int required = math.max(1, capacity);
            if (map.Capacity < required)
            {
                map.Capacity = required;
            }
        }

        private static void DisposeIfCreated(ref NativeParallelMultiHashMap<long, int> map)
        {
            if (map.IsCreated)
            {
                map.Dispose();
            }
        }

        private static void DisposeIfCreated(ref NativeParallelHashMap<long, int> map)
        {
            if (map.IsCreated)
            {
                map.Dispose();
            }
        }

        private static void DisposeIfCreated(ref NativeReference<float> reference)
        {
            if (reference.IsCreated)
            {
                reference.Dispose();
            }
        }

        private static void DisposeIfCreated<T>(ref NativeList<T> list)
            where T : unmanaged
        {
            if (list.IsCreated)
            {
                list.Dispose();
            }
        }

        [BurstCompile]
        private struct BuildProjectileCollisionHashJob : IJob
        {
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            [ReadOnly] public NativeArray<TargetCollisionShape> TargetShapes;
            public NativeParallelMultiHashMap<long, int> ProjectileCollisionCells;
            public NativeReference<float> MaxTargetRadius;

            public void Execute()
            {
                float maxTargetRadius = 0f;
                for (int i = 0; i < TargetPositions.Length; i++)
                {
                    int2 cell = CombatSpatialHash.FloorCell(
                        TargetPositions[i].Value,
                        CombatSpatialHash.ProjectileCollisionCellSize);
                    ProjectileCollisionCells.Add(CombatSpatialHash.CellKey(cell.x, cell.y), i);

                    TargetCollisionShape target = TargetShapes[i];
                    float radius = CombatCollisionMath.BoundingRadius(
                        target.Radius,
                        target.HalfExtents,
                        target.ShapeType);
                    maxTargetRadius = math.max(maxTargetRadius, radius);
                }

                MaxTargetRadius.Value = maxTargetRadius;
            }
        }

        [BurstCompile]
        private struct BuildTrackingHashJob : IJob
        {
            [ReadOnly] public NativeArray<Entity> TargetEntities;
            [ReadOnly] public NativeArray<TargetPosition> TargetPositions;
            public NativeParallelMultiHashMap<long, int> TrackingCells;
            public NativeParallelHashMap<long, int> TrackingIndicesById;

            public void Execute()
            {
                for (int i = 0; i < TargetEntities.Length; i++)
                {
                    TrackingIndicesById.TryAdd(TargetIdKey(TargetKey(TargetEntities[i])), i);
                    int2 cell = CombatSpatialHash.FloorCell(
                        TargetPositions[i].Value,
                        CombatSpatialHash.TrackingCellSize);
                    TrackingCells.Add(CombatSpatialHash.CellKey(cell.x, cell.y), i);
                }
            }
        }

        [BurstCompile]
        private struct BuildAoeOccupiedHashJob : IJob
        {
            [ReadOnly] public NativeArray<TargetCollisionShape> TargetShapes;
            public NativeParallelMultiHashMap<long, int> AoeOccupiedCells;

            public void Execute()
            {
                for (int i = 0; i < TargetShapes.Length; i++)
                {
                    TargetCollisionShape target = TargetShapes[i];
                    int2 min = CombatSpatialHash.MinCell(target.BoundsMin, CombatSpatialHash.AoeCellSize);
                    int2 max = CombatSpatialHash.MaxCell(target.BoundsMax, CombatSpatialHash.AoeCellSize);
                    for (int y = min.y; y <= max.y; y++)
                    {
                        for (int x = min.x; x <= max.x; x++)
                        {
                            AoeOccupiedCells.Add(CombatSpatialHash.CellKey(x, y), i);
                        }
                    }
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
