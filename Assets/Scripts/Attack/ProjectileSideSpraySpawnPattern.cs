using System.Collections.Generic;
using UnityEngine;

namespace PlayGround.Attack
{
    [CreateAssetMenu(menuName = "PlayGround/Attack/Projectile Side Spray Spawn Pattern")]
    public sealed class ProjectileSideSpraySpawnPattern : ProjectileChildSpawnPattern
    {
        [SerializeField, Range(0f, 180f)] private float sideSpreadDegrees = 30f;

        public override void Build(
            List<ProjectileVolleyBuilder.SpawnRequest> buffer,
            Vector2 parentPosition,
            Vector2 parentVelocity,
            int childCount,
            float childSpeed,
            int tickIndex)
        {
            buffer.Clear();

            int count = Mathf.Max(1, childCount);
            float speed = Mathf.Max(0f, childSpeed);
            Vector2 forward = parentVelocity.sqrMagnitude > 0f ? parentVelocity.normalized : Vector2.right;
            Vector2 left = Rotate(forward, -90f);
            Vector2 right = Rotate(forward, 90f);
            int leftCount = (count + 1) / 2;
            int rightCount = count / 2;
            int leftIndex = 0;
            int rightIndex = 0;
            float spread = Mathf.Max(0f, sideSpreadDegrees);

            for (int i = 0; i < count; i++)
            {
                if ((i % 2) == 0)
                {
                    AddChild(buffer, parentPosition, left, leftIndex++, leftCount, spread, speed);
                }
                else
                {
                    AddChild(buffer, parentPosition, right, rightIndex++, rightCount, spread, speed);
                }
            }
        }

        private static void AddChild(
            List<ProjectileVolleyBuilder.SpawnRequest> buffer,
            Vector2 position,
            Vector2 sideDirection,
            int sideIndex,
            int sideCount,
            float spreadDegrees,
            float speed)
        {
            float angle = SpreadAngle(spreadDegrees, sideIndex, sideCount);
            Vector2 direction = Rotate(sideDirection, angle);
            buffer.Add(new ProjectileVolleyBuilder.SpawnRequest(position, direction * speed));
        }

        private static float SpreadAngle(float totalSpreadDegrees, int shotIndex, int shotCount)
        {
            if (shotCount <= 1)
            {
                return 0f;
            }

            float startAngle = -totalSpreadDegrees * 0.5f;
            float angleStep = totalSpreadDegrees / (shotCount - 1);
            return startAngle + angleStep * shotIndex;
        }

        private static Vector2 Rotate(Vector2 vector, float degrees)
        {
            return Quaternion.Euler(0f, 0f, degrees) * vector;
        }
    }
}
