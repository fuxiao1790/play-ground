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

            bool appliedDeltas = false;
            while (state.Deltas.TryDequeue(out SpawnTemplateRefDelta delta))
            {
                NativeHashMap<Hash128, SpawnTemplateRefCount> counts = CountsFor(in state, delta.Kind);
                counts.TryGetValue(delta.Key, out SpawnTemplateRefCount entry);
                int instanceCount = entry.InstanceCount + delta.Delta;
                if (instanceCount < 0)
                {
#if UNITY_EDITOR
                    UnityEngine.Debug.Assert(false,
                        $"Spawn template instance count went negative for {delta.Kind}:{delta.Key}.");
#endif
                    instanceCount = 0;
                }

                entry.InstanceCount = instanceCount;
                counts[delta.Key] = entry;
                appliedDeltas = true;
            }

            if (appliedDeltas)
            {
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

        private static NativeHashMap<Hash128, SpawnTemplateRefCount> CountsFor(
            in SpawnTemplateRegistryState state,
            IntervalChildKind kind)
        {
            if (kind == IntervalChildKind.Targeted)
            {
                return state.TargetedCounts;
            }

            return SpawnTemplateRegistryKind.IsAoe(kind)
                ? state.AoeCounts
                : state.ProjectileCounts;
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
