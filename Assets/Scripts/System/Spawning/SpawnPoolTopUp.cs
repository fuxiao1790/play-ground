using PlayGround.System.Combat.Lifetime;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Combat.Spawning
{
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
            int demand)
        {
            int have = disabledSlotQuery.CalculateEntityCount();
            int deficit = demand - have;
            if (deficit <= 0)
            {
                return 0;
            }

            NativeArray<Entity> created =
                entityManager.CreateEntity(archetype, deficit, Allocator.Temp);
            for (int i = 0; i < created.Length; i++)
            {
                entityManager.SetComponentEnabled<Active>(created[i], false);
            }

            created.Dispose();
            return deficit;
        }
    }
}
