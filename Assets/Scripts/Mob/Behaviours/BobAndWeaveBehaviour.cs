using UnityEngine;

namespace PlayGround.Mob.Behaviours
{
    [CreateAssetMenu(menuName = "PlayGround/Mobs/Behaviours/Bob And Weave")]
    public sealed class BobAndWeaveBehaviour : MobBehaviour
    {
        [SerializeField] private float weaveStrength = 0.75f;
        [SerializeField] private float weaveFrequency = 5f;

        private float time;

        public override string Key => "bob_and_weave";

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

            time += deltaTime;
            Vector2 toTarget = (Vector2)blackboard.Target.position - (Vector2)mob.transform.position;
            if (toTarget.sqrMagnitude <= 0.0001f)
            {
                return Vector2.zero;
            }

            Vector2 forward = toTarget.normalized;
            Vector2 side = new Vector2(-forward.y, forward.x) * Mathf.Sin(time * weaveFrequency) * weaveStrength;
            return (forward + side).normalized * mob.Speed;
        }
    }
}
