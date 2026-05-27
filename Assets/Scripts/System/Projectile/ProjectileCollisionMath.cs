using Unity.Mathematics;

namespace PlayGround.System.Projectile
{
    public static class ProjectileCollisionMath
    {
        public static bool Hit(ProjectileComponent projectile, ProjectileTargetElement target)
        {
            return (projectile.ShapeType, target.ShapeType) switch
            {
                (ProjectileShapeType.Circle, ProjectileShapeType.Circle) => CircleCircle(projectile, target),
                (ProjectileShapeType.Circle, ProjectileShapeType.Rectangle) => CircleRectangle(
                    projectile.Position,
                    projectile.Radius,
                    target.Position,
                    target.HalfExtents,
                    target.RotationRadians),
                (ProjectileShapeType.Rectangle, ProjectileShapeType.Circle) => CircleRectangle(
                    target.Position,
                    target.Radius,
                    projectile.Position,
                    projectile.HalfExtents,
                    projectile.RotationRadians),
                (ProjectileShapeType.Circle, ProjectileShapeType.Capsule) => CircleCapsule(
                    projectile.Position,
                    projectile.Radius,
                    target.Position,
                    target.Radius,
                    target.HalfExtents.x,
                    target.RotationRadians),
                (ProjectileShapeType.Capsule, ProjectileShapeType.Circle) => CircleCapsule(
                    target.Position,
                    target.Radius,
                    projectile.Position,
                    projectile.Radius,
                    projectile.HalfExtents.x,
                    projectile.RotationRadians),
                (ProjectileShapeType.Rectangle, ProjectileShapeType.Rectangle) => RectangleRectangle(projectile, target),
                (ProjectileShapeType.Rectangle, ProjectileShapeType.Capsule) => RectangleCapsule(projectile, target),
                (ProjectileShapeType.Capsule, ProjectileShapeType.Rectangle) => RectangleCapsule(target, projectile),
                (ProjectileShapeType.Capsule, ProjectileShapeType.Capsule) => CapsuleCapsule(projectile, target),
                _ => false
            };
        }

        private static bool CircleCircle(ProjectileComponent projectile, ProjectileTargetElement target)
        {
            float radius = projectile.Radius + target.Radius;
            return math.distancesq(projectile.Position, target.Position) <= radius * radius;
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

        private static bool CapsuleCapsule(ProjectileComponent left, ProjectileTargetElement right)
        {
            GetCapsuleSegment(left.Position, left.HalfExtents.x, left.RotationRadians, out float2 a0, out float2 a1);
            GetCapsuleSegment(right.Position, right.HalfExtents.x, right.RotationRadians, out float2 b0, out float2 b1);
            float radius = left.Radius + right.Radius;
            return DistanceSegmentSegmentSquared(a0, a1, b0, b1) <= radius * radius;
        }

        private static bool RectangleRectangle(ProjectileComponent left, ProjectileTargetElement right)
        {
            GetRectangleCorners(left.Position, left.HalfExtents, left.RotationRadians, out float2 a0, out float2 a1, out float2 a2, out float2 a3);
            GetRectangleCorners(right.Position, right.HalfExtents, right.RotationRadians, out float2 b0, out float2 b1, out float2 b2, out float2 b3);
            return OverlapsOnAxes(a0, a1, a2, a3, b0, b1, b2, b3)
                && OverlapsOnAxes(b0, b1, b2, b3, a0, a1, a2, a3);
        }

        private static bool RectangleCapsule(ProjectileComponent rectangle, ProjectileTargetElement capsule)
        {
            GetCapsuleSegment(capsule.Position, capsule.HalfExtents.x, capsule.RotationRadians, out float2 a, out float2 b);
            a = Rotate(a - rectangle.Position, -rectangle.RotationRadians);
            b = Rotate(b - rectangle.Position, -rectangle.RotationRadians);
            float distanceSquared = DistanceSegmentAabbSquared(a, b, -rectangle.HalfExtents, rectangle.HalfExtents);
            return distanceSquared <= capsule.Radius * capsule.Radius;
        }

        private static bool RectangleCapsule(ProjectileTargetElement rectangle, ProjectileComponent capsule)
        {
            GetCapsuleSegment(capsule.Position, capsule.HalfExtents.x, capsule.RotationRadians, out float2 a, out float2 b);
            a = Rotate(a - rectangle.Position, -rectangle.RotationRadians);
            b = Rotate(b - rectangle.Position, -rectangle.RotationRadians);
            float distanceSquared = DistanceSegmentAabbSquared(a, b, -rectangle.HalfExtents, rectangle.HalfExtents);
            return distanceSquared <= capsule.Radius * capsule.Radius;
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

        private static bool OverlapsOnAxes(float2 a0, float2 a1, float2 a2, float2 a3, float2 b0, float2 b1, float2 b2, float2 b3)
        {
            float2 axis0 = math.normalize(a1 - a0);
            float2 axis1 = math.normalize(a3 - a0);
            return OverlapsOnAxis(axis0, a0, a1, a2, a3, b0, b1, b2, b3)
                && OverlapsOnAxis(axis1, a0, a1, a2, a3, b0, b1, b2, b3);
        }

        private static bool OverlapsOnAxis(float2 axis, float2 a0, float2 a1, float2 a2, float2 a3, float2 b0, float2 b1, float2 b2, float2 b3)
        {
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
            if (lengthSquared <= ProjectileSimulationConstants.MinimumDirectionLengthSquared)
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

            return math.abs(d1) <= ProjectileSimulationConstants.MinimumDirectionLengthSquared && PointOnSegment(b0, a0, a1)
                || math.abs(d2) <= ProjectileSimulationConstants.MinimumDirectionLengthSquared && PointOnSegment(b1, a0, a1)
                || math.abs(d3) <= ProjectileSimulationConstants.MinimumDirectionLengthSquared && PointOnSegment(a0, b0, b1)
                || math.abs(d4) <= ProjectileSimulationConstants.MinimumDirectionLengthSquared && PointOnSegment(a1, b0, b1);
        }

        private static bool PointOnSegment(float2 point, float2 a, float2 b)
        {
            return point.x >= math.min(a.x, b.x) - ProjectileSimulationConstants.MinimumDirectionLengthSquared
                && point.x <= math.max(a.x, b.x) + ProjectileSimulationConstants.MinimumDirectionLengthSquared
                && point.y >= math.min(a.y, b.y) - ProjectileSimulationConstants.MinimumDirectionLengthSquared
                && point.y <= math.max(a.y, b.y) + ProjectileSimulationConstants.MinimumDirectionLengthSquared;
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
