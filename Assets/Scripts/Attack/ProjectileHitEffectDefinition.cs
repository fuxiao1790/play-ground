using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Attack
{
    public abstract class ProjectileHitEffectDefinition : ScriptableObject
    {
        public abstract void Apply(
            in ProjectileHitContext hit,
            global::System.Action<ProjectileAoeSpawnRequest> emitAoe);
    }
}
