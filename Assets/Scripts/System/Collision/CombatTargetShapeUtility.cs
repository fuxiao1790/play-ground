using UnityEngine;

namespace PlayGround.System.Common
{
    public static class CombatTargetShapeUtility
    {
        public static bool IsSupportedShape(Collider2D collider)
        {
            return collider is CircleCollider2D || collider is BoxCollider2D || collider is CapsuleCollider2D;
        }

        public static Vector2 Position(Collider2D collider, Transform fallback)
        {
            if (collider == null)
            {
                return fallback.position;
            }

            if (collider is CircleCollider2D circle)
            {
                return circle.transform.TransformPoint(circle.offset);
            }

            if (collider is BoxCollider2D box)
            {
                return box.transform.TransformPoint(box.offset);
            }

            if (collider is CapsuleCollider2D capsule)
            {
                return capsule.transform.TransformPoint(capsule.offset);
            }

            return collider.bounds.center;
        }

        public static CombatShapeType ShapeType(Collider2D collider)
        {
            return collider switch
            {
                CircleCollider2D => CombatShapeType.Circle,
                BoxCollider2D => CombatShapeType.Rectangle,
                CapsuleCollider2D => CombatShapeType.Capsule,
                null => CombatShapeType.Circle,
                _ => throw new global::System.ArgumentException($"Unsupported combat shape collider type {collider.GetType().Name}.", nameof(collider))
            };
        }

        public static float Radius(Collider2D collider)
        {
            return Radius(collider, 0f);
        }

        public static float Radius(Collider2D collider, float fallbackRadius)
        {
            if (collider is CircleCollider2D circle)
            {
                Vector3 scale = circle.transform.lossyScale;
                return circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            }

            if (collider is CapsuleCollider2D capsule)
            {
                Vector2 size = ScaledSize(capsule.size, capsule.transform.lossyScale);
                return capsule.direction == CapsuleDirection2D.Vertical
                    ? size.x * 0.5f
                    : size.y * 0.5f;
            }

            return Mathf.Max(0f, fallbackRadius);
        }

        public static Vector2 HalfExtents(Collider2D collider)
        {
            return HalfExtents(collider, 0f);
        }

        public static Vector2 HalfExtents(Collider2D collider, float fallbackRadius)
        {
            if (collider is BoxCollider2D box)
            {
                return ScaledSize(box.size, box.transform.lossyScale) * 0.5f;
            }

            if (collider is CapsuleCollider2D capsule)
            {
                Vector2 size = ScaledSize(capsule.size, capsule.transform.lossyScale);
                float radius = Radius(capsule, fallbackRadius);
                float halfSegment = capsule.direction == CapsuleDirection2D.Vertical
                    ? Mathf.Max(0f, (size.y * 0.5f) - radius)
                    : Mathf.Max(0f, (size.x * 0.5f) - radius);
                return new Vector2(halfSegment, radius);
            }

            float radiusFallback = Radius(collider, fallbackRadius);
            return new Vector2(radiusFallback, radiusFallback);
        }

        public static float RotationRadians(Collider2D collider)
        {
            if (collider == null)
            {
                return 0f;
            }

            float rotationDegrees = collider.transform.eulerAngles.z;
            if (collider is CapsuleCollider2D capsule && capsule.direction == CapsuleDirection2D.Horizontal)
            {
                rotationDegrees -= 90f;
            }

            return rotationDegrees * Mathf.Deg2Rad;
        }

        private static Vector2 ScaledSize(Vector2 size, Vector3 scale)
        {
            return new Vector2(size.x * Mathf.Abs(scale.x), size.y * Mathf.Abs(scale.y));
        }
    }
}
