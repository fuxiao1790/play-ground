namespace PlayGround.System.Stats
{
    // ECS Lifecycle: singleton data; created once by CombatStatsGatherSystem.OnCreate;
    // overwritten each frame by that system; never removed (dies with the world).
    public struct CombatStatsSingleton : Unity.Entities.IComponentData
    {
        public int EntitiesSpawnedViaEcb;    // cold create (new archetype entity)
        public int EntitiesSpawnedViaReuse;  // claimed a disabled Active slot
        public int HitEventsCreated;         // CombatHitEvents finalized this frame
        public int VfxEventsCreated;         // VfxPendingSpawns dispatched this frame
    }

    // ECS Lifecycle: managed singleton binding; created with the singleton entity;
    // Display is set/cleared by CombatStatsDisplay via CombatStatsGatherSystem.Bind/Unbind.
    public sealed class CombatStatsBinding : Unity.Entities.IComponentData
    {
        public CombatStatsDisplay Display;
    }
}
