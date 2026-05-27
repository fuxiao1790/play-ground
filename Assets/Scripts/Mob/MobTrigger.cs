using UnityEngine;

namespace PlayGround.Mob
{
    public abstract class MobTrigger : ScriptableObject
    {
        public abstract void UpdateTrigger(float deltaTime, MobRoot mob, MobBlackboard blackboard, MobEventQueue events);
    }
}
