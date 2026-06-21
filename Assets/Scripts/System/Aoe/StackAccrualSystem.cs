using System.Collections.Generic;
using PlayGround.System.Common;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

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
        private const int MaxTargetStackEntries = 32;
        private static readonly StackApplyEventComparer Comparer = new();
        private static readonly ProfilerCounterValue<int> EntryEvictionCounter =
            new(ProfilerCategory.Scripts, "StackAccrualSystem.EntryEvictions", ProfilerMarkerDataUnit.Count);

        private readonly List<StackApplyEvent> events = new();
        private int nextAoeId;
        private int entryEvictions;

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

                if (TryReadTarget(first.TargetProxy, out TargetPosition targetPosition, out DynamicBuffer<TargetStackEntry> stackEntries))
                {
                    for (int i = index; i < groupEnd; i++)
                        Apply(events[i], targetPosition.Value, stackEntries, expansion);
                }

                index = groupEnd;
            }

            events.Clear();
        }

        private bool TryReadTarget(
            Entity targetProxy,
            out TargetPosition targetPosition,
            out DynamicBuffer<TargetStackEntry> stackEntries)
        {
            if (targetProxy != Entity.Null
                && EntityManager.Exists(targetProxy)
                && EntityManager.HasComponent<TargetPosition>(targetProxy)
                && EntityManager.HasBuffer<TargetStackEntry>(targetProxy))
            {
                targetPosition = EntityManager.GetComponentData<TargetPosition>(targetProxy);
                stackEntries = EntityManager.GetBuffer<TargetStackEntry>(targetProxy);
                return true;
            }

            targetPosition = default;
            stackEntries = default;
            return false;
        }

        private void Apply(
            in StackApplyEvent evt,
            float2 targetPosition,
            DynamicBuffer<TargetStackEntry> stackEntries,
            AoeSpawnExpansionSystem expansion)
        {
            int threshold = math.max(1, evt.Threshold);
            if (evt.DebuffKey < 0)
                return;

            int entryIndex = FindEntryIndex(stackEntries, evt.DebuffKey);
            if (entryIndex < 0)
                entryIndex = AddEntry(stackEntries, evt);

            TargetStackEntry entry = stackEntries[entryIndex];
            entry.Count++;
            entry.SummedDamage += evt.Contribution.Damage;
            entry.SummedProjectileCount += evt.Contribution.ProjectileCount;
            entry.SummedArea += evt.Contribution.AreaSize;
            entry.LifetimeRemaining = math.max(0f, evt.Lifetime);

            if (entry.Count >= threshold)
            {
                if (expansion != null)
                    expansion.EventQueue.Enqueue(BuildSpawnEvent(entry, targetPosition));

                stackEntries.RemoveAt(entryIndex);
                return;
            }

            stackEntries[entryIndex] = entry;
        }

        private int AddEntry(DynamicBuffer<TargetStackEntry> stackEntries, in StackApplyEvent evt)
        {
            if (stackEntries.Length >= MaxTargetStackEntries)
            {
                stackEntries.RemoveAt(LeastLifetimeRemainingIndex(stackEntries));
                entryEvictions++;
                EntryEvictionCounter.Value = entryEvictions;
            }

            stackEntries.Add(new TargetStackEntry
            {
                DebuffKey = evt.DebuffKey,
                Count = 0,
                SummedDamage = 0f,
                SummedProjectileCount = 0,
                SummedArea = 0f,
                LifetimeRemaining = math.max(0f, evt.Lifetime),
                Detonation = evt.Detonation
            });

            return stackEntries.Length - 1;
        }

        private static int FindEntryIndex(DynamicBuffer<TargetStackEntry> stackEntries, int debuffKey)
        {
            for (int i = 0; i < stackEntries.Length; i++)
            {
                if (stackEntries[i].DebuffKey == debuffKey)
                    return i;
            }

            return -1;
        }

        private static int LeastLifetimeRemainingIndex(DynamicBuffer<TargetStackEntry> stackEntries)
        {
            int index = 0;
            float leastLifetime = stackEntries[0].LifetimeRemaining;
            for (int i = 1; i < stackEntries.Length; i++)
            {
                float lifetime = stackEntries[i].LifetimeRemaining;
                if (lifetime < leastLifetime)
                {
                    leastLifetime = lifetime;
                    index = i;
                }
            }

            return index;
        }

        private AoeSpawnEvent BuildSpawnEvent(
            in TargetStackEntry entry,
            float2 position)
        {
            DetonationSnapshot detonation = entry.Detonation;
            AoeSpawnGeometry geometry = detonation.AoeGeometry;
            float areaScale = geometry.AreaSize > 0f && entry.SummedArea > 0f
                ? entry.SummedArea / geometry.AreaSize
                : 1f;
            float radius = geometry.Radius * areaScale;
            float2 halfExtents = new(geometry.HalfExtents.x * areaScale, geometry.HalfExtents.y * areaScale);
            CombatCollisionMath.ComputeWorldBounds(
                position,
                radius,
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
                    DamageAmount = math.max(0f, entry.SummedDamage),
                    CritChance = detonation.CritChance,
                    CritMultiplier = detonation.CritMultiplier,
                    DirectDamageEnabled = true,
                    SourceNodeId = default,
                    StackEffect = default
                },
                AreaSize = entry.SummedArea > 0f ? entry.SummedArea : geometry.AreaSize,
                Radius = radius,
                RotationRadians = geometry.RotationRadians,
                Position = position,
                HalfExtents = halfExtents,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
                ShapeType = geometry.ShapeType,
                Render = RenderFor(geometry, areaScale),
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

        private static CombatRenderComponent RenderFor(in AoeSpawnGeometry geometry, float areaScale)
        {
            if (geometry.VisualScale.x <= 0f && geometry.VisualScale.y <= 0f)
                return default;

            return new CombatRenderComponent
            {
                IsRenderable = 1,
                AlignToVelocity = 0,
                VisualScale = new float2(geometry.VisualScale.x * areaScale, geometry.VisualScale.y * areaScale),
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
