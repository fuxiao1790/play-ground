using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using UnityEngine;

namespace PlayGround.System.Combat.Projectiles
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
