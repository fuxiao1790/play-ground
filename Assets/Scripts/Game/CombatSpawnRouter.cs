using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Game
{
    public sealed class CombatSpawnRouter
    {
        private ProjectileRoot playerProjectileRoot;
        private ProjectileRoot mobProjectileRoot;
        private AoeRoot playerAoeRoot;
        private AoeRoot mobAoeRoot;

        public void Bind(
            ProjectileRoot playerToMobProjectiles,
            ProjectileRoot mobToPlayerProjectiles,
            AoeRoot playerToMobAoes,
            AoeRoot mobToPlayerAoes)
        {
            Unbind();

            playerProjectileRoot = playerToMobProjectiles;
            mobProjectileRoot = mobToPlayerProjectiles;
            playerAoeRoot = playerToMobAoes;
            mobAoeRoot = mobToPlayerAoes;

            if (playerProjectileRoot != null)
            {
                playerProjectileRoot.ProjectileHit += OnPlayerProjectileHit;
            }

            if (mobProjectileRoot != null)
            {
                mobProjectileRoot.ProjectileHit += OnMobProjectileHit;
            }

            if (playerAoeRoot != null)
            {
                playerAoeRoot.AoeHit += OnPlayerAoeHit;
            }

            if (mobAoeRoot != null)
            {
                mobAoeRoot.AoeHit += OnMobAoeHit;
            }
        }

        public void Unbind()
        {
            if (playerProjectileRoot != null)
            {
                playerProjectileRoot.ProjectileHit -= OnPlayerProjectileHit;
            }

            if (mobProjectileRoot != null)
            {
                mobProjectileRoot.ProjectileHit -= OnMobProjectileHit;
            }

            if (playerAoeRoot != null)
            {
                playerAoeRoot.AoeHit -= OnPlayerAoeHit;
            }

            if (mobAoeRoot != null)
            {
                mobAoeRoot.AoeHit -= OnMobAoeHit;
            }

            playerProjectileRoot = null;
            mobProjectileRoot = null;
            playerAoeRoot = null;
            mobAoeRoot = null;
        }

        private void OnPlayerProjectileHit(ProjectileHitContext context)
        {
            SpawnImpactAoe(context, playerAoeRoot);
            SpawnImpactProjectiles(context, playerProjectileRoot);
        }

        private void OnMobProjectileHit(ProjectileHitContext context)
        {
            SpawnImpactAoe(context, mobAoeRoot);
            SpawnImpactProjectiles(context, mobProjectileRoot);
        }

        private void OnPlayerAoeHit(AoeHitContext context)
        {
            SpawnProjectileBurst(context, playerProjectileRoot);
        }

        private void OnMobAoeHit(AoeHitContext context)
        {
            SpawnProjectileBurst(context, mobProjectileRoot);
        }

        private static void SpawnImpactAoe(ProjectileHitContext context, AoeRoot destination)
        {
            ProjectileImpactAoeSnapshot impact = context.Payload.ImpactAoe;
            if (destination == null || !impact.Enabled)
            {
                return;
            }

            int targetMask = impact.TargetMask != 0 ? impact.TargetMask : destination.TargetMask;
            destination.Spawn(new AoeSpawnCommand(
                impact.TypeId,
                context.Position,
                targetMask,
                new DamageSnapshot(Mathf.Max(0f, impact.DamageAmount)),
                impact.LifetimeSeconds,
                impact.TickIntervalSeconds,
                critChance: impact.CritChance,
                critMultiplier: impact.CritMultiplier));
        }

        private static void SpawnImpactProjectiles(ProjectileHitContext context, ProjectileRoot destination)
        {
            ProjectileImpactProjectileSnapshot burst = context.Payload.ImpactProjectile;
            if (destination == null || !burst.Enabled)
            {
                return;
            }

            Vector2 baseDirection = Vector2.right;
            if (context.Target != null)
            {
                Vector2 toTarget = context.Target.CombatTargetPosition - context.Position;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    baseDirection = -toTarget.normalized;
                }
            }

            int count = Mathf.Max(1, burst.Count);
            float spread = count > 1 ? burst.SpreadDegrees : 0f;
            int targetMask = burst.TargetMask != 0 ? burst.TargetMask : destination.TargetMask;
            for (int i = 0; i < count; i++)
            {
                Vector2 direction = SpreadDirection(baseDirection, i, count, spread);
                destination.Spawn(new ProjectileSpawnCommand(
                    context.Position,
                    direction,
                    burst.Speed,
                    burst.LifetimeSeconds,
                    burst.Radius,
                    burst.HalfExtents,
                    burst.RotationRadians,
                    burst.Damage,
                    burst.ShapeType,
                    burst.ProjectileTypeId,
                    targetMask,
                    burst.PierceCount,
                    burst.RepeatHitCooldownSeconds,
                    burst.Tracking,
                    ProjectileChildSpawnConfig.Disabled,
                    burst.DirectDamageEnabled,
                    default,
                    burst.ImpactAoe,
                    burst.StackEffect), context.TargetId);
            }
        }

        private static void SpawnProjectileBurst(AoeHitContext context, ProjectileRoot destination)
        {
            AoeProjectileBurstSnapshot burst = context.ProjectileBurst;
            if (destination == null || !burst.Enabled)
            {
                return;
            }

            Vector2 baseDirection = Vector2.right;
            if (context.Target != null)
            {
                Vector2 toTarget = context.Target.CombatTargetPosition - context.Position;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    baseDirection = toTarget.normalized;
                }
            }

            int count = Mathf.Max(1, burst.Count);
            float spread = count > 1 ? burst.SpreadDegrees : 0f;
            int targetMask = burst.TargetMask != 1 ? burst.TargetMask : destination.TargetMask;
            for (int i = 0; i < count; i++)
            {
                Vector2 direction = SpreadDirection(baseDirection, i, count, spread);
                destination.Spawn(new ProjectileSpawnCommand(
                    context.Position,
                    direction,
                    burst.Speed,
                    burst.LifetimeSeconds,
                    burst.Radius,
                    burst.HalfExtents,
                    burst.RotationRadians,
                    burst.Damage,
                    burst.ShapeType,
                    burst.ProjectileTypeId,
                    targetMask,
                    burst.PierceCount,
                    burst.RepeatHitCooldownSeconds,
                    ProjectileTrackingConfig.Disabled,
                    ProjectileChildSpawnConfig.Disabled,
                    burst.DirectDamageEnabled));
            }
        }

        private static Vector2 SpreadDirection(Vector2 baseDirection, int index, int count, float spreadDegrees)
        {
            if (count <= 1 || spreadDegrees <= 0f)
            {
                return baseDirection.sqrMagnitude > 0f ? baseDirection.normalized : Vector2.right;
            }

            float angle = -spreadDegrees * 0.5f + spreadDegrees / (count - 1) * index;
            return Quaternion.Euler(0f, 0f, angle) * baseDirection.normalized;
        }
    }
}
