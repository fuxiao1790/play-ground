using UnityEngine;

namespace PlayGround.Mob.Triggers
{
    [CreateAssetMenu(menuName = "PlayGround/Mobs/Triggers/Target Sensor")]
    public sealed class TargetSensorTrigger : MobTrigger
    {
        [SerializeField] private float detectionRadius = 4.375f;
        [SerializeField] private float loseRadius = 5.625f;

        public override void UpdateTrigger(float deltaTime, MobRoot mob, MobBlackboard blackboard, MobEventQueue events)
        {
            if (!blackboard.HasValidTarget())
            {
                if (blackboard.TargetVisible)
                {
                    blackboard.ClearTarget();
                    events.PushType(MobEventType.TargetLost, this);
                }

                return;
            }

            float distance = Vector2.Distance(mob.transform.position, (Vector2)blackboard.Target.position);
            if (!blackboard.TargetVisible && distance <= detectionRadius)
            {
                blackboard.TargetVisible = true;
                events.PushType(MobEventType.TargetSeen, this);
            }
            else if (blackboard.TargetVisible && distance > loseRadius)
            {
                blackboard.ClearTarget();
                events.PushType(MobEventType.TargetLost, this);
            }
            else if (blackboard.TargetVisible)
            {
                blackboard.RequestTrigger("on_target_seen");
            }
        }
    }
}
