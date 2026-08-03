using NUnit.Framework;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Collision.Narrowphase;
using Unity.Mathematics;

namespace PlayGround.Tests.EditMode
{
    public sealed class CombatSweepMathEditModeTests
    {
        [Test]
        public void SupportExtent_CircleEqualsRadiusOnEveryAxis()
        {
            float2[] axes = { new(1f, 0f), new(0f, 1f), math.normalize(new float2(3f, -4f)) };

            for (int i = 0; i < axes.Length; i++)
            {
                float extent = CombatSweepMath.SupportExtent(
                    0.7f, new float2(99f, 99f), 1.2f, CombatShapeType.Circle, axes[i]);

                Assert.That(extent, Is.EqualTo(0.7f).Within(0.00001f));
            }
        }

        [Test]
        public void SupportExtent_RectangleMatchesAxesProjectionAndAxisSign()
        {
            float2 halfExtents = new(0.05f, 0.075f);
            float2 diagonal = math.normalize(new float2(3f, 4f));

            Assert.That(CombatSweepMath.SupportExtent(0f, halfExtents, 0f, CombatShapeType.Rectangle, new float2(1f, 0f)),
                Is.EqualTo(0.05f).Within(0.00001f));
            Assert.That(CombatSweepMath.SupportExtent(0f, halfExtents, 0f, CombatShapeType.Rectangle, new float2(0f, 1f)),
                Is.EqualTo(0.075f).Within(0.00001f));

            float diagonalExtent = CombatSweepMath.SupportExtent(
                0f, halfExtents, 0f, CombatShapeType.Rectangle, diagonal);
            Assert.That(diagonalExtent, Is.EqualTo((0.05f * 0.6f) + (0.075f * 0.8f)).Within(0.00001f));
            Assert.That(CombatSweepMath.SupportExtent(0f, halfExtents, 0f, CombatShapeType.Rectangle, -diagonal),
                Is.EqualTo(diagonalExtent).Within(0.00001f));
        }

        [Test]
        public void SupportExtent_RotatedRectangleMatchesCounterRotatedAxis()
        {
            float rotation = math.radians(37f);
            float2 axis = math.normalize(new float2(2f, 1f));
            float2 counterRotated = Rotate(axis, -rotation);
            float2 halfExtents = new(0.2f, 0.4f);

            float rotated = CombatSweepMath.SupportExtent(
                0f, halfExtents, rotation, CombatShapeType.Rectangle, axis);
            float unrotated = CombatSweepMath.SupportExtent(
                0f, halfExtents, 0f, CombatShapeType.Rectangle, counterRotated);

            Assert.That(rotated, Is.EqualTo(unrotated).Within(0.00001f));
        }

        [Test]
        public void TryBuildTravelCorridor_ZeroLengthReturnsFalse()
        {
            float2 position = new(2f, -3f);
            float2 halfExtents = new(0.1f, 0.6f);
            float rotation = math.radians(23f);

            bool hasCorridor = CombatSweepMath.TryBuildTravelCorridor(
                position, position, 0.25f, halfExtents, rotation, CombatShapeType.Rectangle,
                out float2 center, out float2 resultHalfExtents, out float resultRotation);

            Assert.That(hasCorridor, Is.False);
            Assert.That(center, Is.EqualTo(float2.zero));
            Assert.That(resultHalfExtents, Is.EqualTo(float2.zero));
            Assert.That(resultRotation, Is.Zero);
        }

        [Test]
        public void TryBuildTravelCorridor_CatchesTargetBetweenDiscreteEndpoints()
        {
            float2 start = new(-2f, 0f);
            float2 end = new(2f, 0f);
            float2 target = float2.zero;

            Assert.That(CombatCollisionMath.Hit(start, 0.1f, float2.zero, 0f, CombatShapeType.Circle,
                target, 0.25f, float2.zero, 0f, CombatShapeType.Circle), Is.False);
            Assert.That(CombatCollisionMath.Hit(end, 0.1f, float2.zero, 0f, CombatShapeType.Circle,
                target, 0.25f, float2.zero, 0f, CombatShapeType.Circle), Is.False);

            Assert.That(CombatSweepMath.TryBuildTravelCorridor(start, end, 0.1f, float2.zero, 0f, CombatShapeType.Circle,
                out float2 center, out float2 halfExtents, out float rotation), Is.True);

            Assert.That(CombatCollisionMath.Hit(center, 0f, halfExtents, rotation, CombatShapeType.Rectangle,
                target, 0.25f, float2.zero, 0f, CombatShapeType.Circle), Is.True);
        }

        [Test]
        public void TryBuildTravelCorridor_UsesShapeSupportInsteadOfBoundingCircle()
        {
            Assert.That(CombatSweepMath.TryBuildTravelCorridor(
                new float2(-1f, 0f), new float2(1f, 0f), 0f, new float2(0.5f, 0.05f), 0f,
                CombatShapeType.Rectangle, out _, out float2 halfExtents, out _), Is.True);

            Assert.That(halfExtents.y, Is.EqualTo(0.05f).Within(0.00001f));
            Assert.That(halfExtents.x, Is.EqualTo(1f).Within(0.00001f));
        }

        [Test]
        public void TryBuildTravelCorridor_CoversSegmentOnly()
        {
            Assert.That(CombatSweepMath.TryBuildTravelCorridor(
                new float2(0f, 0f), new float2(4f, 0f), 0.2f, float2.zero, 0f,
                CombatShapeType.Circle, out float2 center, out float2 halfExtents, out float rotation), Is.True);

            Assert.That(center, Is.EqualTo(new float2(2f, 0f)));
            Assert.That(halfExtents.x, Is.EqualTo(2f).Within(0.00001f));
            Assert.That(rotation, Is.EqualTo(0f).Within(0.00001f));
        }

        [Test]
        public void ClosestApproachParam_ReturnsMidpointAndEndpointClamps()
        {
            float2 start = new(0f, 0f);
            float2 end = new(10f, 0f);

            Assert.That(CombatSweepMath.ClosestApproachParam(start, end, new float2(5f, 3f)),
                Is.EqualTo(0.5f).Within(0.00001f));
            Assert.That(CombatSweepMath.ClosestApproachParam(start, end, new float2(-1f, 0f)), Is.EqualTo(0f));
            Assert.That(CombatSweepMath.ClosestApproachParam(start, end, new float2(12f, 0f)), Is.EqualTo(1f));
        }

        [Test]
        public void CorridorAloneDoesNotCoverEndpointFootprint()
        {
            float2 start = float2.zero;
            float2 end = new(4f, 0f);
            float2 target = new(4.15f, 0f);
            Assert.That(CombatCollisionMath.Hit(end, 0.1f, float2.zero, 0f, CombatShapeType.Circle,
                target, 0.1f, float2.zero, 0f, CombatShapeType.Circle), Is.True);
            Assert.That(CombatSweepMath.TryBuildTravelCorridor(start, end, 0.1f, float2.zero, 0f,
                CombatShapeType.Circle, out float2 center, out float2 halfExtents, out float rotation), Is.True);
            Assert.That(CombatCollisionMath.Hit(center, 0f, halfExtents, rotation, CombatShapeType.Rectangle,
                target, 0.1f, float2.zero, 0f, CombatShapeType.Circle), Is.False);
        }

        private static float2 Rotate(float2 value, float radians)
        {
            math.sincos(radians, out float sine, out float cosine);
            return new float2(
                (value.x * cosine) - (value.y * sine),
                (value.x * sine) + (value.y * cosine));
        }
    }
}
