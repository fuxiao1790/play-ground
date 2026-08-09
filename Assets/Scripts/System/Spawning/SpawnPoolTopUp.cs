using PlayGround.System.Combat.Lifetime;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;

namespace PlayGround.System.Combat.Spawning
{
    // ECS Lifecycle: enabled only on fresh cold slots so SpawnPoolTopUp can disable Active
    // at query granularity, then disabled in the same call to consume the creation marker.
    internal struct ColdSlotTag : IComponentData, IEnableableComponent
    {
    }

    internal static class SpawnPoolTopUp
    {
        /// <summary>
        /// Ensures at least <paramref name="demand"/> disabled-Active slots exist in
        /// <paramref name="archetype"/>. Must run before callers fetch chunk arrays or
        /// component type handles because entity creation is structural.
        /// </summary>
        public static int EnsureDisabledSlots(
            EntityManager entityManager,
            EntityArchetype archetype,
            EntityQuery disabledSlotQuery,
            EntityQuery activeColdSlotQuery,
            EntityQuery coldSlotMarkerQuery,
            int demand,
            ProfilerMarker createSlotsMarker)
        {
            int have = disabledSlotQuery.CalculateEntityCount();
            int deficit = demand - have;
            if (deficit <= 0)
            {
                return 0;
            }

            using (createSlotsMarker.Auto())
            {
                entityManager.CreateEntity(archetype, deficit, Allocator.Temp).Dispose();
                entityManager.SetComponentEnabled<Active>(activeColdSlotQuery, false);
                entityManager.SetComponentEnabled<ColdSlotTag>(coldSlotMarkerQuery, false);
            }
            return deficit;
        }
    }
}
