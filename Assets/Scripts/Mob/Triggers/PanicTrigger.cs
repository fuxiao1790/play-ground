using UnityEngine;

namespace PlayGround.Mob.Triggers
{
    [CreateAssetMenu(menuName = "PlayGround/Mobs/Triggers/Panic")]
    public sealed class PanicTrigger : MobTrigger
    {
        [SerializeField] private float panicHealthRatio = 0.35f;

        public override void UpdateTrigger(float deltaTime, MobRoot mob, MobBlackboard blackboard, MobEventQueue events)
        {
            if (blackboard.HealthRatio() <= panicHealthRatio)
            {
                blackboard.RequestTrigger("on_low_hp", 100);
            }
        }
    }
}
