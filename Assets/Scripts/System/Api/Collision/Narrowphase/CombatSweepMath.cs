using PlayGround.System.Combat.Collision;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Collision.Narrowphase
{
    public static class CombatSweepMath
    {
        private const float Epsilon = 0.000001f;

        public static float SupportExtent(
            float radius,
            float2 halfExtents,
            float rotationRadians,
            CombatShapeType shapeType,
            float2 unitAxis)
        {
            switch (shapeType)
            {
                case CombatShapeType.Rectangle:
                    math.sincos(rotationRadians, out float sine, out float cosine);
                    float2 xAxis = new(cosine, sine);
                    float2 yAxis = new(-sine, cosine);
                    return math.abs(halfExtents.x * math.dot(xAxis, unitAxis))
                        + math.abs(halfExtents.y * math.dot(yAxis, unitAxis));
                case CombatShapeType.Capsule:
                    CombatCollisionMath.GetCapsuleSegment(float2.zero, halfExtents.x, rotationRadians, out _, out float2 capsuleEnd);
                    return radius + math.abs(math.dot(capsuleEnd, unitAxis));
                default:
                    return radius;
            }
        }

        public static bool TryBuildTravelCorridor(
            float2 segmentStart,
            float2 segmentEnd,
            float radius,
            float2 halfExtents,
            float rotationRadians,
            CombatShapeType shapeType,
            out float2 boxCenter,
            out float2 boxHalfExtents,
            out float boxRotationRadians)
        {
            float2 segment = segmentEnd - segmentStart;
            float segmentLengthSquared = math.lengthsq(segment);
            if (segmentLengthSquared <= Epsilon)
            {
                boxCenter = default;
                boxHalfExtents = default;
                boxRotationRadians = default;
                return false;
            }

            float segmentLength = math.sqrt(segmentLengthSquared);
            float2 direction = segment / segmentLength;
            float2 perpendicular = new(-direction.y, direction.x);
            float perpendicularSupport = SupportExtent(radius, halfExtents, rotationRadians, shapeType, perpendicular);

            boxCenter = (segmentStart + segmentEnd) * 0.5f;
            boxHalfExtents = new float2(segmentLength * 0.5f, perpendicularSupport);
            boxRotationRadians = math.atan2(direction.y, direction.x);
            return true;
        }

        public static float ClosestApproachParam(float2 segmentStart, float2 segmentEnd, float2 targetPosition)
        {
            float2 segment = segmentEnd - segmentStart;
            float segmentLengthSquared = math.lengthsq(segment);
            if (segmentLengthSquared <= Epsilon)
            {
                return 0f;
            }

            return math.clamp(math.dot(targetPosition - segmentStart, segment) / segmentLengthSquared, 0f, 1f);
        }
    }
}
