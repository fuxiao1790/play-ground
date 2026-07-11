using UnityEngine;

namespace PlayGround.Spawn
{
    [CreateAssetMenu(menuName = "Play Ground/Spawn/Continuous Stream Behaviour")]
    public sealed class ContinuousStreamBehaviour : SpawnBehaviour
    {
        [SerializeField, Min(0f)] private float spawnsPerSecond = 50f;
        [SerializeField, Min(1)] private int maxSpawnsPerTick = 8;

        public override SpawnBehaviourRuntime CreateRuntime()
        {
            return new Runtime(spawnsPerSecond, maxSpawnsPerTick);
        }

        private sealed class Runtime : SpawnBehaviourRuntime
        {
            private readonly float spawnsPerSecond;
            private readonly int maxSpawnsPerTick;
            private float accumulator;

            public Runtime(float spawnsPerSecond, int maxSpawnsPerTick)
            {
                this.spawnsPerSecond = Mathf.Max(0f, spawnsPerSecond);
                this.maxSpawnsPerTick = Mathf.Max(1, maxSpawnsPerTick);
            }

            public override void Tick(ISpawnSink sink, float dt)
            {
                if (sink == null)
                {
                    return;
                }

                accumulator += spawnsPerSecond * Mathf.Max(0f, dt);
                if (!sink.CanSpawn)
                {
                    accumulator = Mathf.Min(accumulator, 1f);
                    return;
                }

                int spawned = 0;
                while (accumulator >= 1f && spawned < maxSpawnsPerTick && sink.CanSpawn)
                {
                    sink.Spawn();
                    accumulator -= 1f;
                    spawned++;
                }

                if (!sink.CanSpawn)
                {
                    accumulator = Mathf.Min(accumulator, 1f);
                }
            }
        }
    }
}
