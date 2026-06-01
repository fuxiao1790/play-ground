using System.Collections.Generic;
using PlayGround.Common;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Attack
{
    public static class ProjectileVolleyBuilder
    {
        public readonly struct SpawnRequest
        {
            public SpawnRequest(Vector2 position, Vector2 velocity)
            {
                Position = position;
                Velocity = velocity;
            }

            public Vector2 Position { get; }
            public Vector2 Velocity { get; }
        }

        public static int Build(
            List<ProjectileSpawnCommand> commands,
            Vector2 origin,
            Vector2 aimDirection,
            int projectileCount,
            float spreadDegrees,
            float jitterDegrees,
            float speed,
            float lifetime,
            float radius,
            DamageSnapshot damage,
            CombatShapeType shapeType)
        {
            commands.Clear();
            projectileCount = Mathf.Max(1, projectileCount);
            Vector2 baseDirection = aimDirection.sqrMagnitude > 0f ? aimDirection.normalized : Vector2.right;
            float firstAngle = projectileCount == 1 ? 0f : -spreadDegrees * 0.5f;
            float step = projectileCount == 1 ? 0f : spreadDegrees / (projectileCount - 1);

            for (int i = 0; i < projectileCount; i++)
            {
                float angle = firstAngle + step * i;
                if (jitterDegrees > 0f)
                {
                    angle += Random.Range(-jitterDegrees, jitterDegrees);
                }

                Vector2 direction = Quaternion.Euler(0f, 0f, angle) * baseDirection;
                commands.Add(new ProjectileSpawnCommand(origin, direction, speed, lifetime, radius, damage, shapeType));
            }

            return commands.Count;
        }
    }
}
