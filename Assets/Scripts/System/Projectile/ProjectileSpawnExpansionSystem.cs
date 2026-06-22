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
    [UpdateAfter(typeof(TimedProjectileSpawnSystem))]
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
        // (TimedProjectileSpawnSystem, ProjectileCollisionSystem, LingeringAoeCollisionSystem, ImpactAoeCollisionSystem).
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

            while (EventQueue.TryDequeue(out ProjectileSpawnEvent evt))
                events[offset++] = evt;

            for (int s = 0; s < scopes.Length; s++)
            {
                DynamicBuffer<ProjectileSpawnEvent> buf = EntityManager.GetBuffer<ProjectileSpawnEvent>(scopes[s]);
                for (int i = 0; i < buf.Length; i++)
                    events[offset++] = buf[i];
                buf.Clear();
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

                    if (evt.Count <= 1)
                    {
                        WriteCommand(in evt, evt.BaseProjectileId, evt.BaseDirection * evt.Speed);
                    }
                    else
                    {
                        var rng = new Random(evt.JitterSeed != 0 ? evt.JitterSeed : 1u);
                        for (int i = 0; i < evt.Count; i++)
                        {
                            float angle = SpreadAngle(evt.SpreadDegrees, i, evt.Count);
                            if (evt.JitterDegrees > 0f)
                                angle += rng.NextFloat(-evt.JitterDegrees, evt.JitterDegrees);

                            int id = evt.BaseProjectileId + i;
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
                    HasChildSpawner = evt.HasChildSpawner,
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
                    ChildSpawner = evt.ChildSpawner,
                    ChildSpawnState = evt.ChildSpawnState
                };

                if (evt.HasChildSpawner != 0)
                    ChildSpawnerCommands.Enqueue(command);
                else
                    BasicCommands.Enqueue(command);
            }

            private static float SpreadAngle(float spread, int i, int count) =>
                count <= 1 ? 0f : -spread * 0.5f + spread / (count - 1) * i;

            private static float2 Rotate(float2 v, float degrees)
            {
                math.sincos(math.radians(degrees), out float s, out float c);
                return new float2(c * v.x - s * v.y, s * v.x + c * v.y);
            }
        }
    }
}
