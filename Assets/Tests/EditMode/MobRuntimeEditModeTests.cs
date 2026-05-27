using NUnit.Framework;
using PlayGround.Mob;
using UnityEngine;

namespace PlayGround.Tests.EditMode
{
    public sealed class MobRuntimeEditModeTests
    {
        [Test]
        public void StateDriverMovesFromIdleToWanderOnTick()
        {
            var blackboard = new MobBlackboard();
            var driver = new MobStateDriver(blackboard);
            var queue = new MobEventQueue();

            queue.PushType(MobEventType.Tick);
            driver.Update(queue.Drain());

            Assert.That(driver.CurrentState, Is.EqualTo(MobBehaviourState.Wander));
        }

        [Test]
        public void StateDriverRecoversFromHurtToChaseWhenTargetVisible()
        {
            GameObject target = new("MobTarget");
            try
            {
                var blackboard = new MobBlackboard { TargetVisible = true, Target = target.transform };
                var driver = new MobStateDriver(blackboard);
                var queue = new MobEventQueue();

                queue.PushType(MobEventType.Damaged);
                queue.PushType(MobEventType.Recovered);
                driver.Update(queue.Drain());

                Assert.That(driver.CurrentState, Is.EqualTo(MobBehaviourState.Chase));
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void LethalEventWinsAndLeavesMobDead()
        {
            var blackboard = new MobBlackboard();
            var driver = new MobStateDriver(blackboard);
            var queue = new MobEventQueue();

            queue.PushType(MobEventType.Died);
            queue.PushType(MobEventType.Tick);
            driver.Update(queue.Drain());

            Assert.That(driver.CurrentState, Is.EqualTo(MobBehaviourState.Dead));
        }

        [Test]
        public void DebuffStacksReturnThresholdHitAndCanClear()
        {
            var stacks = new MobDebuffStackState();

            Assert.That(stacks.AddStacks(MobDebuffStatus.Volatile, 2, 3), Is.False);
            Assert.That(stacks.AddStacks(MobDebuffStatus.Volatile, 1, 3), Is.True);
            Assert.That(stacks.GetStackCount(MobDebuffStatus.Volatile), Is.EqualTo(3));

            stacks.ClearStacks(MobDebuffStatus.Volatile);

            Assert.That(stacks.GetStackCount(MobDebuffStatus.Volatile), Is.EqualTo(0));
        }
    }
}
