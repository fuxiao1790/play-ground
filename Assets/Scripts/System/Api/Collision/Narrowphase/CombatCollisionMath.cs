using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Collision.Narrowphase
{
    public static class CombatCollisionMath
    {
        private const float Epsilon = 0.000001f;

        public static void ComputeWorldBounds(
            float2 position,
            float radius,
            float2 halfExtents,
            float rotationRadians,
            CombatShapeType shapeType,
            out float2 min,
            out float2 max)
        {
            switch (shapeType)
            {
                case CombatShapeType.Rectangle:
                    RectangleBounds(position, halfExtents, rotationRadians, out min, out max);
                    return;
                case CombatShapeType.Capsule:
                    CapsuleBounds(position, radius, halfExtents.x, rotationRadians, out min, out max);
                    return;
                default:
                    float2 radiusVector = new(radius, radius);
                    min = position - radiusVector;
                    max = position + radiusVector;
                    return;
            }
        }

        public static bool BoundsIntersect(float2 leftMin, float2 leftMax, float2 rightMin, float2 rightMax)
        {
            return leftMax.x >= rightMin.x
                && rightMax.x >= leftMin.x
                && leftMax.y >= rightMin.y
                && rightMax.y >= leftMin.y;
        }

        public static bool Hit(
            CombatKinematicsComponent kinematics,
            CombatCollisionComponent collision,
            CombatTargetElement target)
        {
            return Hit(
                kinematics.Position,
                collision.Radius,
                collision.HalfExtents,
                collision.RotationRadians,
                collision.ShapeType,
                target.Position,
                target.Radius,
                target.HalfExtents,
                target.RotationRadians,
                target.ShapeType);
        }

        public static bool Hit(
            float2 leftPosition,
            float leftRadius,
            float2 leftHalfExtents,
            float leftRotationRadians,
            CombatShapeType leftShapeType,
            float2 rightPosition,
            float rightRadius,
            float2 rightHalfExtents,
            float rightRotationRadians,
            CombatShapeType rightShapeType)
        {
            return (leftShapeType, rightShapeType) switch
            {
                (CombatShapeType.Circle, CombatShapeType.Circle) => CircleCircle(leftPosition, leftRadius, rightPosition, rightRadius),
                (CombatShapeType.Circle, CombatShapeType.Rectangle) => CircleRectangle(leftPosition, leftRadius, rightPosition, rightHalfExtents, rightRotationRadians),
                (CombatShapeType.Rectangle, CombatShapeType.Circle) => CircleRectangle(rightPosition, rightRadius, leftPosition, leftHalfExtents, leftRotationRadians),
                (CombatShapeType.Circle, CombatShapeType.Capsule) => CircleCapsule(leftPosition, leftRadius, rightPosition, rightRadius, rightHalfExtents.x, rightRotationRadians),
                (CombatShapeType.Capsule, CombatShapeType.Circle) => CircleCapsule(rightPosition, rightRadius, leftPosition, leftRadius, leftHalfExtents.x, leftRotationRadians),
                (CombatShapeType.Rectangle, CombatShapeType.Rectangle) => RectangleRectangle(leftPosition, leftHalfExtents, leftRotationRadians, rightPosition, rightHalfExtents, rightRotationRadians),
                (CombatShapeType.Rectangle, CombatShapeType.Capsule) => RectangleCapsule(leftPosition, leftHalfExtents, leftRotationRadians, rightPosition, rightRadius, rightHalfExtents.x, rightRotationRadians),
                (CombatShapeType.Capsule, CombatShapeType.Rectangle) => RectangleCapsule(rightPosition, rightHalfExtents, rightRotationRadians, leftPosition, leftRadius, leftHalfExtents.x, leftRotationRadians),
                (CombatShapeType.Capsule, CombatShapeType.Capsule) => CapsuleCapsule(leftPosition, leftRadius, leftHalfExtents.x, leftRotationRadians, rightPosition, rightRadius, rightHalfExtents.x, rightRotationRadians),
                _ => false
            };
        }

        public static float BoundingRadius(float radius, float2 halfExtents, CombatShapeType shapeType)
        {
            return shapeType switch
            {
                CombatShapeType.Rectangle => math.length(halfExtents),
                CombatShapeType.Capsule => halfExtents.x + radius,
                _ => radius
            };
        }

        private static bool CircleCircle(float2 leftCenter, float leftRadius, float2 rightCenter, float rightRadius)
        {
            float radius = leftRadius + rightRadius;
            return math.distancesq(leftCenter, rightCenter) <= radius * radius;
        }

        private static bool CircleRectangle(float2 circleCenter, float circleRadius, float2 rectangleCenter, float2 halfExtents, float rotationRadians)
        {
            float2 local = Rotate(circleCenter - rectangleCenter, -rotationRadians);
            float2 closest = math.clamp(local, -halfExtents, halfExtents);
            return math.lengthsq(local - closest) <= circleRadius * circleRadius;
        }

        private static bool CircleCapsule(float2 circleCenter, float circleRadius, float2 capsuleCenter, float capsuleRadius, float halfSegment, float rotationRadians)
        {
            GetCapsuleSegment(capsuleCenter, halfSegment, rotationRadians, out float2 a, out float2 b);
            float radius = circleRadius + capsuleRadius;
            return DistancePointSegmentSquared(circleCenter, a, b) <= radius * radius;
        }

        private static bool CapsuleCapsule(
            float2 leftCenter,
            float leftRadius,
            float leftHalfSegment,
            float leftRotationRadians,
            float2 rightCenter,
            float rightRadius,
            float rightHalfSegment,
            float rightRotationRadians)
        {
            GetCapsuleSegment(leftCenter, leftHalfSegment, leftRotationRadians, out float2 a0, out float2 a1);
            GetCapsuleSegment(rightCenter, rightHalfSegment, rightRotationRadians, out float2 b0, out float2 b1);
            float radius = leftRadius + rightRadius;
            return DistanceSegmentSegmentSquared(a0, a1, b0, b1) <= radius * radius;
        }

        private static bool RectangleRectangle(
            float2 leftCenter,
            float2 leftHalfExtents,
            float leftRotationRadians,
            float2 rightCenter,
            float2 rightHalfExtents,
            float rightRotationRadians)
        {
            GetRectangleCorners(leftCenter, leftHalfExtents, leftRotationRadians, out float2 a0, out float2 a1, out float2 a2, out float2 a3);
            GetRectangleCorners(rightCenter, rightHalfExtents, rightRotationRadians, out float2 b0, out float2 b1, out float2 b2, out float2 b3);
            return OverlapsOnAxes(a0, a1, a2, a3, b0, b1, b2, b3)
                && OverlapsOnAxes(b0, b1, b2, b3, a0, a1, a2, a3);
        }

        private static bool RectangleCapsule(
            float2 rectangleCenter,
            float2 rectangleHalfExtents,
            float rectangleRotationRadians,
            float2 capsuleCenter,
            float capsuleRadius,
            float capsuleHalfSegment,
            float capsuleRotationRadians)
        {
            GetCapsuleSegment(capsuleCenter, capsuleHalfSegment, capsuleRotationRadians, out float2 a, out float2 b);
            a = Rotate(a - rectangleCenter, -rectangleRotationRadians);
            b = Rotate(b - rectangleCenter, -rectangleRotationRadians);
            float distanceSquared = DistanceSegmentAabbSquared(a, b, -rectangleHalfExtents, rectangleHalfExtents);
            return distanceSquared <= capsuleRadius * capsuleRadius;
        }

        private static void GetCapsuleSegment(float2 center, float halfSegment, float rotationRadians, out float2 a, out float2 b)
        {
            float2 axis = Rotate(new float2(0f, 1f), rotationRadians);
            a = center - axis * halfSegment;
            b = center + axis * halfSegment;
        }

        private static void GetRectangleCorners(float2 center, float2 halfExtents, float rotationRadians, out float2 c0, out float2 c1, out float2 c2, out float2 c3)
        {
            float2 xAxis = Rotate(new float2(1f, 0f), rotationRadians);
            float2 yAxis = Rotate(new float2(0f, 1f), rotationRadians);
            float2 xHalf = xAxis * halfExtents.x;
            float2 yHalf = yAxis * halfExtents.y;
            c0 = center - xHalf - yHalf;
            c1 = center + xHalf - yHalf;
            c2 = center + xHalf + yHalf;
            c3 = center - xHalf + yHalf;
        }

        private static void RectangleBounds(float2 center, float2 halfExtents, float rotationRadians, out float2 min, out float2 max)
        {
            float2 xAxis = Rotate(new float2(1f, 0f), rotationRadians);
            float2 yAxis = Rotate(new float2(0f, 1f), rotationRadians);
            float2 half = new(
                (math.abs(xAxis.x) * halfExtents.x) + (math.abs(yAxis.x) * halfExtents.y),
                (math.abs(xAxis.y) * halfExtents.x) + (math.abs(yAxis.y) * halfExtents.y));
            min = center - half;
            max = center + half;
        }

        private static void CapsuleBounds(float2 center, float radius, float halfSegment, float rotationRadians, out float2 min, out float2 max)
        {
            GetCapsuleSegment(center, halfSegment, rotationRadians, out float2 a, out float2 b);
            float2 radiusVector = new(radius, radius);
            min = math.min(a, b) - radiusVector;
            max = math.max(a, b) + radiusVector;
        }

        private static bool OverlapsOnAxes(float2 a0, float2 a1, float2 a2, float2 a3, float2 b0, float2 b1, float2 b2, float2 b3)
        {
            float2 axis0 = a1 - a0;
            float2 axis1 = a3 - a0;
            return OverlapsOnAxis(axis0, a0, a1, a2, a3, b0, b1, b2, b3)
                && OverlapsOnAxis(axis1, a0, a1, a2, a3, b0, b1, b2, b3);
        }

        private static bool OverlapsOnAxis(float2 axis, float2 a0, float2 a1, float2 a2, float2 a3, float2 b0, float2 b1, float2 b2, float2 b3)
        {
            if (math.lengthsq(axis) <= Epsilon)
            {
                return true;
            }

            axis = math.normalize(axis);
            ProjectOntoAxis(axis, a0, a1, a2, a3, out float aMin, out float aMax);
            ProjectOntoAxis(axis, b0, b1, b2, b3, out float bMin, out float bMax);
            return aMax >= bMin && bMax >= aMin;
        }

        private static void ProjectOntoAxis(float2 axis, float2 c0, float2 c1, float2 c2, float2 c3, out float min, out float max)
        {
            float p0 = math.dot(c0, axis);
            float p1 = math.dot(c1, axis);
            float p2 = math.dot(c2, axis);
            float p3 = math.dot(c3, axis);
            min = math.min(math.min(p0, p1), math.min(p2, p3));
            max = math.max(math.max(p0, p1), math.max(p2, p3));
        }

        private static float DistanceSegmentAabbSquared(float2 a, float2 b, float2 min, float2 max)
        {
            if (SegmentIntersectsAabb(a, b, min, max))
            {
                return 0f;
            }

            float distanceSquared = float.PositiveInfinity;
            distanceSquared = math.min(distanceSquared, DistancePointAabbSquared(a, min, max));
            distanceSquared = math.min(distanceSquared, DistancePointAabbSquared(b, min, max));
            distanceSquared = math.min(distanceSquared, DistancePointSegmentSquared(new float2(min.x, min.y), a, b));
            distanceSquared = math.min(distanceSquared, DistancePointSegmentSquared(new float2(max.x, min.y), a, b));
            distanceSquared = math.min(distanceSquared, DistancePointSegmentSquared(new float2(max.x, max.y), a, b));
            distanceSquared = math.min(distanceSquared, DistancePointSegmentSquared(new float2(min.x, max.y), a, b));
            return distanceSquared;
        }

        private static float DistancePointAabbSquared(float2 point, float2 min, float2 max)
        {
            float2 closest = math.clamp(point, min, max);
            return math.lengthsq(point - closest);
        }

        private static float DistanceSegmentSegmentSquared(float2 a0, float2 a1, float2 b0, float2 b1)
        {
            if (SegmentsIntersect(a0, a1, b0, b1))
            {
                return 0f;
            }

            float d1 = DistancePointSegmentSquared(a0, b0, b1);
            float d2 = DistancePointSegmentSquared(a1, b0, b1);
            float d3 = DistancePointSegmentSquared(b0, a0, a1);
            float d4 = DistancePointSegmentSquared(b1, a0, a1);
            return math.min(math.min(d1, d2), math.min(d3, d4));
        }

        private static float DistancePointSegmentSquared(float2 point, float2 a, float2 b)
        {
            float2 ab = b - a;
            float lengthSquared = math.lengthsq(ab);
            if (lengthSquared <= Epsilon)
            {
                return math.lengthsq(point - a);
            }

            float t = math.clamp(math.dot(point - a, ab) / lengthSquared, 0f, 1f);
            float2 closest = a + ab * t;
            return math.lengthsq(point - closest);
        }

        private static bool SegmentIntersectsAabb(float2 a, float2 b, float2 min, float2 max)
        {
            if (PointInAabb(a, min, max) || PointInAabb(b, min, max))
            {
                return true;
            }

            float2 c0 = new(min.x, min.y);
            float2 c1 = new(max.x, min.y);
            float2 c2 = new(max.x, max.y);
            float2 c3 = new(min.x, max.y);
            return SegmentsIntersect(a, b, c0, c1)
                || SegmentsIntersect(a, b, c1, c2)
                || SegmentsIntersect(a, b, c2, c3)
                || SegmentsIntersect(a, b, c3, c0);
        }

        private static bool PointInAabb(float2 point, float2 min, float2 max)
        {
            return point.x >= min.x && point.x <= max.x && point.y >= min.y && point.y <= max.y;
        }

        private static bool SegmentsIntersect(float2 a0, float2 a1, float2 b0, float2 b1)
        {
            float d1 = Cross(a1 - a0, b0 - a0);
            float d2 = Cross(a1 - a0, b1 - a0);
            float d3 = Cross(b1 - b0, a0 - b0);
            float d4 = Cross(b1 - b0, a1 - b0);
            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f))
                && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f)))
            {
                return true;
            }

            return math.abs(d1) <= Epsilon && PointOnSegment(b0, a0, a1)
                || math.abs(d2) <= Epsilon && PointOnSegment(b1, a0, a1)
                || math.abs(d3) <= Epsilon && PointOnSegment(a0, b0, b1)
                || math.abs(d4) <= Epsilon && PointOnSegment(a1, b0, b1);
        }

        private static bool PointOnSegment(float2 point, float2 a, float2 b)
        {
            return point.x >= math.min(a.x, b.x) - Epsilon
                && point.x <= math.max(a.x, b.x) + Epsilon
                && point.y >= math.min(a.y, b.y) - Epsilon
                && point.y <= math.max(a.y, b.y) + Epsilon;
        }

        private static float2 Rotate(float2 vector, float radians)
        {
            math.sincos(radians, out float sine, out float cosine);
            return new float2(
                (vector.x * cosine) - (vector.y * sine),
                (vector.x * sine) + (vector.y * cosine));
        }

        private static float Cross(float2 left, float2 right)
        {
            return (left.x * right.y) - (left.y * right.x);
        }
    }
}
