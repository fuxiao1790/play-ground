using PlayGround.System.Combat.Lifetime;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Combat.Spawning
{
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    [UpdateAfter(typeof(CombatPoolCleanupSystem))]
    public sealed partial class SpawnTemplateRefCountSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            CompleteDependency();

            RefRW<SpawnTemplateRegistryState> stateRef = SystemAPI.GetSingletonRW<SpawnTemplateRegistryState>();
            ref SpawnTemplateRegistryState state = ref stateRef.ValueRW;
            ProjectileSpawnTemplate projectileTemplates = SystemAPI.GetSingleton<ProjectileSpawnTemplate>();
            AoeSpawnTemplate aoeTemplates = SystemAPI.GetSingleton<AoeSpawnTemplate>();
            TargetedSpawnTemplate targetedTemplates = SystemAPI.GetSingleton<TargetedSpawnTemplate>();

            int deltaCapacity = state.Deltas.Count;
            bool appliedDeltas = deltaCapacity > 0;
            if (appliedDeltas)
            {
                using var projectileDeltas =
                    new NativeHashMap<Hash128, int>(deltaCapacity, Allocator.Temp);
                using var aoeDeltas =
                    new NativeHashMap<Hash128, int>(deltaCapacity, Allocator.Temp);
                using var targetedDeltas =
                    new NativeHashMap<Hash128, int>(deltaCapacity, Allocator.Temp);

                while (state.Deltas.TryDequeue(out SpawnTemplateRefDelta delta))
                {
                    Accumulate(
                        DeltaTotalsFor(delta.Kind, projectileDeltas, aoeDeltas, targetedDeltas),
                        delta);
                }

                ApplyDeltas(state.ProjectileCounts, projectileDeltas, IntervalChildKind.Projectile);
                ApplyDeltas(state.AoeCounts, aoeDeltas, IntervalChildKind.ImpactAoe);
                ApplyDeltas(state.TargetedCounts, targetedDeltas, IntervalChildKind.Targeted);
                state.IsDirty = true;
            }

            if (!state.IsDirty)
            {
                return;
            }

            Reclaim(state.ProjectileCounts, projectileTemplates.Map);
            Reclaim(state.AoeCounts, aoeTemplates.Map);
            Reclaim(state.TargetedCounts, targetedTemplates.Map);
            state.IsDirty = false;
        }

        private static NativeHashMap<Hash128, int> DeltaTotalsFor(
            IntervalChildKind kind,
            NativeHashMap<Hash128, int> projectileDeltas,
            NativeHashMap<Hash128, int> aoeDeltas,
            NativeHashMap<Hash128, int> targetedDeltas)
        {
            if (kind == IntervalChildKind.Targeted)
            {
                return targetedDeltas;
            }

            return SpawnTemplateRegistryKind.IsAoe(kind)
                ? aoeDeltas
                : projectileDeltas;
        }

        private static void Accumulate(
            NativeHashMap<Hash128, int> totals,
            in SpawnTemplateRefDelta delta)
        {
            totals.TryGetValue(delta.Key, out int total);
            totals[delta.Key] = total + delta.Delta;
        }

        private static void ApplyDeltas(
            NativeHashMap<Hash128, SpawnTemplateRefCount> counts,
            NativeHashMap<Hash128, int> totals,
            IntervalChildKind kind)
        {
            using NativeArray<Hash128> keys = totals.GetKeyArray(Allocator.Temp);
            for (int i = 0; i < keys.Length; i++)
            {
                Hash128 key = keys[i];
                counts.TryGetValue(key, out SpawnTemplateRefCount entry);
                int instanceCount = entry.InstanceCount + totals[key];
                if (instanceCount < 0)
                {
#if UNITY_EDITOR
                    UnityEngine.Debug.Assert(false,
                        $"Spawn template instance count went negative for {kind}:{key}.");
#endif
                    instanceCount = 0;
                }

                entry.InstanceCount = instanceCount;
                counts[key] = entry;
            }
        }

        private static void Reclaim<TTemplate>(
            NativeHashMap<Hash128, SpawnTemplateRefCount> counts,
            NativeHashMap<Hash128, TTemplate> templates)
            where TTemplate : unmanaged
        {
            using NativeArray<Hash128> keys = counts.GetKeyArray(Allocator.Temp);
            for (int i = 0; i < keys.Length; i++)
            {
                Hash128 key = keys[i];
                if (!counts.TryGetValue(key, out SpawnTemplateRefCount entry) || !entry.Reclaimable)
                {
                    continue;
                }

                counts.Remove(key);
                templates.Remove(key);
            }
        }
    }
}
