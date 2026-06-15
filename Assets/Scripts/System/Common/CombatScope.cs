using Unity.Entities;

namespace PlayGround.System.Common
{
    // ECS Lifecycle: unified scope tag; added at CombatRoot setup; kept until root
    // teardown. One scope per faction serves both the projectile and AOE domains
    // (shared target list + damage buffer + per-domain spawn request buffers),
    // replacing the separate ProjectileScope / AoeScope tags.
    public struct CombatScope : IComponentData
    {
    }
}
