using UnityEngine;

namespace PlayGround.Mob
{
    public abstract class MobBehaviour : ScriptableObject
    {
        [SerializeField] private string key = "none";

        public virtual string Key => key;

        public virtual bool SupportsState(MobBehaviourState state)
        {
            return false;
        }

        public virtual Vector2 Evaluate(float deltaTime, MobRoot mob, MobBlackboard blackboard, MobBehaviourState state)
        {
            return Vector2.zero;
        }
    }
}
