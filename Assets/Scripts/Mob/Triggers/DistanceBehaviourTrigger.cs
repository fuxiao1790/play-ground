using UnityEngine;

namespace PlayGround.Mob.Triggers
{
    [CreateAssetMenu(menuName = "PlayGround/Mobs/Triggers/Distance Behaviour")]
    public sealed class DistanceBehaviourTrigger : MobTrigger
    {
        [SerializeField] private float keepDistanceRadius = 1.625f;

        public override void UpdateTrigger(float deltaTime, MobRoot mob, MobBlackboard blackboard, MobEventQueue events)
        {
            if (!blackboard.TargetVisible || !blackboard.HasValidTarget())
            {
                return;
            }

            float distance = Vector2.Distance(mob.transform.position, (Vector2)blackboard.Target.position);
            if (distance < keepDistanceRadius)
            {
                blackboard.RequestTrigger("on_close_to_target", 20);
            }
        }
    }
}
