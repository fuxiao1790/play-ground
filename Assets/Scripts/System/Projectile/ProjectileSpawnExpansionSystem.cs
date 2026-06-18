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
    // and writes fully-resolved ProjectileSpawnCommandData into PendingCommands for
    // ProjectileSpawnApplySystem to consume.
    // Built beside the old ProjectileMultiExpandSystem; inert until Task 004 wires producers.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TimedProjectileSpawnSystem))]
    [UpdateAfter(typeof(ProjectileCollisionSystem))]
    [UpdateAfter(typeof(PlayGround.System.Aoe.AoeCollisionSystem))]
    [UpdateBefore(typeof(ProjectileSpawnApplySystem))]
    public partial class ProjectileSpawnExpansionSystem : SystemBase
    {
        private EntityQuery _scopeQuery;

        internal NativeQueue<ProjectileSpawnEvent> EventQueue;
        internal NativeStream PendingCommands;
        internal JobHandle PendingHandle;

        protected override void OnCreate()
        {
            EventQueue = new NativeQueue<ProjectileSpawnEvent>(Allocator.Persistent);
            _scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<ProjectileSpawnEvent>());
        }

        protected override void OnDestroy()
        {
            if (PendingCommands.IsCreated)
                PendingCommands.Dispose();
            EventQueue.Dispose();
        }

        protected override void OnUpdate()
        {
            if (PendingCommands.IsCreated)
                PendingCommands.Dispose();

            Dependency.Complete();

            int queueCount = EventQueue.Count;

            using NativeArray<Entity> scopes = _scopeQuery.ToEntityArray(Allocator.Temp);
            int bufferCount = 0;
            for (int s = 0; s < scopes.Length; s++)
                bufferCount += EntityManager.GetBuffer<ProjectileSpawnEvent>(scopes[s]).Length;

            int totalEvents = queueCount + bufferCount;
            if (totalEvents == 0)
            {
                PendingCommands = default;
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

            PendingCommands = new NativeStream(totalEvents, Allocator.TempJob);

            Dependency = new ProjectileExpansionJob
            {
                Events = events,
                Stream = PendingCommands.AsWriter()
            }.Schedule(Dependency);

            Dependency = events.Dispose(Dependency);
            PendingHandle = Dependency;
        }

        [BurstCompile]
        private struct ProjectileExpansionJob : IJob
        {
            [ReadOnly] public NativeArray<ProjectileSpawnEvent> Events;
            public NativeStream.Writer Stream;

            public void Execute()
            {
                for (int ci = 0; ci < Events.Length; ci++)
                {
                    ProjectileSpawnEvent evt = Events[ci];
                    Stream.BeginForEachIndex(ci);

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

                    Stream.EndForEachIndex();
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

                Stream.Write(new ProjectileSpawnCommandData
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
                });
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
