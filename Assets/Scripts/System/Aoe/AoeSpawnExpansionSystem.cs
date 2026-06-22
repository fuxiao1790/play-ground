using PlayGround.System.Common;
using PlayGround.System.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Aoe
{
    // Drains AoeSpawnEvent from EventQueue (internal producers) and the scope
    // DynamicBuffer<AoeSpawnEvent> (managed submission), resolves world bounds,
    // and writes AoeSpawnCommand into PendingCommands for AoeSpawnApplySystem.
    // Also emits spawn-time VFX (Trigger=0) per AoE on the main thread.
    // Built beside the old AoeSpawnSystem; inert until Task 006 wires producers.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ImpactAoeCollisionSystem))]
    [UpdateAfter(typeof(PlayGround.System.Projectile.ProjectileCollisionSystem))]
    [UpdateAfter(typeof(HitApplyFinalizeSystem))]
    [UpdateBefore(typeof(AoeSpawnApplySystem))]
    [UpdateBefore(typeof(PlayGround.System.Projectile.BasicProjectileSpawnApplySystem))]
    [UpdateBefore(typeof(PlayGround.System.Projectile.ChildSpawnerProjectileSpawnApplySystem))]
    public partial class AoeSpawnExpansionSystem : SystemBase
    {
        private EntityQuery _scopeQuery;

        internal NativeQueue<AoeSpawnEvent> EventQueue;
        internal NativeStream PendingCommands;
        internal JobHandle PendingHandle;

        // Combined handle of every producer job that wrote EventQueue this frame
        // (ProjectileCollisionSystem, StackAccrualSystem). Producers run before this system in the order graph
        // but their write jobs are async; ECS does not track the queue, so this system must
        // complete them itself before reading the queue on the main thread. Reset each frame.
        internal JobHandle ProducerHandle;

        protected override void OnCreate()
        {
            EventQueue = new NativeQueue<AoeSpawnEvent>(Allocator.Persistent);
            _scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<AoeSpawnEvent>());
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

            // Producers write EventQueue via ParallelWriter; their handles are not part of
            // this system's component-derived Dependency. Complete them before any read.
            ProducerHandle.Complete();
            ProducerHandle = default;

            int queueCount = EventQueue.Count;

            using NativeArray<Entity> scopes = _scopeQuery.ToEntityArray(Allocator.Temp);
            int bufferCount = 0;
            for (int s = 0; s < scopes.Length; s++)
                bufferCount += EntityManager.GetBuffer<AoeSpawnEvent>(scopes[s]).Length;

            int totalEvents = queueCount + bufferCount;
            if (totalEvents == 0)
            {
                PendingCommands = default;
                return;
            }

            var events = new NativeArray<AoeSpawnEvent>(totalEvents, Allocator.TempJob);
            int offset = 0;

            while (EventQueue.TryDequeue(out AoeSpawnEvent evt))
                events[offset++] = evt;

            for (int s = 0; s < scopes.Length; s++)
            {
                DynamicBuffer<AoeSpawnEvent> buf = EntityManager.GetBuffer<AoeSpawnEvent>(scopes[s]);
                for (int i = 0; i < buf.Length; i++)
                    events[offset++] = buf[i];
                buf.Clear();
            }

            if (scopes.Length > 0)
            {
                DynamicBuffer<VfxSpawnRequestElement> vfxBuffer =
                    EntityManager.GetBuffer<VfxSpawnRequestElement>(scopes[0]);
                vfxBuffer.EnsureCapacity(vfxBuffer.Length + totalEvents);
                for (int i = 0; i < totalEvents; i++)
                {
                    AoeSpawnEvent e = events[i];
                    vfxBuffer.Add(new VfxSpawnRequestElement
                    {
                        Faction  = e.Faction,
                        TypeId   = e.TypeId,
                        Trigger  = 0,
                        Position = e.Position,
                        AreaSize = e.AreaSize
                    });
                }
            }

            PendingCommands = new NativeStream(totalEvents, Allocator.TempJob);

            Dependency = new AoeExpansionJob
            {
                Events = events,
                Stream = PendingCommands.AsWriter()
            }.Schedule(Dependency);

            Dependency = events.Dispose(Dependency);
            PendingHandle = Dependency;
        }

        [BurstCompile]
        private struct AoeExpansionJob : IJob
        {
            [ReadOnly] public NativeArray<AoeSpawnEvent> Events;
            public NativeStream.Writer Stream;

            public void Execute()
            {
                for (int ci = 0; ci < Events.Length; ci++)
                {
                    AoeSpawnEvent evt = Events[ci];
                    Stream.BeginForEachIndex(ci);

                    CombatCollisionMath.ComputeWorldBounds(
                        evt.Position, evt.Radius, evt.HalfExtents, evt.RotationRadians, evt.ShapeType,
                        out float2 boundsMin, out float2 boundsMax);

                    Stream.Write(new AoeSpawnCommand
                    {
                        Faction                  = evt.Faction,
                        AoeId                    = evt.AoeId,
                        TypeId                   = evt.TypeId,
                        Lifetime                 = evt.Lifetime,
                        RepeatHitCooldownSeconds = evt.RepeatHitCooldownSeconds,
                        HitPayload               = evt.HitPayload,
                        AreaSize                 = evt.AreaSize,
                        Radius                   = evt.Radius,
                        RotationRadians          = evt.RotationRadians,
                        Position                 = evt.Position,
                        HalfExtents              = evt.HalfExtents,
                        BoundsMin                = boundsMin,
                        BoundsMax                = boundsMax,
                        ShapeType                = evt.ShapeType,
                        Render                   = evt.Render,
                        ProjectileBurst          = evt.ProjectileBurst,
                        AoeSpawn                 = evt.AoeSpawn
                    });

                    Stream.EndForEachIndex();
                }
            }
        }
    }
}
