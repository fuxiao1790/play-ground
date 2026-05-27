using UnityEngine;

namespace PlayGround.Mob.Triggers
{
    [CreateAssetMenu(menuName = "PlayGround/Mobs/Triggers/Hurt Recovery")]
    public sealed class HurtRecoveryTrigger : MobTrigger
    {
        [SerializeField] private float hurtDuration = 0.35f;

        private float remaining;
        private bool inHurt;
        private bool recoverySent;

        public override void UpdateTrigger(float deltaTime, MobRoot mob, MobBlackboard blackboard, MobEventQueue events)
        {
            if (blackboard.BehaviourState != MobBehaviourState.Hurt)
            {
                inHurt = false;
                recoverySent = false;
                return;
            }

            if (!inHurt)
            {
                inHurt = true;
                recoverySent = false;
                remaining = hurtDuration;
                return;
            }

            if (recoverySent)
            {
                return;
            }

            remaining -= deltaTime;
            if (remaining <= 0f)
            {
                recoverySent = true;
                events.PushType(MobEventType.Recovered, this);
            }
        }
    }
}
