using System.Collections.Generic;
using UnityEngine;

namespace PlayGround.Attack
{
    public abstract class ProjectileChildSpawnPattern : ScriptableObject
    {
        public abstract void Build(
            List<ProjectileVolleyBuilder.SpawnRequest> buffer,
            Vector2 parentPosition,
            Vector2 parentVelocity,
            int childCount,
            float childSpeed,
            int tickIndex);
    }
}
