using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.System.Aoe
{
    public static class AoeCollisionMath
    {
        private const float Epsilon = 0.0001f;

        public static bool Hit(Vector2 aoePosition, AoeShape aoe, Vector2 targetPosition, AoeShape target)
        {
            return (aoe.ShapeType, target.ShapeType) switch
            {
                (ProjectileShapeType.Circle, ProjectileShapeType.Circle) => CircleCircle(aoePosition, aoe.Radius, targetPosition, target.Radius),
                (ProjectileShapeType.Circle, ProjectileShapeType.Rectangle) => CircleRectangle(aoePosition, aoe.Radius, targetPosition, target.HalfExtents, target.RotationRadians),
                (ProjectileShapeType.Rectangle, ProjectileShapeType.Circle) => CircleRectangle(targetPosition, target.Radius, aoePosition, aoe.HalfExtents, aoe.RotationRadians),
                (ProjectileShapeType.Rectangle, ProjectileShapeType.Rectangle) => RectangleRectangle(aoePosition, aoe.HalfExtents, aoe.RotationRadians, targetPosition, target.HalfExtents, target.RotationRadians),
                _ => CircleCircle(aoePosition, BoundingRadius(aoe), targetPosition, BoundingRadius(target))
            };
        }

        public static Rect Bounds(Vector2 position, AoeShape shape)
        {
            float radius = BoundingRadius(shape);
            return new Rect(position.x - radius, position.y - radius, radius * 2f, radius * 2f);
        }

        private static float BoundingRadius(AoeShape shape)
        {
            return shape.ShapeType switch
            {
                ProjectileShapeType.Rectangle => shape.HalfExtents.magnitude,
                ProjectileShapeType.Capsule => shape.HalfExtents.x + shape.Radius,
                _ => shape.Radius
            };
        }

        private static bool CircleCircle(Vector2 leftCenter, float leftRadius, Vector2 rightCenter, float rightRadius)
        {
            float radius = leftRadius + rightRadius;
            return (leftCenter - rightCenter).sqrMagnitude <= radius * radius;
        }

        private static bool CircleRectangle(Vector2 circleCenter, float circleRadius, Vector2 rectangleCenter, Vector2 halfExtents, float rotationRadians)
        {
            Vector2 local = Rotate(circleCenter - rectangleCenter, -rotationRadians);
            Vector2 closest = new(
                Mathf.Clamp(local.x, -halfExtents.x, halfExtents.x),
                Mathf.Clamp(local.y, -halfExtents.y, halfExtents.y));
            return (local - closest).sqrMagnitude <= circleRadius * circleRadius;
        }

        private static bool RectangleRectangle(
            Vector2 leftCenter,
            Vector2 leftHalfExtents,
            float leftRotationRadians,
            Vector2 rightCenter,
            Vector2 rightHalfExtents,
            float rightRotationRadians)
        {
            GetRectangleCorners(leftCenter, leftHalfExtents, leftRotationRadians, out Vector2 a0, out Vector2 a1, out Vector2 a2, out Vector2 a3);
            GetRectangleCorners(rightCenter, rightHalfExtents, rightRotationRadians, out Vector2 b0, out Vector2 b1, out Vector2 b2, out Vector2 b3);
            return OverlapsOnAxes(a0, a1, a2, a3, b0, b1, b2, b3)
                && OverlapsOnAxes(b0, b1, b2, b3, a0, a1, a2, a3);
        }

        private static void GetRectangleCorners(Vector2 center, Vector2 halfExtents, float rotationRadians, out Vector2 c0, out Vector2 c1, out Vector2 c2, out Vector2 c3)
        {
            Vector2 xAxis = Rotate(Vector2.right, rotationRadians);
            Vector2 yAxis = Rotate(Vector2.up, rotationRadians);
            Vector2 xHalf = xAxis * halfExtents.x;
            Vector2 yHalf = yAxis * halfExtents.y;
            c0 = center - xHalf - yHalf;
            c1 = center + xHalf - yHalf;
            c2 = center + xHalf + yHalf;
            c3 = center - xHalf + yHalf;
        }

        private static bool OverlapsOnAxes(Vector2 a0, Vector2 a1, Vector2 a2, Vector2 a3, Vector2 b0, Vector2 b1, Vector2 b2, Vector2 b3)
        {
            Vector2 axis0 = (a1 - a0).normalized;
            Vector2 axis1 = (a3 - a0).normalized;
            return OverlapsOnAxis(axis0, a0, a1, a2, a3, b0, b1, b2, b3)
                && OverlapsOnAxis(axis1, a0, a1, a2, a3, b0, b1, b2, b3);
        }

        private static bool OverlapsOnAxis(Vector2 axis, Vector2 a0, Vector2 a1, Vector2 a2, Vector2 a3, Vector2 b0, Vector2 b1, Vector2 b2, Vector2 b3)
        {
            if (axis.sqrMagnitude <= Epsilon)
            {
                return true;
            }

            Project(axis, a0, a1, a2, a3, out float aMin, out float aMax);
            Project(axis, b0, b1, b2, b3, out float bMin, out float bMax);
            return aMax >= bMin && bMax >= aMin;
        }

        private static void Project(Vector2 axis, Vector2 c0, Vector2 c1, Vector2 c2, Vector2 c3, out float min, out float max)
        {
            float p0 = Vector2.Dot(c0, axis);
            float p1 = Vector2.Dot(c1, axis);
            float p2 = Vector2.Dot(c2, axis);
            float p3 = Vector2.Dot(c3, axis);
            min = Mathf.Min(Mathf.Min(p0, p1), Mathf.Min(p2, p3));
            max = Mathf.Max(Mathf.Max(p0, p1), Mathf.Max(p2, p3));
        }

        private static Vector2 Rotate(Vector2 vector, float radians)
        {
            float sine = Mathf.Sin(radians);
            float cosine = Mathf.Cos(radians);
            return new Vector2(
                (vector.x * cosine) - (vector.y * sine),
                (vector.x * sine) + (vector.y * cosine));
        }
    }
}
