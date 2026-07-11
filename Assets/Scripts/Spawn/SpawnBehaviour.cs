using UnityEngine;

namespace PlayGround.Spawn
{
    public abstract class SpawnBehaviour : ScriptableObject
    {
        public abstract SpawnBehaviourRuntime CreateRuntime();
    }

    public abstract class SpawnBehaviourRuntime
    {
        public abstract void Tick(ISpawnSink sink, float dt);
    }
}
