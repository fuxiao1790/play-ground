using PlayGround.System.Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    // Drains ProjectileSpawnEvent from EventQueue (internal producers) and the scope
    // DynamicBuffer<ProjectileSpawnEvent> (managed submission), fans them out by Count/Spread/Jitter,
    // and writes fully-resolved ProjectileSpawnCommand into per-shape command
    // containers for the matching apply systems to consume.
    // Built beside the old ProjectileMultiExpandSystem; inert until Task 004 wires producers.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TimedSpawnSystem))]
    [UpdateAfter(typeof(ProjectileCollisionSystem))]
    [UpdateAfter(typeof(PlayGround.System.Aoe.ImpactAoeCollisionSystem))]
    [UpdateAfter(typeof(StatusProcessSystem))]
    [UpdateBefore(typeof(BasicProjectileSpawnApplySystem))]
    [UpdateBefore(typeof(ChildSpawnerProjectileSpawnApplySystem))]
    [UpdateBefore(typeof(PlayGround.System.Aoe.AoeSpawnApplySystem))]
    public partial class ProjectileSpawnExpansionSystem : SystemBase
    {
        private EntityQuery _scopeQuery;

        internal NativeQueue<ProjectileSpawnEvent> EventQueue;
        internal NativeQueue<ProjectileSpawnCommand> BasicProjectileCommandContainer;
        internal NativeQueue<ProjectileSpawnCommand> ChildSpawnerProjectileCommandContainer;
        internal JobHandle PendingHandle;

        // Combined handle of every producer job that wrote EventQueue this frame
        // (TimedSpawnSystem, ProjectileCollisionSystem, LingeringAoeCollisionSystem, ImpactAoeCollisionSystem).
        // Producers run before this system in the order graph but their write jobs are
        // async; ECS does not track the queue, so this system must complete them itself
        // before reading the queue on the main thread. Reset to default each frame.
        internal JobHandle ProducerHandle;

        protected override void OnCreate()
        {
            EventQueue = new NativeQueue<ProjectileSpawnEvent>(Allocator.Persistent);
            BasicProjectileCommandContainer = new NativeQueue<ProjectileSpawnCommand>(Allocator.Persistent);
            ChildSpawnerProjectileCommandContainer = new NativeQueue<ProjectileSpawnCommand>(Allocator.Persistent);
            _scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<ProjectileSpawnEvent>());
        }

        protected override void OnDestroy()
        {
            PendingHandle.Complete();
            if (BasicProjectileCommandContainer.IsCreated)
                BasicProjectileCommandContainer.Dispose();
            if (ChildSpawnerProjectileCommandContainer.IsCreated)
                ChildSpawnerProjectileCommandContainer.Dispose();
            EventQueue.Dispose();
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();

            // Producers write EventQueue via ParallelWriter; their handles are not part of
            // this system's component-derived Dependency. Complete them before any read.
            ProducerHandle.Complete();
            ProducerHandle = default;

            int queueCount = EventQueue.Count;

            using NativeArray<Entity> scopes = _scopeQuery.ToEntityArray(Allocator.Temp);
            int bufferCount = 0;
            for (int s = 0; s < scopes.Length; s++)
                bufferCount += EntityManager.GetBuffer<ProjectileSpawnEvent>(scopes[s]).Length;

            int totalEvents = queueCount + bufferCount;
            if (totalEvents == 0)
            {
                PendingHandle = default;
                return;
            }

            var events = new NativeArray<ProjectileSpawnEvent>(totalEvents, Allocator.TempJob);
            int offset = 0;

            // Bulk copy queued events in one memcpy instead of per-element TryDequeue: the
            // managed NativeQueue.TryDequeue path crosses the safety boundary and copies the
            // (large) event struct individually per element, which dominated this system's
            // main-thread cost. Producers are already completed above, so Count is stable.
            if (queueCount > 0)
            {
                NativeArray<ProjectileSpawnEvent> queued = EventQueue.ToArray(Allocator.Temp);
                NativeArray<ProjectileSpawnEvent>.Copy(queued, 0, events, offset, queued.Length);
                offset += queued.Length;
                queued.Dispose();
                EventQueue.Clear();
            }

            for (int s = 0; s < scopes.Length; s++)
            {
                DynamicBuffer<ProjectileSpawnEvent> buf = EntityManager.GetBuffer<ProjectileSpawnEvent>(scopes[s]);
                if (buf.Length > 0)
                {
                    NativeArray<ProjectileSpawnEvent>.Copy(buf.AsNativeArray(), 0, events, offset, buf.Length);
                    offset += buf.Length;
                    buf.Clear();
                }
            }

            Dependency = new ProjectileExpansionJob
            {
                Events = events,
                BasicCommands = BasicProjectileCommandContainer.AsParallelWriter(),
                ChildSpawnerCommands = ChildSpawnerProjectileCommandContainer.AsParallelWriter()
            }.Schedule(Dependency);

            Dependency = events.Dispose(Dependency);
            PendingHandle = Dependency;
        }

        [BurstCompile]
        private struct ProjectileExpansionJob : IJob
        {
            [ReadOnly] public NativeArray<ProjectileSpawnEvent> Events;
            public NativeQueue<ProjectileSpawnCommand>.ParallelWriter BasicCommands;
            public NativeQueue<ProjectileSpawnCommand>.ParallelWriter ChildSpawnerCommands;

            public void Execute()
            {
                for (int ci = 0; ci < Events.Length; ci++)
                {
                    ProjectileSpawnEvent evt = Events[ci];
                    int count = math.max(1, evt.Count);

                    if (evt.DeterministicIdTickIndex > 0
                        && evt.SpawnPatternType == ProjectileChildSpawnPatternType.SideSpray)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            int id = ProjectileIdFor(in evt, i);
                            float2 velocity = SideSprayVelocity(in evt, i, count);
                            WriteCommand(in evt, id, velocity);
                        }
                    }
                    else if (evt.DeterministicIdTickIndex > 0
                        && evt.SpawnPatternType == ProjectileChildSpawnPatternType.Radial)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            int id = ProjectileIdFor(in evt, i);
                            float2 velocity = RadialDirection(i, count) * evt.Speed;
                            WriteCommand(in evt, id, velocity);
                        }
                    }
                    else if (count <= 1)
                    {
                        WriteCommand(in evt, ProjectileIdFor(in evt, 0), evt.BaseDirection * evt.Speed);
                    }
                    else
                    {
                        var rng = new Random(evt.JitterSeed != 0 ? evt.JitterSeed : 1u);
                        for (int i = 0; i < count; i++)
                        {
                            float angle = SpreadAngle(evt.SpreadDegrees, i, count);
                            if (evt.JitterDegrees > 0f)
                                angle += rng.NextFloat(-evt.JitterDegrees, evt.JitterDegrees);

                            int id = ProjectileIdFor(in evt, i);
                            float2 velocity = Rotate(evt.BaseDirection, angle) * evt.Speed;
                            WriteCommand(in evt, id, velocity);
                        }
                    }
                }
            }

            private void WriteCommand(in ProjectileSpawnEvent evt, int projectileId, float2 velocity)
            {
                ProjectileCollisionMath.ComputeWorldBounds(
                    evt.Position, evt.Radius, evt.HalfExtents, evt.RotationRadians, evt.ShapeType,
                    out float2 boundsMin, out float2 boundsMax);

                CombatRenderComponent render = evt.Render;
                render.RenderZ = CombatRoot.ProjectileRenderZ
                    - (projectileId % CombatRoot.ProjectileRenderZSlots) * CombatRoot.ProjectileRenderZStep;

                var command = new ProjectileSpawnCommand
                {
                    Faction = evt.Faction,
                    ProjectileId = projectileId,
                    TypeId = evt.TypeId,
                    HasTimedSpawner = evt.HasTimedSpawner,
                    PierceRemaining = evt.PierceRemaining,
                    RepeatHitCooldownSeconds = evt.RepeatHitCooldownSeconds,
                    SeedContactGateTargetId = evt.SeedContactGateTargetId,
                    Lifetime = evt.Lifetime,
                    Radius = evt.Radius,
                    RotationRadians = evt.RotationRadians,
                    Position = evt.Position,
                    Velocity = velocity,
                    HalfExtents = evt.HalfExtents,
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax,
                    ShapeType = evt.ShapeType,
                    HitPayload = evt.HitPayload,
                    Tracking = evt.Tracking,
                    Render = render,
                    TimedSpawn = evt.TimedSpawn
                };

                if (evt.HasTimedSpawner != 0)
                    ChildSpawnerCommands.Enqueue(command);
                else
                    BasicCommands.Enqueue(command);
            }

            private static float SpreadAngle(float spread, int i, int count) =>
                count <= 1 ? 0f : -spread * 0.5f + spread / (count - 1) * i;

            private static float2 SideSprayVelocity(in ProjectileSpawnEvent evt, int shotIndex, int shotCount)
            {
                float2 forward = math.normalizesafe(evt.BaseDirection, new float2(1f, 0f));
                float2 left = new float2(-forward.y, forward.x);
                float2 right = new float2(forward.y, -forward.x);
                bool isLeft = (shotIndex & 1) == 0;
                int leftCount = (shotCount + 1) / 2;
                int rightCount = shotCount / 2;
                int sideIndex = shotIndex / 2;
                int sideCount = isLeft ? leftCount : rightCount;
                float2 sideDirection = isLeft ? left : right;
                float angle = SpreadAngle(evt.SpreadDegrees, sideIndex, sideCount);
                return Rotate(sideDirection, angle) * evt.Speed;
            }

            private static float2 RadialDirection(int shotIndex, int shotCount)
            {
                if (shotCount <= 1)
                {
                    return new float2(1f, 0f);
                }

                float radians = math.PI * 2f * shotIndex / shotCount;
                math.sincos(radians, out float s, out float c);
                return new float2(c, s);
            }

            private static int ProjectileIdFor(in ProjectileSpawnEvent evt, int shotIndex)
            {
                if (evt.DeterministicIdTickIndex <= 0)
                {
                    return evt.BaseProjectileId + shotIndex;
                }

                unchecked
                {
                    int hash = evt.BaseProjectileId;
                    hash = (hash * 397) ^ (int)evt.JitterSeed;
                    hash = (hash * 397) ^ evt.DeterministicIdTickIndex;
                    hash = (hash * 397) ^ shotIndex;
                    hash &= int.MaxValue;
                    return hash == 0 ? 1 : hash;
                }
            }

            private static float2 Rotate(float2 v, float degrees)
            {
                math.sincos(math.radians(degrees), out float s, out float c);
                return new float2(c * v.x - s * v.y, s * v.x + c * v.y);
            }
        }
    }
}
