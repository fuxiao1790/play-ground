using PlayGround.System.Common;
using PlayGround.System.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Aoe
{
    // Drains thin AoeSpawnEvent values, dereferences command-shaped templates,
    // stamps per-instance frame data, and writes AoeSpawnCommand into PendingCommands.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ImpactAoeCollisionSystem))]
    [UpdateAfter(typeof(PlayGround.System.Projectile.ProjectileCollisionSystem))]
    [UpdateAfter(typeof(StatusProcessSystem))]
    [UpdateBefore(typeof(AoeSpawnApplySystem))]
    [UpdateBefore(typeof(PlayGround.System.Projectile.BasicProjectileSpawnApplySystem))]
    [UpdateBefore(typeof(PlayGround.System.Projectile.ChildSpawnerProjectileSpawnApplySystem))]
    public partial class AoeSpawnExpansionSystem : SystemBase
    {
        private EntityQuery _scopeQuery;

        internal NativeQueue<AoeSpawnEvent> EventQueue;
        internal NativeStream PendingCommands;
        internal JobHandle PendingHandle;
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
            {
                PendingCommands.Dispose();
            }

            EventQueue.Dispose();
        }

        protected override void OnUpdate()
        {
            if (PendingCommands.IsCreated)
            {
                PendingCommands.Dispose();
            }

            Dependency.Complete();
            ProducerHandle.Complete();
            ProducerHandle = default;

            int queueCount = EventQueue.Count;
            using NativeArray<Entity> scopes = _scopeQuery.ToEntityArray(Allocator.Temp);
            int bufferCount = 0;
            for (int s = 0; s < scopes.Length; s++)
            {
                bufferCount += EntityManager.GetBuffer<AoeSpawnEvent>(scopes[s]).Length;
            }

            int totalEvents = queueCount + bufferCount;
            if (totalEvents == 0)
            {
                PendingCommands = default;
                return;
            }

            var events = new NativeArray<AoeSpawnEvent>(totalEvents, Allocator.TempJob);
            int offset = 0;

            while (EventQueue.TryDequeue(out AoeSpawnEvent evt))
            {
                events[offset++] = evt;
            }

            for (int s = 0; s < scopes.Length; s++)
            {
                DynamicBuffer<AoeSpawnEvent> buf = EntityManager.GetBuffer<AoeSpawnEvent>(scopes[s]);
                for (int i = 0; i < buf.Length; i++)
                {
                    events[offset++] = buf[i];
                }

                buf.Clear();
            }

            if (!SystemAPI.TryGetSingleton(out AoeSpawnTemplate templates))
            {
                PendingCommands = default;
                Dependency = events.Dispose(Dependency);
                PendingHandle = Dependency;
                return;
            }

            var vfx = World.GetExistingSystemManaged<CombatVfxDispatchSystem>();
            bool hasVfx = vfx != null && vfx.HasQueue;

            PendingCommands = new NativeStream(totalEvents, Allocator.TempJob);

            Dependency = new AoeExpansionJob
            {
                Events = events,
                Templates = templates.Map,
                Stream = PendingCommands.AsWriter(),
                VfxPending = hasVfx ? vfx.AsParallelWriter() : default,
                HasVfxWriter = hasVfx
            }.Schedule(Dependency);

            if (vfx != null)
            {
                vfx.ProducerHandle = JobHandle.CombineDependencies(vfx.ProducerHandle, Dependency);
            }

            Dependency = events.Dispose(Dependency);
            PendingHandle = Dependency;
        }

        [BurstCompile]
        private struct AoeExpansionJob : IJob
        {
            [ReadOnly] public NativeArray<AoeSpawnEvent> Events;
            [ReadOnly] public NativeHashMap<Hash128, AoeSpawnCommand> Templates;
            public NativeStream.Writer Stream;
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;
            public bool HasVfxWriter;

            public void Execute()
            {
                for (int ci = 0; ci < Events.Length; ci++)
                {
                    AoeSpawnEvent evt = Events[ci];
                    Stream.BeginForEachIndex(ci);

                    if (evt.Kind == IntervalChildKind.Aoe
                        && Templates.TryGetValue(evt.TemplateKey, out AoeSpawnCommand command))
                    {
                        Stamp(ref command, in evt);

                        CombatCollisionMath.ComputeWorldBounds(
                            command.Position, command.Radius, command.HalfExtents, command.RotationRadians, command.ShapeType,
                            out float2 boundsMin, out float2 boundsMax);

                        int count = math.max(1, command.Count);
                        for (int i = 0; i < count; i++)
                        {
                            AoeSpawnCommand spawned = command;
                            spawned.AoeId = AoeIdFor(in command, i);
                            spawned.BoundsMin = boundsMin;
                            spawned.BoundsMax = boundsMax;
                            if (spawned.HasTimedSpawner != 0)
                            {
                                TimedSpawnComponent timedSpawn = spawned.TimedSpawn;
                                timedSpawn.Faction = spawned.Faction;
                                timedSpawn.SourceId = spawned.AoeId;
                                spawned.TimedSpawn = timedSpawn;
                            }

                            Stream.Write(spawned);
                            if (HasVfxWriter)
                            {
                                VfxPending.Enqueue(new VfxPendingSpawn
                                {
                                    TypeId = command.TypeId,
                                    Trigger = 0,
                                    Position = evt.Position,
                                    AreaSize = command.AreaSize
                                });
                            }
                        }
                    }

                    Stream.EndForEachIndex();
                }
            }

            private static void Stamp(ref AoeSpawnCommand command, in AoeSpawnEvent evt)
            {
                command.Faction = evt.Faction;
                command.AoeId = evt.SourceId;
                command.Position = evt.Position;
                command.JitterSeed = evt.JitterSeed;
                command.DeterministicIdTickIndex = evt.DeterministicIdTickIndex;
            }

            private static int AoeIdFor(in AoeSpawnCommand command, int index)
            {
                if (command.DeterministicIdTickIndex <= 0)
                {
                    return command.AoeId + index;
                }

                unchecked
                {
                    int hash = command.AoeId;
                    hash = (hash * 397) ^ (int)command.JitterSeed;
                    hash = (hash * 397) ^ command.DeterministicIdTickIndex;
                    hash = (hash * 397) ^ index;
                    hash &= int.MaxValue;
                    return hash == 0 ? 1 : hash;
                }
            }
        }
    }
}
