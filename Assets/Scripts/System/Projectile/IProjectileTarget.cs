using PlayGround.Common;
using UnityEngine;

namespace PlayGround.System.Projectile
{
    public interface IProjectileTarget
    {
        int TargetId { get; }
        Vector2 ProjectileTargetPosition { get; }
        float ProjectileTargetRadius { get; }
        Vector2 ProjectileTargetHalfExtents { get; }
        float ProjectileTargetRotationRadians { get; }
        ProjectileShapeType ProjectileTargetShapeType { get; }
        int ProjectileTargetMask { get; }
        bool IsProjectileTargetActive { get; }
        void ReceiveProjectileHit(DamageSnapshot damage);
    }
}
