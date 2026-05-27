using PlayGround.Common;
using PlayGround.Mob;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Attack
{
    public sealed class ProjectileStackExplosionEffect : ProjectileHitEffect
    {
        [SerializeField] private MobDebuffStatus stackDebuffStatus = MobDebuffStatus.Volatile;
        [SerializeField, Min(1)] private int stacksPerProjectileHit = 1;
        [SerializeField, Min(1)] private int stackExplosionThreshold = 3;
        [SerializeField] private int stackExplosionAoeTypeId = -1;
        [SerializeField] private float stackExplosionDamage = 1f;
        [SerializeField] private float stackExplosionLifetimeSeconds;
        [SerializeField] private float stackExplosionTickIntervalSeconds;
        [SerializeField] private bool stackExplosionAtTargetPosition = true;

        public override void Apply(in ProjectileHitContext hit, global::System.Action<ProjectileAoeSpawnRequest> emitAoe)
        {
            if (stackExplosionAoeTypeId < 0 || hit.Target is not MobRoot mob)
            {
                return;
            }

            bool thresholdReached = mob.AddDebuffStacks(
                stackDebuffStatus,
                Mathf.Max(1, stacksPerProjectileHit),
                Mathf.Max(1, stackExplosionThreshold));
            if (!thresholdReached)
            {
                return;
            }

            mob.ClearDebuffStacks(stackDebuffStatus);
            Vector2 position = stackExplosionAtTargetPosition ? mob.transform.position : hit.Position;
            emitAoe?.Invoke(new ProjectileAoeSpawnRequest(
                stackExplosionAoeTypeId,
                position,
                new DamageSnapshot(Mathf.Max(0f, stackExplosionDamage)),
                stackExplosionLifetimeSeconds,
                stackExplosionTickIntervalSeconds));
        }
    }
}
