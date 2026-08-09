using PlayGround.System.Combat.Aoes;
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
            NativeQueue<CircularVfxSpawnRequest>.ParallelWriter circularVfxPending,
            NativeQueue<TimedCircularVfxSpawnRequest>.ParallelWriter timedCircularVfxPending)
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

            var events = new NativeList<TargetedSpawnEvent>(Allocator.TempJob);
            NativeArray<ArchetypeChunk> scopeChunks =
                _scopeQuery.ToArchetypeChunkArray(Allocator.TempJob);
            JobHandle gatherHandle = new GatherSpawnEventsJob<TargetedSpawnEvent>
            {
                Queue = lane.EventQueue,
                BufferHandle = GetBufferTypeHandle<TargetedSpawnEvent>(false),
                ScopeChunks = scopeChunks,
                Events = events
            }.Schedule(Dependency);

            if (!SystemAPI.TryGetSingleton(out TargetedSpawnTemplate templates))
            {
                Dependency = events.Dispose(gatherHandle);
                lane.PendingHandle = scopeChunks.Dispose(Dependency);
                Dependency = lane.PendingHandle;
                return;
            }

            RefRW<CombatAoeVfxDispatchSingleton> vfx =
                SystemAPI.GetSingletonRW<CombatAoeVfxDispatchSingleton>();
            NativeList<TargetedSpawnCommand> commands = new(Allocator.TempJob);
            JobHandle expansionInput =
                JobHandle.CombineDependencies(Dependency, vfx.ValueRO.ProducerHandle);

            Dependency = new TargetedExpansionJob
            {
                Events = events.AsDeferredJobArray(),
                Templates = templates.Map,
                Commands = commands,
                CircularVfxPending = vfx.ValueRO.PendingCircularSpawns.AsParallelWriter(),
                TimedCircularVfxPending = vfx.ValueRO.PendingTimedCircularSpawns.AsParallelWriter()
            }.Schedule(JobHandle.CombineDependencies(gatherHandle, expansionInput));
            vfx.ValueRW.ProducerHandle = Dependency;

            Dependency = events.Dispose(Dependency);
            Dependency = scopeChunks.Dispose(Dependency);
            lane.Commands = commands;
            lane.PendingHandle = Dependency;
        }

        [BurstCompile]
        private struct TargetedExpansionJob : IJob
        {
            [ReadOnly] public NativeArray<TargetedSpawnEvent> Events;
            [ReadOnly] public NativeHashMap<Hash128, TargetedSpawnCommand> Templates;
            public NativeList<TargetedSpawnCommand> Commands;
            public NativeQueue<CircularVfxSpawnRequest>.ParallelWriter CircularVfxPending;
            public NativeQueue<TimedCircularVfxSpawnRequest>.ParallelWriter TimedCircularVfxPending;

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
                        TimedCircularVfxPending);
                }
            }
        }
    }
}
