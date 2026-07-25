using NUnit.Framework;
using PlayGround.Skills;

namespace PlayGround.Tests.EditMode
{
    public sealed class SkillSlotStateEditModeTests
    {
        [Test]
        public void RefundFireMakesRejectedCastReadyImmediately()
        {
            var state = new SkillSlotState();
            state.SetRecoveryTime(2f);
            state.ResetOnFire();

            state.RefundFire();

            Assert.That(state.IsReady, Is.True);
        }
    }
}
