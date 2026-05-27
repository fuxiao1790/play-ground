using UnityEngine;

namespace PlayGround.Mob.Behaviours
{
    [CreateAssetMenu(menuName = "PlayGround/Mobs/Behaviours/Wander")]
    public sealed class WanderBehaviour : MobBehaviour
    {
        [SerializeField] private float changeInterval = 1.4f;
        [SerializeField] private float speedScale = 0.65f;

        private Vector2 direction;
        private float remaining;

        public override string Key => "wander";

        public override bool SupportsState(MobBehaviourState state)
        {
            return state == MobBehaviourState.Wander;
        }

        public override Vector2 Evaluate(float deltaTime, MobRoot mob, MobBlackboard blackboard, MobBehaviourState state)
        {
            remaining -= deltaTime;
            if (remaining <= 0f)
            {
                remaining = Random.Range(changeInterval * 0.5f, changeInterval * 1.5f);
                direction = Random.insideUnitCircle.normalized;
            }

            return direction * mob.Speed * speedScale;
        }
    }
}
