using UnityEngine;

namespace PlayGround.Mob.Behaviours
{
    [CreateAssetMenu(menuName = "PlayGround/Mobs/Behaviours/Keep Distance")]
    public sealed class KeepDistanceBehaviour : MobBehaviour
    {
        [SerializeField] private float preferredDistance = 2.25f;
        [SerializeField] private float tolerance = 0.375f;

        public override string Key => "keep_distance";

        public override bool SupportsState(MobBehaviourState state)
        {
            return state == MobBehaviourState.Chase;
        }

        public override Vector2 Evaluate(float deltaTime, MobRoot mob, MobBlackboard blackboard, MobBehaviourState state)
        {
            if (!blackboard.HasValidTarget())
            {
                return Vector2.zero;
            }

            Vector2 fromTarget = (Vector2)mob.transform.position - (Vector2)blackboard.Target.position;
            float distance = fromTarget.magnitude;
            if (distance <= 0.001f)
            {
                return Vector2.right * mob.Speed;
            }

            if (distance < preferredDistance - tolerance)
            {
                return fromTarget.normalized * mob.Speed;
            }

            if (distance > preferredDistance + tolerance)
            {
                return -fromTarget.normalized * mob.Speed;
            }

            return Vector2.zero;
        }
    }
}
