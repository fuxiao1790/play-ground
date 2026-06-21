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
        public int DebuffKey;
        public int Threshold;
        public float Lifetime;
        public StackContribution Contribution;
        public DetonationSnapshot Detonation;
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
                if (evt.TargetProxy != Entity.Null && evt.DebuffKey >= 0 && evt.Detonation.Enabled)
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
                int debuffKey = first.DebuffKey;
                int groupEnd = index + 1;
                while (groupEnd < events.Count
                       && events[groupEnd].TargetProxy == first.TargetProxy
                       && events[groupEnd].DebuffKey == debuffKey)
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
            int threshold = math.max(1, evt.Threshold);
            if (!stackState.TryAddStacks(evt.DebuffKey, 1, out int count))
                return;

            if (count >= threshold)
            {
                if (expansion != null)
                    expansion.EventQueue.Enqueue(BuildSpawnEvent(evt, targetPosition));

                count = 0;
            }

            stackState.SetCount(evt.DebuffKey, count);
        }

        private AoeSpawnEvent BuildSpawnEvent(
            in StackApplyEvent evt,
            float2 position)
        {
            DetonationSnapshot detonation = evt.Detonation;
            AoeSpawnGeometry geometry = detonation.AoeGeometry;
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
                Faction = detonation.Faction,
                AoeId = NextAoeId(),
                TypeId = detonation.TypeId,
                Lifetime = math.max(0f, detonation.LifetimeSeconds),
                RepeatHitCooldownSeconds = math.max(0f, detonation.TickIntervalSeconds),
                HitPayload = new CombatHitPayload
                {
                    DamageAmount = math.max(0f, evt.Contribution.Damage * math.max(1, evt.Threshold)),
                    CritChance = detonation.CritChance,
                    CritMultiplier = detonation.CritMultiplier,
                    DirectDamageEnabled = true,
                    SourceNodeId = default,
                    StackEffect = default
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
                    : x.DebuffKey.CompareTo(y.DebuffKey);
            }
        }
    }
}
