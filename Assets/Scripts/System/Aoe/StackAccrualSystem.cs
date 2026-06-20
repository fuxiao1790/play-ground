using System.Collections.Generic;
using PlayGround.System.Common;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace PlayGround.System.Aoe
{
    // ECS Lifecycle: transient stack intent; enqueued by AOE collision jobs, drained by StackAccrualSystem in the same simulation frame.
    public struct StackApplyEvent
    {
        public Entity TargetProxy;
        public CombatFaction Faction;
        public int TargetMask;
        public FixedList512Bytes<StackStage> Chain;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(LingeringAoeCollisionSystem))]
    [UpdateAfter(typeof(ImpactAoeCollisionSystem))]
    [UpdateBefore(typeof(AoeSpawnExpansionSystem))]
    public partial class StackAccrualSystem : SystemBase
    {
        private static readonly StackApplyEventComparer Comparer = new();
        private readonly List<StackApplyEvent> events = new();
        private int nextAoeId;

        internal NativeQueue<StackApplyEvent> EventQueue;
        internal JobHandle ProducerHandle;

        protected override void OnCreate()
        {
            EventQueue = new NativeQueue<StackApplyEvent>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            ProducerHandle.Complete();
            if (EventQueue.IsCreated)
                EventQueue.Dispose();
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();
            ProducerHandle.Complete();
            ProducerHandle = default;

            int eventCount = EventQueue.Count;
            if (eventCount == 0)
            {
                EventQueue.Clear();
                return;
            }

            events.Clear();
            if (events.Capacity < eventCount)
                events.Capacity = eventCount;

            while (EventQueue.TryDequeue(out StackApplyEvent evt))
            {
                if (evt.TargetProxy != Entity.Null && evt.Chain.Length > 0)
                    events.Add(evt);
            }

            if (events.Count == 0)
                return;

            events.Sort(Comparer);
            AoeSpawnExpansionSystem expansion = World.GetExistingSystemManaged<AoeSpawnExpansionSystem>();

            int index = 0;
            while (index < events.Count)
            {
                StackApplyEvent first = events[index];
                int statusId = StatusId(first);
                int groupEnd = index + 1;
                while (groupEnd < events.Count
                       && events[groupEnd].TargetProxy == first.TargetProxy
                       && StatusId(events[groupEnd]) == statusId)
                {
                    groupEnd++;
                }

                if (TryReadTarget(first.TargetProxy, out TargetPosition targetPosition, out TargetStackStateComponent stackState))
                {
                    for (int i = index; i < groupEnd; i++)
                        Apply(events[i], targetPosition.Value, ref stackState, expansion);

                    EntityManager.SetComponentData(first.TargetProxy, stackState);
                }

                index = groupEnd;
            }

            events.Clear();
        }

        private bool TryReadTarget(
            Entity targetProxy,
            out TargetPosition targetPosition,
            out TargetStackStateComponent stackState)
        {
            if (targetProxy != Entity.Null
                && EntityManager.Exists(targetProxy)
                && EntityManager.HasComponent<TargetPosition>(targetProxy)
                && EntityManager.HasComponent<TargetStackStateComponent>(targetProxy))
            {
                targetPosition = EntityManager.GetComponentData<TargetPosition>(targetProxy);
                stackState = EntityManager.GetComponentData<TargetStackStateComponent>(targetProxy);
                return true;
            }

            targetPosition = default;
            stackState = default;
            return false;
        }

        private void Apply(
            in StackApplyEvent evt,
            float2 targetPosition,
            ref TargetStackStateComponent stackState,
            AoeSpawnExpansionSystem expansion)
        {
            StackStage stage = evt.Chain[0];
            int amount = math.max(1, stage.StacksPerHit);
            int threshold = math.max(1, stage.StackThreshold);
            if (!stackState.TryAddStacks(stage.DebuffStatusId, amount, out int count))
                return;

            // TODO: keep the carry-over rule explicit; this currently subtracts thresholds and preserves the remainder.
            while (count >= threshold)
            {
                if (expansion != null && stage.AoeTypeId >= 0)
                    expansion.EventQueue.Enqueue(BuildSpawnEvent(evt, stage, targetPosition));

                count -= threshold;
            }

            stackState.SetCount(stage.DebuffStatusId, count);
        }

        private AoeSpawnEvent BuildSpawnEvent(
            in StackApplyEvent evt,
            in StackStage stage,
            float2 position)
        {
            AoeSpawnGeometry geometry = stage.AoeGeometry;
            float2 halfExtents = new(geometry.HalfExtents.x, geometry.HalfExtents.y);
            CombatCollisionMath.ComputeWorldBounds(
                position,
                geometry.Radius,
                halfExtents,
                geometry.RotationRadians,
                geometry.ShapeType,
                out float2 boundsMin,
                out float2 boundsMax);

            return new AoeSpawnEvent
            {
                Faction = evt.Faction,
                AoeId = NextAoeId(),
                TypeId = stage.AoeTypeId,
                Lifetime = math.max(0f, stage.AoeLifetimeSeconds),
                RepeatHitCooldownSeconds = math.max(0f, stage.AoeTickIntervalSeconds),
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = math.max(0f, stage.AoeDamage),
                    CritChance = 0f,
                    CritMultiplier = 1.5f,
                    DirectDamageEnabled = true,
                    SourceNodeId = default,
                    StackEffect = Tail(evt)
                },
                AreaSize = geometry.AreaSize,
                Radius = geometry.Radius,
                RotationRadians = geometry.RotationRadians,
                Position = position,
                HalfExtents = halfExtents,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
                ShapeType = geometry.ShapeType,
                Render = RenderFor(geometry),
                ProjectileBurst = default
            };
        }

        private int NextAoeId()
        {
            nextAoeId++;
            if (nextAoeId <= 0)
                nextAoeId = 1;
            return nextAoeId;
        }

        private static StackChainSnapshot Tail(in StackApplyEvent evt)
        {
            var tail = new StackChainSnapshot
            {
                Faction = evt.Faction,
                TargetMask = evt.TargetMask,
                Stages = default
            };

            for (int i = 1; i < evt.Chain.Length; i++)
                tail.Stages.Add(evt.Chain[i]);

            return tail;
        }

        private static CombatRenderComponent RenderFor(in AoeSpawnGeometry geometry)
        {
            if (geometry.VisualScale.x <= 0f && geometry.VisualScale.y <= 0f)
                return default;

            return new CombatRenderComponent
            {
                IsRenderable = 1,
                AlignToVelocity = 0,
                VisualScale = new float2(geometry.VisualScale.x, geometry.VisualScale.y),
                VisualRotationSin = geometry.VisualRotationSin,
                VisualRotationCos = geometry.VisualRotationCos,
                RenderZ = CombatRoot.AoeRenderZ
            };
        }

        private static int StatusId(in StackApplyEvent evt) =>
            evt.Chain.Length > 0 ? evt.Chain[0].DebuffStatusId : int.MaxValue;

        private sealed class StackApplyEventComparer : IComparer<StackApplyEvent>
        {
            public int Compare(StackApplyEvent x, StackApplyEvent y)
            {
                int indexCompare = x.TargetProxy.Index.CompareTo(y.TargetProxy.Index);
                if (indexCompare != 0)
                    return indexCompare;

                int versionCompare = x.TargetProxy.Version.CompareTo(y.TargetProxy.Version);
                return versionCompare != 0
                    ? versionCompare
                    : StatusId(x).CompareTo(StatusId(y));
            }
        }
    }
}
