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

    public interface IProjectileTarget : IProjectileHitActor
    {
        int TargetId { get; }
        Vector2 ProjectileTargetPosition { get; }
        float ProjectileTargetRadius { get; }
        Vector2 ProjectileTargetHalfExtents { get; }
        float ProjectileTargetRotationRadians { get; }
        ProjectileShapeType ProjectileTargetShapeType { get; }
        int ProjectileTargetMask { get; }
        bool IsProjectileTargetActive { get; }
    }
}
