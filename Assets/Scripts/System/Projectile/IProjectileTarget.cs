using PlayGround.System.Common;
using UnityEngine;

namespace PlayGround.System.Projectile
{
    public enum ProjectileHitActorRole
    {
        Source = 0,
        Target = 1
    }

    public interface IProjectileHitActor
    {
        EntityId ProjectileHitNodeId { get; }
        void ReceiveProjectileHitPayload(in ProjectileHitPayload payload, in ProjectileHitContext context, ProjectileHitActorRole role);
    }

    public interface IProjectileTarget : ICombatTarget, IProjectileHitActor
    {
    }
}
