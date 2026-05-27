using UnityEngine;

namespace PlayGround.Mob.Triggers
{
    [CreateAssetMenu(menuName = "PlayGround/Mobs/Triggers/Bob And Weave")]
    public sealed class BobAndWeaveTrigger : MobTrigger
    {
        [SerializeField] private float minDistance = 2f;
        [SerializeField] private float maxDistance = 4.0625f;

        public override void UpdateTrigger(float deltaTime, MobRoot mob, MobBlackboard blackboard, MobEventQueue events)
        {
            if (!blackboard.TargetVisible || !blackboard.HasValidTarget())
            {
                return;
            }

            float distance = Vector2.Distance(mob.transform.position, (Vector2)blackboard.Target.position);
            if (distance >= minDistance && distance <= maxDistance)
            {
                blackboard.RequestTrigger("on_weave_range", 10);
            }
        }
    }
}
