using Unity.Entities;

namespace PlayGround.System.Common
{
    // ECS Lifecycle: scope component; added at root setup; written by the managed
    // routing binder; kept until root teardown.
    //
    // Routes internal follow-up spawns from a source combat scope to the
    // destination projectile and AOE scopes, fully in ECS. Replaces the managed
    // CombatSpawnRouter + the destination.TargetMask fallback.
    //
    // Mapping (mirrors the old CombatSpawnRouter.Bind):
    //   projectile scope -> AoeDestinationScope = own AOE scope, ProjectileDestinationScope = self
    //   AOE scope        -> ProjectileDestinationScope = own projectile scope
    // Target mask is intentionally absent: projectile/AOE collision filters by
    // which targets populate each scope's CombatTargetElement buffer (set at
    // registration), not by a per-spawn mask, so a mask here would be dead data.
    public struct CombatSpawnRouting : IComponentData
    {
        public Entity ProjectileDestinationScope;
        public Entity AoeDestinationScope;
    }
}
