using UnityEngine;

namespace PlayGround.Spawn
{
    public abstract class SpawnPlacement : ScriptableObject
    {
        public abstract bool TryResolve(in SpawnContext ctx, out Vector2 position);
    }
}
