using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Attack
{
    public abstract class ProjectileHitEffect : MonoBehaviour
    {
        public virtual void Configure(Component owner)
        {
        }

        public abstract void Apply(in ProjectileHitContext hit, global::System.Action<ProjectileAoeSpawnRequest> emitAoe);
    }
}
