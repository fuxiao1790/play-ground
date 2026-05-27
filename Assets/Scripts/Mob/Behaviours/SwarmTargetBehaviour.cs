using UnityEngine;

namespace PlayGround.Mob.Behaviours
{
    [CreateAssetMenu(menuName = "PlayGround/Mobs/Behaviours/Swarm Target")]
    public sealed class SwarmTargetBehaviour : MobBehaviour
    {
        public override string Key => "swarm_target";

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

            Vector2 toTarget = (Vector2)blackboard.Target.position - (Vector2)mob.transform.position;
            return toTarget.sqrMagnitude <= 0.0001f ? Vector2.zero : toTarget.normalized * mob.Speed;
        }
    }
}
