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

            if (SystemAPI.TryGetSingletonEntity<VfxSingleton>(out Entity vfxEntity))
            {
                DynamicBuffer<VfxSpawnRequestElement> vfxBuffer =
                    EntityManager.GetBuffer<VfxSpawnRequestElement>(vfxEntity);
                int vfxCount = 0;
                for (int i = 0; i < totalEvents; i++)
                {
                    if (events[i].Kind == IntervalChildKind.Aoe
                        && templates.Map.TryGetValue(events[i].TemplateKey, out AoeSpawnCommand template))
                    {
                        vfxCount += math.max(1, template.Count);
                    }
                }

                vfxBuffer.EnsureCapacity(vfxBuffer.Length + vfxCount);
                for (int i = 0; i < totalEvents; i++)
                {
                    AoeSpawnEvent e = events[i];
                    if (e.Kind != IntervalChildKind.Aoe
                        || !templates.Map.TryGetValue(e.TemplateKey, out AoeSpawnCommand template))
                    {
                        continue;
                    }

                    int count = math.max(1, template.Count);
                    for (int j = 0; j < count; j++)
                    {
                        vfxBuffer.Add(new VfxSpawnRequestElement
                        {
                            TypeId = template.TypeId,
                            Trigger = 0,
                            Position = e.Position,
                            AreaSize = template.AreaSize
                        });
                    }
                }
            }

            PendingCommands = new NativeStream(totalEvents, Allocator.TempJob);

            Dependency = new AoeExpansionJob
            {
                Events = events,
                Templates = templates.Map,
                Stream = PendingCommands.AsWriter()
            }.Schedule(Dependency);

            Dependency = events.Dispose(Dependency);
            PendingHandle = Dependency;
        }

        [BurstCompile]
        private struct AoeExpansionJob : IJob
        {
            [ReadOnly] public NativeArray<AoeSpawnEvent> Events;
            [ReadOnly] public NativeHashMap<Hash128, AoeSpawnCommand> Templates;
            public NativeStream.Writer Stream;

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
