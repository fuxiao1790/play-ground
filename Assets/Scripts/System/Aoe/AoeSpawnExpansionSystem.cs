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

            // AoeExpansionJob is a plain IJob with no ECS component access, so the
            // job-safety system cannot auto-chain it behind the IJobEntity VFX
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
                Stream = PendingCommands.AsWriter(),
                VfxPending = hasVfx ? vfx.AsParallelWriter() : default,
                HasVfxWriter = hasVfx
            }.Schedule(expansionInput);

            if (hasVfx)
            {
                // Dependency already includes the prior ProducerHandle via expansionInput.
                vfx.ProducerHandle = Dependency;
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

                            Stream.Write(spawned);
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
