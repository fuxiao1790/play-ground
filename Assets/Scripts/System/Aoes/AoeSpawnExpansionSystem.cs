using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Narrowphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Aoes
{
    internal static class AoeExpansionCore
    {
        public static void Expand(
            IntervalChildKind expectedKind,
            IntervalChildKind eventKind,
            Hash128 templateKey,
            float2 position,
            float2 aimDirection,
            CombatFaction faction,
            int sourceId,
            uint jitterSeed,
            int deterministicIdTickIndex,
            int contactGateSeedTargetId,
            NativeHashMap<Hash128, AoeSpawnCommand> templates,
            NativeList<AoeSpawnCommand> commands,
            NativeQueue<VfxSpawnRequest>.ParallelWriter basicVfxPending,
            bool hasBasicVfxWriter,
            NativeQueue<TimedVfxSpawnRequest>.ParallelWriter timedVfxPending,
            bool hasTimedVfxWriter)
        {
            if (eventKind != expectedKind
                || !templates.TryGetValue(templateKey, out AoeSpawnCommand command))
            {
                return;
            }

            Stamp(
                ref command,
                position,
                aimDirection,
                faction,
                sourceId,
                jitterSeed,
                deterministicIdTickIndex,
                contactGateSeedTargetId);

            bool lingering = expectedKind == IntervalChildKind.LingeringAoe;
            int echoCount = math.max(1, command.EchoCount);
            var rng = new Random(ScatterSeedFor(in command));
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
                    if (lingering)
                    {
                        TimedSpawnComponent timedSpawn = spawned.TimedSpawn;
                        timedSpawn.Faction = spawned.Faction;
                        timedSpawn.SourceId = spawned.AoeId;
                        spawned.TimedSpawn = timedSpawn;
                    }
                    else
                    {
                        spawned.HasTimedSpawner = 0;
                        spawned.TimedSpawn = default;
                    }
                }

                commands.Add(spawned);

                if (hasBasicVfxWriter || hasTimedVfxWriter)
                {
                    // While arming, only the telegraph plays here; the spawn
                    // burst is deferred to arm completion in CombatArmingSystem.
                    int vfxId = spawned.ArmSeconds > 0f
                        ? spawned.VfxIds.ArmingId
                        : spawned.VfxIds.SpawnId;
                    VfxTimingData timing = AoeSpawnApplyUtility.VfxTimingFor(spawned);
                    VfxEmit.Enqueue(
                        vfxId,
                        pos,
                        spawned.AreaSize,
                        timing,
                        basicVfxPending,
                        hasBasicVfxWriter,
                        timedVfxPending,
                        hasTimedVfxWriter);
                }
            }
        }

        private static void Stamp(
            ref AoeSpawnCommand command,
            float2 position,
            float2 aimDirection,
            CombatFaction faction,
            int sourceId,
            uint jitterSeed,
            int deterministicIdTickIndex,
            int contactGateSeedTargetId)
        {
            command.Faction = faction;
            command.AoeId = sourceId;
            command.Position = position;
            command.JitterSeed = jitterSeed;
            command.DeterministicIdTickIndex = deterministicIdTickIndex;
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

        private static uint ScatterSeedFor(in AoeSpawnCommand command)
        {
            if (command.DeterministicIdTickIndex <= 0)
            {
                return command.JitterSeed != 0 ? command.JitterSeed : 1u;
            }

            unchecked
            {
                uint hash = (uint)command.AoeId;
                hash = (hash * 397u) ^ command.JitterSeed;
                hash = (hash * 397u) ^ (uint)command.DeterministicIdTickIndex;
                hash *= 0x9E3779B9u;
                hash ^= hash >> 16;
                return hash != 0 ? hash : 1u;
            }
        }
    }

    // ECS Lifecycle: singleton impact-AOE spawn lane; EventQueue + Commands created by
    // ImpactAoeSpawnExpansionSystem on create, drained/produced each simulation update,
    // consumed by ImpactAoeSpawnApplySystem, disposed by ImpactAoeSpawnExpansionSystem on destroy.
    public struct ImpactAoeSpawnEventSingleton : IComponentData
    {
        public NativeQueue<ImpactAoeSpawnEvent> EventQueue;
        public NativeList<AoeSpawnCommand> Commands;
        public JobHandle ProducerHandle;
        public JobHandle PendingHandle;
    }

    // ECS Lifecycle: singleton lingering-AOE spawn lane; EventQueue + Commands created by
    // LingeringAoeSpawnExpansionSystem on create, drained/produced each simulation update,
    // consumed by LingeringAoeSpawnApplySystem, disposed by LingeringAoeSpawnExpansionSystem on destroy.
    public struct LingeringAoeSpawnEventSingleton : IComponentData
    {
        public NativeQueue<LingeringAoeSpawnEvent> EventQueue;
        public NativeList<AoeSpawnCommand> Commands;
        public JobHandle ProducerHandle;
        public JobHandle PendingHandle;
    }

    // Drains impact AOE spawn intent and writes impact AoeSpawnCommand values for apply.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TimedSpawnSystem))]
    [UpdateAfter(typeof(ImpactAoeCollisionSystem))]
    [UpdateAfter(typeof(LingeringAoeCollisionSystem))]
    [UpdateAfter(typeof(PlayGround.System.Combat.Projectiles.ProjectileCollisionSystem))]
    [UpdateAfter(typeof(StatusProcessSystem))]
    [UpdateBefore(typeof(ImpactAoeSpawnApplySystem))]
    [UpdateBefore(typeof(LingeringAoeSpawnApplySystem))]
    [UpdateBefore(typeof(PlayGround.System.Combat.Projectiles.ProjectileSpawnApplySystem))]
    public partial class ImpactAoeSpawnExpansionSystem : SystemBase
    {
        private EntityQuery _scopeQuery;
        private Entity singletonEntity;

        protected override void OnCreate()
        {
            singletonEntity = EntityManager.CreateEntity(typeof(ImpactAoeSpawnEventSingleton));
            EntityManager.SetComponentData(singletonEntity, new ImpactAoeSpawnEventSingleton
            {
                EventQueue = new NativeQueue<ImpactAoeSpawnEvent>(Allocator.Persistent)
            });

            _scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<ImpactAoeSpawnEvent>());
        }

        protected override void OnDestroy()
        {
            if (singletonEntity == Entity.Null
                || !EntityManager.Exists(singletonEntity)
                || !EntityManager.HasComponent<ImpactAoeSpawnEventSingleton>(singletonEntity))
            {
                return;
            }

            ImpactAoeSpawnEventSingleton singleton =
                EntityManager.GetComponentData<ImpactAoeSpawnEventSingleton>(singletonEntity);
            singleton.PendingHandle.Complete();
            singleton.ProducerHandle.Complete();
            if (singleton.Commands.IsCreated)
            {
                singleton.Commands.Dispose();
            }

            if (singleton.EventQueue.IsCreated)
            {
                singleton.EventQueue.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            RefRW<ImpactAoeSpawnEventSingleton> impactLane =
                SystemAPI.GetSingletonRW<ImpactAoeSpawnEventSingleton>();
            ref ImpactAoeSpawnEventSingleton singleton = ref impactLane.ValueRW;

            singleton.PendingHandle.Complete();
            if (singleton.Commands.IsCreated)
            {
                singleton.Commands.Dispose();
                singleton.Commands = default;
            }

            Dependency.Complete();
            singleton.ProducerHandle.Complete();
            singleton.ProducerHandle = default;

            int queueCount = singleton.EventQueue.Count;
            using NativeArray<Entity> scopes = _scopeQuery.ToEntityArray(Allocator.Temp);
            int bufferCount = 0;
            for (int s = 0; s < scopes.Length; s++)
            {
                bufferCount += EntityManager.GetBuffer<ImpactAoeSpawnEvent>(scopes[s]).Length;
            }

            int totalEvents = queueCount + bufferCount;
            if (totalEvents == 0)
            {
                singleton.PendingHandle = default;
                return;
            }

            var events = new NativeArray<ImpactAoeSpawnEvent>(totalEvents, Allocator.TempJob);
            int offset = 0;

            while (singleton.EventQueue.TryDequeue(out ImpactAoeSpawnEvent evt))
            {
                events[offset++] = evt;
            }

            for (int s = 0; s < scopes.Length; s++)
            {
                DynamicBuffer<ImpactAoeSpawnEvent> buf = EntityManager.GetBuffer<ImpactAoeSpawnEvent>(scopes[s]);
                for (int i = 0; i < buf.Length; i++)
                {
                    events[offset++] = buf[i];
                }

                buf.Clear();
            }

            if (!SystemAPI.TryGetSingleton(out AoeSpawnTemplate templates))
            {
                Dependency = events.Dispose(Dependency);
                singleton.PendingHandle = Dependency;
                return;
            }

            bool hasVfx = SystemAPI.TryGetSingletonRW<CombatAoeVfxDispatchSingleton>(
                out RefRW<CombatAoeVfxDispatchSingleton> vfx);
            NativeQueue<VfxSpawnRequest> basicVfxQueue = hasVfx ? vfx.ValueRO.PendingBasicSpawns : default;
            NativeQueue<TimedVfxSpawnRequest> timedVfxQueue = hasVfx ? vfx.ValueRO.PendingTimedSpawns : default;
            bool hasBasicVfx = hasVfx && basicVfxQueue.IsCreated;
            bool hasTimedVfx = hasVfx && timedVfxQueue.IsCreated;
            hasVfx = hasBasicVfx || hasTimedVfx;

            NativeList<AoeSpawnCommand> commands =
                new(events.Length, Allocator.TempJob);

            JobHandle expansionInput = Dependency;
            if (hasVfx)
            {
                expansionInput = JobHandle.CombineDependencies(expansionInput, vfx.ValueRO.ProducerHandle);
            }

            Dependency = new ImpactAoeExpansionJob
            {
                Events = events,
                Templates = templates.Map,
                Commands = commands,
                BasicVfxPending = hasBasicVfx ? basicVfxQueue.AsParallelWriter() : default,
                HasBasicVfxWriter = hasBasicVfx,
                TimedVfxPending = hasTimedVfx ? timedVfxQueue.AsParallelWriter() : default,
                HasTimedVfxWriter = hasTimedVfx
            }.Schedule(expansionInput);

            if (hasVfx)
            {
                vfx.ValueRW.ProducerHandle = Dependency;
            }

            Dependency = events.Dispose(Dependency);
            singleton.Commands = commands;
            singleton.PendingHandle = Dependency;
        }

        [BurstCompile]
        private struct ImpactAoeExpansionJob : IJob
        {
            [ReadOnly] public NativeArray<ImpactAoeSpawnEvent> Events;
            [ReadOnly] public NativeHashMap<Hash128, AoeSpawnCommand> Templates;
            public NativeList<AoeSpawnCommand> Commands;
            public NativeQueue<VfxSpawnRequest>.ParallelWriter BasicVfxPending;
            public bool HasBasicVfxWriter;
            public NativeQueue<TimedVfxSpawnRequest>.ParallelWriter TimedVfxPending;
            public bool HasTimedVfxWriter;

            public void Execute()
            {
                for (int ci = 0; ci < Events.Length; ci++)
                {
                    ImpactAoeSpawnEvent evt = Events[ci];
                    AoeExpansionCore.Expand(
                        IntervalChildKind.ImpactAoe,
                        evt.Kind,
                        evt.TemplateKey,
                        evt.Position,
                        evt.AimDirection,
                        evt.Faction,
                        evt.SourceId,
                        evt.JitterSeed,
                        evt.DeterministicIdTickIndex,
                        evt.ContactGateSeedTargetId,
                        Templates,
                        Commands,
                        BasicVfxPending,
                        HasBasicVfxWriter,
                        TimedVfxPending,
                        HasTimedVfxWriter);
                }
            }
        }
    }

    // Drains lingering AOE spawn intent and writes lingering AoeSpawnCommand values for apply.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TimedSpawnSystem))]
    [UpdateAfter(typeof(ImpactAoeCollisionSystem))]
    [UpdateAfter(typeof(LingeringAoeCollisionSystem))]
    [UpdateAfter(typeof(PlayGround.System.Combat.Projectiles.ProjectileCollisionSystem))]
    [UpdateAfter(typeof(StatusProcessSystem))]
    [UpdateBefore(typeof(ImpactAoeSpawnApplySystem))]
    [UpdateBefore(typeof(LingeringAoeSpawnApplySystem))]
    [UpdateBefore(typeof(PlayGround.System.Combat.Projectiles.ProjectileSpawnApplySystem))]
    public partial class LingeringAoeSpawnExpansionSystem : SystemBase
    {
        private EntityQuery _scopeQuery;
        private Entity singletonEntity;

        protected override void OnCreate()
        {
            singletonEntity = EntityManager.CreateEntity(typeof(LingeringAoeSpawnEventSingleton));
            EntityManager.SetComponentData(singletonEntity, new LingeringAoeSpawnEventSingleton
            {
                EventQueue = new NativeQueue<LingeringAoeSpawnEvent>(Allocator.Persistent)
            });

            _scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<LingeringAoeSpawnEvent>());
        }

        protected override void OnDestroy()
        {
            if (singletonEntity == Entity.Null
                || !EntityManager.Exists(singletonEntity)
                || !EntityManager.HasComponent<LingeringAoeSpawnEventSingleton>(singletonEntity))
            {
                return;
            }

            LingeringAoeSpawnEventSingleton singleton =
                EntityManager.GetComponentData<LingeringAoeSpawnEventSingleton>(singletonEntity);
            singleton.PendingHandle.Complete();
            singleton.ProducerHandle.Complete();
            if (singleton.Commands.IsCreated)
            {
                singleton.Commands.Dispose();
            }

            if (singleton.EventQueue.IsCreated)
            {
                singleton.EventQueue.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            RefRW<LingeringAoeSpawnEventSingleton> lingeringLane =
                SystemAPI.GetSingletonRW<LingeringAoeSpawnEventSingleton>();
            ref LingeringAoeSpawnEventSingleton singleton = ref lingeringLane.ValueRW;

            singleton.PendingHandle.Complete();
            if (singleton.Commands.IsCreated)
            {
                singleton.Commands.Dispose();
                singleton.Commands = default;
            }

            Dependency.Complete();
            singleton.ProducerHandle.Complete();
            singleton.ProducerHandle = default;

            int queueCount = singleton.EventQueue.Count;
            using NativeArray<Entity> scopes = _scopeQuery.ToEntityArray(Allocator.Temp);
            int bufferCount = 0;
            for (int s = 0; s < scopes.Length; s++)
            {
                bufferCount += EntityManager.GetBuffer<LingeringAoeSpawnEvent>(scopes[s]).Length;
            }

            int totalEvents = queueCount + bufferCount;
            if (totalEvents == 0)
            {
                singleton.PendingHandle = default;
                return;
            }

            var events = new NativeArray<LingeringAoeSpawnEvent>(totalEvents, Allocator.TempJob);
            int offset = 0;

            while (singleton.EventQueue.TryDequeue(out LingeringAoeSpawnEvent evt))
            {
                events[offset++] = evt;
            }

            for (int s = 0; s < scopes.Length; s++)
            {
                DynamicBuffer<LingeringAoeSpawnEvent> buf =
                    EntityManager.GetBuffer<LingeringAoeSpawnEvent>(scopes[s]);
                for (int i = 0; i < buf.Length; i++)
                {
                    events[offset++] = buf[i];
                }

                buf.Clear();
            }

            if (!SystemAPI.TryGetSingleton(out AoeSpawnTemplate templates))
            {
                Dependency = events.Dispose(Dependency);
                singleton.PendingHandle = Dependency;
                return;
            }

            bool hasVfx = SystemAPI.TryGetSingletonRW<CombatAoeVfxDispatchSingleton>(
                out RefRW<CombatAoeVfxDispatchSingleton> vfx);
            NativeQueue<VfxSpawnRequest> basicVfxQueue = hasVfx ? vfx.ValueRO.PendingBasicSpawns : default;
            NativeQueue<TimedVfxSpawnRequest> timedVfxQueue = hasVfx ? vfx.ValueRO.PendingTimedSpawns : default;
            bool hasBasicVfx = hasVfx && basicVfxQueue.IsCreated;
            bool hasTimedVfx = hasVfx && timedVfxQueue.IsCreated;
            hasVfx = hasBasicVfx || hasTimedVfx;

            NativeList<AoeSpawnCommand> commands =
                new(events.Length, Allocator.TempJob);

            JobHandle expansionInput = Dependency;
            if (hasVfx)
            {
                expansionInput = JobHandle.CombineDependencies(expansionInput, vfx.ValueRO.ProducerHandle);
            }

            Dependency = new LingeringAoeExpansionJob
            {
                Events = events,
                Templates = templates.Map,
                Commands = commands,
                BasicVfxPending = hasBasicVfx ? basicVfxQueue.AsParallelWriter() : default,
                HasBasicVfxWriter = hasBasicVfx,
                TimedVfxPending = hasTimedVfx ? timedVfxQueue.AsParallelWriter() : default,
                HasTimedVfxWriter = hasTimedVfx
            }.Schedule(expansionInput);

            if (hasVfx)
            {
                vfx.ValueRW.ProducerHandle = Dependency;
            }

            Dependency = events.Dispose(Dependency);
            singleton.Commands = commands;
            singleton.PendingHandle = Dependency;
        }

        [BurstCompile]
        private struct LingeringAoeExpansionJob : IJob
        {
            [ReadOnly] public NativeArray<LingeringAoeSpawnEvent> Events;
            [ReadOnly] public NativeHashMap<Hash128, AoeSpawnCommand> Templates;
            public NativeList<AoeSpawnCommand> Commands;
            public NativeQueue<VfxSpawnRequest>.ParallelWriter BasicVfxPending;
            public bool HasBasicVfxWriter;
            public NativeQueue<TimedVfxSpawnRequest>.ParallelWriter TimedVfxPending;
            public bool HasTimedVfxWriter;

            public void Execute()
            {
                for (int ci = 0; ci < Events.Length; ci++)
                {
                    LingeringAoeSpawnEvent evt = Events[ci];
                    AoeExpansionCore.Expand(
                        IntervalChildKind.LingeringAoe,
                        evt.Kind,
                        evt.TemplateKey,
                        evt.Position,
                        evt.AimDirection,
                        evt.Faction,
                        evt.SourceId,
                        evt.JitterSeed,
                        evt.DeterministicIdTickIndex,
                        evt.ContactGateSeedTargetId,
                        Templates,
                        Commands,
                        BasicVfxPending,
                        HasBasicVfxWriter,
                        TimedVfxPending,
                        HasTimedVfxWriter);
                }
            }
        }
    }
}
