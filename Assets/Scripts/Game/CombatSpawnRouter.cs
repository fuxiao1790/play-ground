using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Game
{
    // Consumes internal HitSpawn streams only. Do not route combat spawns from
    // damage application or expose CombatSpawnElement to scene listeners.
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
                playerProjectileRoot.HitSpawn += OnPlayerProjectileHitSpawn;
            }

            if (mobProjectileRoot != null)
            {
                mobProjectileRoot.HitSpawn += OnMobProjectileHitSpawn;
            }

            if (playerAoeRoot != null)
            {
                playerAoeRoot.HitSpawn += OnPlayerAoeHitSpawn;
            }

            if (mobAoeRoot != null)
            {
                mobAoeRoot.HitSpawn += OnMobAoeHitSpawn;
            }
        }

        public void Unbind()
        {
            if (playerProjectileRoot != null)
            {
                playerProjectileRoot.HitSpawn -= OnPlayerProjectileHitSpawn;
            }

            if (mobProjectileRoot != null)
            {
                mobProjectileRoot.HitSpawn -= OnMobProjectileHitSpawn;
            }

            if (playerAoeRoot != null)
            {
                playerAoeRoot.HitSpawn -= OnPlayerAoeHitSpawn;
            }

            if (mobAoeRoot != null)
            {
                mobAoeRoot.HitSpawn -= OnMobAoeHitSpawn;
            }

            playerProjectileRoot = null;
            mobProjectileRoot = null;
            playerAoeRoot = null;
            mobAoeRoot = null;
        }

        private void OnPlayerProjectileHitSpawn(in CombatHitContext context, in CombatSpawnElement spawn)
        {
            SpawnImpactAoe(context, in spawn, playerAoeRoot);
            SpawnImpactProjectiles(context, in spawn, playerProjectileRoot);
        }

        private void OnMobProjectileHitSpawn(in CombatHitContext context, in CombatSpawnElement spawn)
        {
            SpawnImpactAoe(context, in spawn, mobAoeRoot);
            SpawnImpactProjectiles(context, in spawn, mobProjectileRoot);
        }

        private void OnPlayerAoeHitSpawn(in CombatHitContext context, in CombatSpawnElement spawn)
        {
            SpawnProjectileBurst(context, in spawn, playerProjectileRoot);
        }

        private void OnMobAoeHitSpawn(in CombatHitContext context, in CombatSpawnElement spawn)
        {
            SpawnProjectileBurst(context, in spawn, mobProjectileRoot);
        }

        private static void SpawnImpactAoe(in CombatHitContext context, in CombatSpawnElement spawn, AoeRoot destination)
        {
            ProjectileImpactAoeSnapshot impact = spawn.ImpactAoe;
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
                impact.Geometry,
                critChance: impact.CritChance,
                critMultiplier: impact.CritMultiplier,
                sourceNodeId: context.SourceNodeId));
        }

        private static void SpawnImpactProjectiles(in CombatHitContext context, in CombatSpawnElement spawn, ProjectileRoot destination)
        {
            ProjectileImpactProjectileSnapshot burst = spawn.ImpactProjectile;
            if (destination == null || !burst.Enabled)
            {
                return;
            }

            Vector2 baseDirection = Vector2.right;
            Vector2 toTarget = new Vector2(spawn.TargetPosition.x, spawn.TargetPosition.y) - context.Position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                baseDirection = -toTarget.normalized;
            }

            int targetMask = burst.TargetMask != 0 ? burst.TargetMask : destination.TargetMask;
            destination.Spawn(new ProjectileSpawnCommand(
                context.Position,
                baseDirection,
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
                burst.StackEffect,
                count: burst.Count,
                spreadDegrees: burst.SpreadDegrees), context.TargetId);
        }

        private static void SpawnProjectileBurst(in CombatHitContext context, in CombatSpawnElement spawn, ProjectileRoot destination)
        {
            AoeProjectileBurstSnapshot burst = spawn.ProjectileBurst;
            if (destination == null || !burst.Enabled)
            {
                return;
            }

            Vector2 baseDirection = Vector2.right;
            Vector2 toTarget = new Vector2(spawn.TargetPosition.x, spawn.TargetPosition.y) - context.Position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                baseDirection = toTarget.normalized;
            }

            int targetMask = burst.TargetMask != 1 ? burst.TargetMask : destination.TargetMask;
            destination.Spawn(new ProjectileSpawnCommand(
                context.Position,
                baseDirection,
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
                burst.DirectDamageEnabled,
                count: burst.Count,
                spreadDegrees: burst.SpreadDegrees));
        }
    }
}
