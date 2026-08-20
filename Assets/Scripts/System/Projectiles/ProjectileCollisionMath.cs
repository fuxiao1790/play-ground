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

namespace PlayGround.System.Combat.Projectiles
{
    public static class ProjectileCollisionMath
    {
        public static void ComputeWorldBounds(
            float2 position,
            float radius,
            float2 halfExtents,
            float rotationRadians,
            CombatShapeType shapeType,
            out float2 min,
            out float2 max)
        {
            CombatCollisionMath.ComputeWorldBounds(
                position,
                radius,
                halfExtents,
                rotationRadians,
                shapeType,
                out min,
                out max);
        }

        public static bool BoundsIntersect(float2 leftMin, float2 leftMax, float2 rightMin, float2 rightMax)
        {
            return CombatCollisionMath.BoundsIntersect(leftMin, leftMax, rightMin, rightMax);
        }

        public static bool Hit(
            CombatKinematicsComponent projectileKinematics,
            CombatCollisionComponent projectileCollision,
            CombatTargetElement target)
        {
            return CombatCollisionMath.Hit(projectileKinematics, projectileCollision, target);
        }
    }
}
