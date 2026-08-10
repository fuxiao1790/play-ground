using PlayGround.System.Combat.Spawning;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.Tests.PlayMode
{
    internal static class SpawnTemplateRegistryTestState
    {
        public static SpawnTemplateRegistryState Add(EntityManager entityManager, Entity scope)
        {
            SpawnTemplateRegistryState state = new()
            {
                ProjectileCounts = new NativeHashMap<Hash128, SpawnTemplateRefCount>(16, Allocator.Persistent),
                AoeCounts = new NativeHashMap<Hash128, SpawnTemplateRefCount>(16, Allocator.Persistent),
                TargetedCounts = new NativeHashMap<Hash128, SpawnTemplateRefCount>(16, Allocator.Persistent),
                Deltas = new NativeQueue<SpawnTemplateRefDelta>(Allocator.Persistent)
            };
            entityManager.AddComponentData(scope, state);
            return state;
        }

        public static void Dispose(ref SpawnTemplateRegistryState state)
        {
            if (state.ProjectileCounts.IsCreated) state.ProjectileCounts.Dispose();
            if (state.AoeCounts.IsCreated) state.AoeCounts.Dispose();
            if (state.TargetedCounts.IsCreated) state.TargetedCounts.Dispose();
            if (state.Deltas.IsCreated) state.Deltas.Dispose();
            state = default;
        }
    }
}
