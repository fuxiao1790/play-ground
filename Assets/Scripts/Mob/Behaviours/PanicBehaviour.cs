using UnityEngine;

namespace PlayGround.Mob.Behaviours
{
    [CreateAssetMenu(menuName = "PlayGround/Mobs/Behaviours/Panic")]
    public sealed class PanicBehaviour : MobBehaviour
    {
        [SerializeField] private float speedScale = 1.2f;
        [SerializeField] private float jitterStrength = 0.35f;

        public override string Key => "panic";

        public override bool SupportsState(MobBehaviourState state)
        {
            return state is MobBehaviourState.Wander or MobBehaviourState.Chase;
        }

        public override Vector2 Evaluate(float deltaTime, MobRoot mob, MobBlackboard blackboard, MobBehaviourState state)
        {
            Vector2 direction = Random.insideUnitCircle.normalized * jitterStrength;
            if (blackboard.HasValidTarget())
            {
                Vector2 away = (Vector2)mob.transform.position - (Vector2)blackboard.Target.position;
                if (away.sqrMagnitude > 0.0001f)
                {
                    direction += away.normalized;
                }
            }

            return direction.sqrMagnitude <= 0.0001f ? Vector2.zero : direction.normalized * mob.Speed * speedScale;
        }
    }
}
