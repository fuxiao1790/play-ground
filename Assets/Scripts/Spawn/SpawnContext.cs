using System.Collections.Generic;
using UnityEngine;

namespace PlayGround.Spawn
{
    public readonly struct SpawnContext
    {
        public SpawnContext(
            IReadOnlyList<SpawnPoint> points,
            Transform target,
            global::System.Random rng)
        {
            Points = points;
            Target = target;
            Rng = rng;
        }

        public IReadOnlyList<SpawnPoint> Points { get; }
        public Transform Target { get; }
        public global::System.Random Rng { get; }
    }
}
