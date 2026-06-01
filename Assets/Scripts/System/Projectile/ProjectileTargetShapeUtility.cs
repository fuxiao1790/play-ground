using PlayGround.System.Common;
using UnityEngine;

namespace PlayGround.System.Projectile
{
    public static class ProjectileTargetShapeUtility
    {
        public static bool IsSupportedShape(Collider2D collider)
        {
            return CombatTargetShapeUtility.IsSupportedShape(collider);
        }

        public static Vector2 Position(Collider2D collider, Transform fallback)
        {
            return CombatTargetShapeUtility.Position(collider, fallback);
        }

        public static CombatShapeType ShapeType(Collider2D collider)
        {
            return CombatTargetShapeUtility.ShapeType(collider);
        }

        public static float Radius(Collider2D collider)
        {
            return CombatTargetShapeUtility.Radius(collider);
        }

        public static float Radius(Collider2D collider, float fallbackRadius)
        {
            return CombatTargetShapeUtility.Radius(collider, fallbackRadius);
        }

        public static Vector2 HalfExtents(Collider2D collider)
        {
            return CombatTargetShapeUtility.HalfExtents(collider);
        }

        public static Vector2 HalfExtents(Collider2D collider, float fallbackRadius)
        {
            return CombatTargetShapeUtility.HalfExtents(collider, fallbackRadius);
        }

        public static float RotationRadians(Collider2D collider)
        {
            return CombatTargetShapeUtility.RotationRadians(collider);
        }
    }
}
