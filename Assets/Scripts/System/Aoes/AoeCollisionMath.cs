using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Narrowphase;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Combat.Aoes
{
    public static class AoeCollisionMath
    {
        public static bool Hit(Vector2 aoePosition, AoeShape aoe, Vector2 targetPosition, AoeShape target)
        {
            return CombatCollisionMath.Hit(
                ToFloat2(aoePosition),
                aoe.Radius,
                ToFloat2(aoe.HalfExtents),
                aoe.RotationRadians,
                aoe.ShapeType,
                ToFloat2(targetPosition),
                target.Radius,
                ToFloat2(target.HalfExtents),
                target.RotationRadians,
                target.ShapeType);
        }

        public static Rect Bounds(Vector2 position, AoeShape shape)
        {
            CombatCollisionMath.ComputeWorldBounds(
                ToFloat2(position),
                shape.Radius,
                ToFloat2(shape.HalfExtents),
                shape.RotationRadians,
                shape.ShapeType,
                out float2 min,
                out float2 max);

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static float2 ToFloat2(Vector2 value)
        {
            return new float2(value.x, value.y);
        }
    }
}
