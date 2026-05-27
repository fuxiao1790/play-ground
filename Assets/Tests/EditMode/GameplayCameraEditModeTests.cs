using NUnit.Framework;
using PlayGround.CameraSystem;
using UnityEngine;

namespace PlayGround.Tests.EditMode
{
    public sealed class GameplayCameraEditModeTests
    {
        [Test]
        public void OvalClampLeavesCameraInsideDeadZone()
        {
            Vector2 cameraPosition = GameplayCamera.ClampPlayerToOval(Vector2.zero, new Vector2(1f, 0f), new Vector2(2f, 2f));

            Assert.That(cameraPosition, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void OvalClampMovesCameraToDeadZoneEdge()
        {
            Vector2 cameraPosition = GameplayCamera.ClampPlayerToOval(Vector2.zero, new Vector2(4f, 0f), new Vector2(2f, 2f));

            Assert.That(cameraPosition.x, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(cameraPosition.y, Is.EqualTo(0f).Within(0.0001f));
        }
    }
}
