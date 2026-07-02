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
    // stamps per-instance frame data, and writes AoeSpawnCommand into impact or lingering apply containers.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ImpactAoeCollisionSystem))]
    [UpdateAfter(typeof(PlayGround.System.Projectile.ProjectileCollisionSystem))]
    [UpdateAfter(typeof(StatusProcessSystem))]
    [UpdateBefore(typeof(ImpactAoeSpawnApplySystem))]
    [UpdateBefore(typeof(LingeringAoeSpawnApplySystem))]
    [UpdateBefore(typeof(PlayGround.System.Projectile.ProjectileSpawnApplySystem))]
    public partial class AoeSpawnExpansionSystem : SystemBase
    {
        private EntityQuery _scopeQuery;

        internal NativeQueue<AoeSpawnEvent> EventQueue;
        internal NativeStream ImpactCommandStream;
        internal NativeStream LingeringCommandStream;
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
            PendingHandle.Complete();
            if (ImpactCommandStream.IsCreated)
            {
                ImpactCommandStream.Dispose();
            }

            if (LingeringCommandStream.IsCreated)
            {
                LingeringCommandStream.Dispose();
            }

            EventQueue.Dispose();
        }

        protected override void OnUpdate()
        {
            if (ImpactCommandStream.IsCreated)
            {
                ImpactCommandStream.Dispose();
            }

            if (LingeringCommandStream.IsCreated)
            {
                LingeringCommandStream.Dispose();
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
                PendingHandle = default;
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
                Dependency = events.Dispose(Dependency);
                PendingHandle = Dependency;
                return;
            }

            var vfx = World.GetExistingSystemManaged<CombatVfxDispatchSystem>();
            bool hasVfx = vfx != null && vfx.HasQueue;

            ImpactCommandStream = new NativeStream(events.Length, Allocator.TempJob);
            LingeringCommandStream = new NativeStream(events.Length, Allocator.TempJob);

            // AoeExpansionJob is a plain IJobParallelFor with no ECS component access,
            // so the job-safety system cannot auto-chain it behind the IJobEntity VFX
            // producers (lifetime, pulse, collision) the way shared component access
            // chains those. Feed the collector's accumulated ProducerHandle in as an
            // input dependency so writes to the shared VFX queue stay serialized.
            JobHandle expansionInput = Dependency;
            if (hasVfx)
            {
                expansionInput = JobHandle.CombineDependencies(expansionInput, vfx.ProducerHandle);
            }

            Dependency = new AoeExpansionJob
            {
                Events = events,
                Templates = templates.Map,
                ImpactCommands = ImpactCommandStream.AsWriter(),
                LingeringCommands = LingeringCommandStream.AsWriter(),
                VfxPending = hasVfx ? vfx.AsParallelWriter() : default,
                HasVfxWriter = hasVfx
            }.Schedule(events.Length, 1, expansionInput);

            if (hasVfx)
            {
                // Dependency already includes the prior ProducerHandle via expansionInput.
                vfx.ProducerHandle = Dependency;
            }

            Dependency = events.Dispose(Dependency);
            PendingHandle = Dependency;
        }

        [BurstCompile]
        private struct AoeExpansionJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<AoeSpawnEvent> Events;
            [ReadOnly] public NativeHashMap<Hash128, AoeSpawnCommand> Templates;
            public NativeStream.Writer ImpactCommands;
            public NativeStream.Writer LingeringCommands;
            public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;
            public bool HasVfxWriter;

            public void Execute(int ci)
            {
                AoeSpawnEvent evt = Events[ci];

                if (evt.Kind != IntervalChildKind.Aoe
                    || !Templates.TryGetValue(evt.TemplateKey, out AoeSpawnCommand command))
                {
                    return;
                }

                Stamp(ref command, in evt);

                // Every echo of a command shares the template's lifetime, so the whole
                // event routes to one domain stream. Begin that stream's lane once.
                bool lingering = command.Lifetime > 0f;
                if (lingering)
                {
                    LingeringCommands.BeginForEachIndex(ci);
                }
                else
                {
                    ImpactCommands.BeginForEachIndex(ci);
                }

                int echoCount = math.max(1, command.EchoCount);
                var rng = new Random(command.JitterSeed != 0 ? command.JitterSeed : 1u);
                for (int i = 0; i < echoCount; i++)
                {
                    AoeSpawnCommand spawned = command;
                    spawned.AoeId = AoeIdFor(in command, i);
                    float2 pos = command.Position;
                    if (command.ScatterRadius > 0f)
                    {
                        float angle = rng.NextFloat(0f, 2f * math.PI);
                        float dist = command.ScatterRadius * math.sqrt(rng.NextFloat());
                        math.sincos(angle, out float s, out float c);
                        pos += new float2(c, s) * dist;
                    }

                    CombatCollisionMath.ComputeWorldBounds(
                        pos, command.Radius, command.HalfExtents, command.RotationRadians, command.ShapeType,
                        out float2 boundsMin, out float2 boundsMax);

                    spawned.Position = pos;
                    spawned.BoundsMin = boundsMin;
                    spawned.BoundsMax = boundsMax;
                    if (spawned.HasTimedSpawner != 0)
                    {
                        TimedSpawnComponent timedSpawn = spawned.TimedSpawn;
                        timedSpawn.Faction = spawned.Faction;
                        timedSpawn.SourceId = spawned.AoeId;
                        spawned.TimedSpawn = timedSpawn;
                    }

                    if (lingering)
                    {
                        LingeringCommands.Write(spawned);
                    }
                    else
                    {
                        ImpactCommands.Write(spawned);
                    }

                    if (HasVfxWriter)
                    {
                        VfxPending.Enqueue(new VfxPendingSpawn
                        {
                            TypeId = command.TypeId,
                            Trigger = 0,
                            Position = pos,
                            AreaSize = command.AreaSize
                        });
                    }
                }

                if (lingering)
                {
                    LingeringCommands.EndForEachIndex();
                }
                else
                {
                    ImpactCommands.EndForEachIndex();
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
