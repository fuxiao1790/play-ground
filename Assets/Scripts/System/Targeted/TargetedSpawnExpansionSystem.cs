using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Audio;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Vfx;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Targeted
{
    internal static class TargetedExpansionCore
    {
        public static void Expand(
            IntervalChildKind eventKind,
            Hash128 templateKey,
            float2 origin,
            float2 acquireAnchor,
            byte hasAcquiredTarget,
            CombatFaction faction,
            int sourceId,
            uint jitterSeed,
            int deterministicIdTickIndex,
            NativeHashMap<Hash128, TargetedSpawnCommand> templates,
            NativeList<TargetedSpawnCommand> commands,
            NativeQueue<ImpactCircleVfxEvent>.ParallelWriter circularVfxPending,
            NativeQueue<LingeringCircleVfxEvent>.ParallelWriter timedCircularVfxPending,
            NativeQueue<SoundEvent>.ParallelWriter soundsPending)
        {
            if (eventKind != IntervalChildKind.Targeted
                || !templates.TryGetValue(templateKey, out TargetedSpawnCommand command))
            {
                return;
            }

            Stamp(
                ref command,
                origin,
                acquireAnchor,
                hasAcquiredTarget,
                faction,
                sourceId,
                jitterSeed,
                deterministicIdTickIndex);

            int echoCount = math.max(1, command.EchoCount);
            for (int i = 0; i < echoCount; i++)
            {
                TargetedSpawnCommand spawned = command;
                spawned.TargetedId = TargetedIdFor(in command, i);
                spawned.InstanceIndex = i;

                commands.Add(spawned);

                int vfxId = spawned.ArmSeconds > 0f
                    ? spawned.VfxIds.ArmingId
                    : spawned.VfxIds.SpawnId;
                VfxEmit.Enqueue(
                    vfxId,
                    spawned.Origin,
                    spawned.VfxSize.EffectSize,
                    TargetedVfxUtility.TimingFor(spawned),
                    circularVfxPending,
                    timedCircularVfxPending);
                SoundEmit.Enqueue(
                    spawned.SoundIds.SpawnId,
                    spawned.Origin,
                    spawned.SpawnSoundRadius,
                    SoundCategory.Spawn,
                    spawned.Faction,
                    soundsPending);
            }
        }

        private static void Stamp(
            ref TargetedSpawnCommand command,
            float2 origin,
            float2 acquireAnchor,
            byte hasAcquiredTarget,
            CombatFaction faction,
            int sourceId,
            uint jitterSeed,
            int deterministicIdTickIndex)
        {
            command.Faction = faction;
            command.TargetedId = sourceId;
            command.Origin = origin;
            command.AcquireAnchor = acquireAnchor;
            command.HasAcquiredTarget = hasAcquiredTarget;
            command.JitterSeed = jitterSeed;
            command.DeterministicIdTickIndex = deterministicIdTickIndex;
        }

        private static int TargetedIdFor(in TargetedSpawnCommand command, int index)
        {
            if (command.DeterministicIdTickIndex <= 0)
            {
                return command.TargetedId + index;
            }

            unchecked
            {
                int hash = command.TargetedId;
                hash = (hash * 397) ^ (int)command.JitterSeed;
                hash = (hash * 397) ^ command.DeterministicIdTickIndex;
                hash = (hash * 397) ^ index;
                hash &= int.MaxValue;
                return hash == 0 ? 1 : hash;
            }
        }
    }

    // ECS Lifecycle: singleton targeted spawn lane; EventQueue + Commands created by
    // TargetedSpawnExpansionSystem on create, drained/produced each simulation update,
    // consumed by TargetedSpawnApplySystem, disposed by TargetedSpawnExpansionSystem on destroy.
    public struct TargetedSpawnEventSingleton : IComponentData
    {
        public NativeQueue<TargetedSpawnEvent> EventQueue;
        public NativeList<TargetedSpawnCommand> Commands;
        public JobHandle ProducerHandle;
        public JobHandle PendingHandle;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TimedSpawnSystem))]
    [UpdateAfter(typeof(ImpactAoeCollisionSystem))]
    [UpdateAfter(typeof(LingeringAoeCollisionSystem))]
    [UpdateAfter(typeof(PlayGround.System.Combat.Projectiles.ProjectileDiscreteCollisionSystem))]
    [UpdateAfter(typeof(StatusProcessSystem))]
    [UpdateBefore(typeof(TargetedSpawnApplySystem))]
    public partial class TargetedSpawnExpansionSystem : SystemBase
    {
        private EntityQuery _scopeQuery;
        private Entity _singletonEntity;

        protected override void OnCreate()
        {
            _singletonEntity = EntityManager.CreateEntity(typeof(TargetedSpawnEventSingleton));
            EntityManager.SetComponentData(_singletonEntity, new TargetedSpawnEventSingleton
            {
                EventQueue = new NativeQueue<TargetedSpawnEvent>(Allocator.Persistent)
            });
            _scopeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CombatScope>(),
                ComponentType.ReadWrite<TargetedSpawnEvent>());
        }

        protected override void OnDestroy()
        {
            if (_singletonEntity == Entity.Null
                || !EntityManager.Exists(_singletonEntity)
                || !EntityManager.HasComponent<TargetedSpawnEventSingleton>(_singletonEntity))
            {
                return;
            }

            TargetedSpawnEventSingleton lane =
                EntityManager.GetComponentData<TargetedSpawnEventSingleton>(_singletonEntity);
            lane.PendingHandle.Complete();
            lane.ProducerHandle.Complete();
            if (lane.Commands.IsCreated)
            {
                lane.Commands.Dispose();
            }

            if (lane.EventQueue.IsCreated)
            {
                lane.EventQueue.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            RefRW<TargetedSpawnEventSingleton> laneRef =
                SystemAPI.GetSingletonRW<TargetedSpawnEventSingleton>();
            ref TargetedSpawnEventSingleton lane = ref laneRef.ValueRW;

            lane.PendingHandle.Complete();
            if (lane.Commands.IsCreated)
            {
                lane.Commands.Dispose();
                lane.Commands = default;
            }

            Dependency.Complete();
            lane.ProducerHandle.Complete();
            lane.ProducerHandle = default;

            int queueCount = lane.EventQueue.Count;
            using NativeArray<Entity> scopes = _scopeQuery.ToEntityArray(Allocator.Temp);
            int bufferCount = 0;
            for (int s = 0; s < scopes.Length; s++)
            {
                bufferCount += EntityManager.GetBuffer<TargetedSpawnEvent>(scopes[s]).Length;
            }

            int totalEvents = queueCount + bufferCount;
            if (totalEvents == 0)
            {
                lane.PendingHandle = default;
                return;
            }

            var events = new NativeArray<TargetedSpawnEvent>(totalEvents, Allocator.TempJob);
            int offset = 0;
            while (lane.EventQueue.TryDequeue(out TargetedSpawnEvent evt))
            {
                events[offset++] = evt;
            }

            for (int s = 0; s < scopes.Length; s++)
            {
                DynamicBuffer<TargetedSpawnEvent> buffer =
                    EntityManager.GetBuffer<TargetedSpawnEvent>(scopes[s]);
                for (int i = 0; i < buffer.Length; i++)
                {
                    events[offset++] = buffer[i];
                }

                buffer.Clear();
            }

            if (!SystemAPI.TryGetSingleton(out TargetedSpawnTemplate templates))
            {
                Dependency = events.Dispose(Dependency);
                lane.PendingHandle = Dependency;
                return;
            }

            RefRW<CombatAoeVfxDispatchSingleton> vfx =
                SystemAPI.GetSingletonRW<CombatAoeVfxDispatchSingleton>();
            RefRW<SoundEventSingleton> sounds =
                SystemAPI.GetSingletonRW<SoundEventSingleton>();
            NativeList<TargetedSpawnCommand> commands = new(events.Length, Allocator.TempJob);
            JobHandle expansionInput = JobHandle.CombineDependencies(
                Dependency,
                vfx.ValueRO.ProducerHandle,
                sounds.ValueRO.ProducerHandle);

            Dependency = new TargetedExpansionJob
            {
                Events = events,
                Templates = templates.Map,
                Commands = commands,
                CircularVfxPending = vfx.ValueRO.PendingCircularSpawns.AsParallelWriter(),
                TimedCircularVfxPending = vfx.ValueRO.PendingTimedCircularSpawns.AsParallelWriter(),
                SoundsPending = sounds.ValueRO.Events.AsParallelWriter()
            }.Schedule(expansionInput);
            vfx.ValueRW.ProducerHandle = Dependency;
            sounds.ValueRW.ProducerHandle = Dependency;

            Dependency = events.Dispose(Dependency);
            lane.Commands = commands;
            lane.PendingHandle = Dependency;
        }

        [BurstCompile]
        private struct TargetedExpansionJob : IJob
        {
            [ReadOnly] public NativeArray<TargetedSpawnEvent> Events;
            [ReadOnly] public NativeHashMap<Hash128, TargetedSpawnCommand> Templates;
            public NativeList<TargetedSpawnCommand> Commands;
            public NativeQueue<ImpactCircleVfxEvent>.ParallelWriter CircularVfxPending;
            public NativeQueue<LingeringCircleVfxEvent>.ParallelWriter TimedCircularVfxPending;
            public NativeQueue<SoundEvent>.ParallelWriter SoundsPending;

            public void Execute()
            {
                for (int i = 0; i < Events.Length; i++)
                {
                    TargetedSpawnEvent evt = Events[i];
                    TargetedExpansionCore.Expand(
                        evt.Kind,
                        evt.TemplateKey,
                        evt.Position,
                        evt.AcquireAnchor,
                        evt.HasAcquiredTarget,
                        evt.Faction,
                        evt.SourceId,
                        evt.JitterSeed,
                        evt.DeterministicIdTickIndex,
                        Templates,
                        Commands,
                        CircularVfxPending,
                        TimedCircularVfxPending,
                        SoundsPending);
                }
            }
        }
    }
}
