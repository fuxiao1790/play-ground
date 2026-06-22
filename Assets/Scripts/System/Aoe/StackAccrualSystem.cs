using System.Collections.Generic;
using PlayGround.Common;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

//todo: this type is no longer specific to aoe, move this out
//todo: implementation needs to be investigated and reconsidered.
namespace PlayGround.System.Aoe
{
    // ECS Lifecycle: transient stack intent; enqueued by applicator collision jobs, drained by StackAccrualSystem in the same simulation frame.
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
    [UpdateAfter(typeof(PlayGround.System.Projectile.ProjectileCollisionSystem))]
    [UpdateAfter(typeof(LingeringAoeCollisionSystem))]
    [UpdateAfter(typeof(ImpactAoeCollisionSystem))]
    [UpdateBefore(typeof(AoeSpawnExpansionSystem))]
    [UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]
    public partial class StackAccrualSystem : SystemBase
    {
        private const int MaxTargetStackEntries = 32;
        private static readonly StackApplyEventComparer Comparer = new();
        private static readonly ProfilerCounterValue<int> EntryEvictionCounter =
            new(ProfilerCategory.Scripts, "StackAccrualSystem.EntryEvictions", ProfilerMarkerDataUnit.Count);

        private readonly List<StackApplyEvent> events = new();
        private EntityQuery targetStackQuery;
        private int nextAoeId;
        private int nextProjectileDetonationSourceId;
        private int entryEvictions;

        internal NativeQueue<StackApplyEvent> EventQueue;
        internal JobHandle ProducerHandle;

        protected override void OnCreate()
        {
            EventQueue = new NativeQueue<StackApplyEvent>(Allocator.Persistent);
            targetStackQuery = EntityManager.CreateEntityQuery(ComponentType.ReadWrite<TargetStackEntry>());
        }

        protected override void OnDestroy()
        {
            ProducerHandle.Complete();
            targetStackQuery.Dispose();
            if (EventQueue.IsCreated)
                EventQueue.Dispose();
        }

        protected override void OnUpdate()
        {
            Dependency.Complete();
            ProducerHandle.Complete();
            ProducerHandle = default;

            TickAndFizzle(math.max(0f, SystemAPI.Time.DeltaTime));

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
            AoeSpawnExpansionSystem aoeExpansion = World.GetExistingSystemManaged<AoeSpawnExpansionSystem>();
            ProjectileSpawnExpansionSystem projectileExpansion =
                World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>();

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
                        Apply(events[i], targetPosition.Value, stackEntries, aoeExpansion, projectileExpansion);
                }

                index = groupEnd;
            }

            events.Clear();
        }

        private void TickAndFizzle(float deltaTime)
        {
            if (deltaTime <= 0f || targetStackQuery.IsEmptyIgnoreFilter)
                return;

            using NativeArray<Entity> targets = targetStackQuery.ToEntityArray(Allocator.Temp);
            for (int t = 0; t < targets.Length; t++)
            {
                DynamicBuffer<TargetStackEntry> stackEntries = EntityManager.GetBuffer<TargetStackEntry>(targets[t]);
                for (int i = stackEntries.Length - 1; i >= 0; i--)
                {
                    TargetStackEntry entry = stackEntries[i];
                    entry.LifetimeRemaining = math.max(0f, entry.LifetimeRemaining - deltaTime);
                    if (entry.LifetimeRemaining <= 0f)
                    {
                        // Entries still in the buffer are below threshold; threshold hits detonate and clear immediately.
                        stackEntries.RemoveAt(i);
                        continue;
                    }

                    stackEntries[i] = entry;
                }
            }
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
            AoeSpawnExpansionSystem aoeExpansion,
            ProjectileSpawnExpansionSystem projectileExpansion)
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
                BuildDetonationSpawn(entry.Detonation, entry, targetPosition, aoeExpansion, projectileExpansion);
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

        private void BuildDetonationSpawn(
            in DetonationSnapshot snapshot,
            in TargetStackEntry entry,
            float2 position,
            AoeSpawnExpansionSystem aoeExpansion,
            ProjectileSpawnExpansionSystem projectileExpansion)
        {
            switch (snapshot.Kind)
            {
                case StackDetonationKind.Aoe:
                    aoeExpansion.EventQueue.Enqueue(BuildAoeSpawnEvent(entry, position));
                    aoeExpansion.ProducerHandle =
                        JobHandle.CombineDependencies(aoeExpansion.ProducerHandle, Dependency);
                    return;
                case StackDetonationKind.Projectile:
                    if (!snapshot.ProjectileBurst.Enabled)
                    {
                        UnityEngine.Debug.LogError(
                            $"Stack detonation kind {snapshot.Kind} has no enabled projectile burst payload.");
                        return;
                    }

                    projectileExpansion.EventQueue.Enqueue(BuildProjectileDetonation(entry, position));
                    projectileExpansion.ProducerHandle =
                        JobHandle.CombineDependencies(projectileExpansion.ProducerHandle, Dependency);
                    return;
                default:
                    UnityEngine.Debug.LogError($"Unhandled stack detonation kind {snapshot.Kind}.");
                    return;
            }
        }

        private AoeSpawnEvent BuildAoeSpawnEvent(
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
                ProjectileBurst = default,
                AoeSpawn = detonation.AoeOnHitSpawn
            };
        }

        private ProjectileSpawnEvent BuildProjectileDetonation(
            in TargetStackEntry entry,
            float2 position)
        {
            DetonationSnapshot detonation = entry.Detonation;
            AoeProjectileBurstSnapshot burst = detonation.ProjectileBurst;
            int count = math.max(1, entry.SummedProjectileCount);
            float totalDamage = math.max(0f, entry.SummedDamage);

            // Stack contributions store nova total damage; the projectile payload is per projectile.
            var resolvedBurst = new AoeProjectileBurstSnapshot(
                burst.ProjectileTypeId,
                burst.TargetMask,
                count,
                burst.SpreadDegrees,
                burst.Speed,
                burst.LifetimeSeconds,
                burst.Radius,
                burst.HalfExtents,
                burst.RotationRadians,
                burst.ShapeType,
                new DamageSnapshot(totalDamage / count),
                burst.DirectDamageEnabled,
                burst.PierceCount,
                burst.RepeatHitCooldownSeconds,
                burst.VisualScale,
                burst.VisualRotationDegrees);

            return ProjectileSpawnPipeline.BuildBurstEvent(
                detonation.Faction,
                NextProjectileDetonationSourceId(),
                detonation.TypeId,
                0,
                position,
                position,
                resolvedBurst);
        }

        private int NextAoeId()
        {
            nextAoeId++;
            if (nextAoeId <= 0)
                nextAoeId = 1;
            return nextAoeId;
        }

        private int NextProjectileDetonationSourceId()
        {
            nextProjectileDetonationSourceId++;
            if (nextProjectileDetonationSourceId <= 0)
                nextProjectileDetonationSourceId = 1;
            return nextProjectileDetonationSourceId;
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
