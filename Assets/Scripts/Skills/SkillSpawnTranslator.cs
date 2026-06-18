using PlayGround.Common;
using PlayGround.Skills.Runtime;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Skills
{
    public static class SkillSpawnTranslator
    {
        public static void Spawn(
            RuntimeSkillDefinition def,
            Vector2 origin,
            Vector2 aimDir,
            Vector2 aimWorldPos,
            CombatRoot combatRoot)
        {
            if (def is RuntimeProjectileDefinition proj)
                SpawnProjectile(proj, origin, aimDir, combatRoot);
            else if (def is RuntimeAoeDefinition aoe)
                SpawnAoe(aoe, origin, aimWorldPos, combatRoot);
        }

        private static void SpawnProjectile(
            RuntimeProjectileDefinition def,
            Vector2 origin,
            Vector2 aimDir,
            CombatRoot root)
        {
            if (root == null || def.Prefab == null || def.TypeId < 0) return;

            BasicAttackPrefab prefab = def.Prefab;
            DamageSnapshot damage = new(Mathf.Max(0f, def.Damage));
            Vector2 baseDir = aimDir.sqrMagnitude > 0f ? aimDir.normalized : Vector2.right;
            int targetMask = root.TargetMask;

            ProjectileImpactAoeSnapshot impactAoe = BuildImpactAoeSnapshot(def, targetMask);
            CombatStatusEffectSnapshot stackEffect = BuildStackEffectSnapshot(def);
            ProjectileImpactProjectileSnapshot impactProjectile = BuildImpactProjectileSnapshot(def, targetMask);
            ProjectileChildSpawnConfig childSpawn = def.BuildChildSpawnConfig();

            root.Spawn(new ProjectileSpawnRequest(
                origin,
                baseDir,
                def.Speed,
                def.Lifetime,
                prefab.Radius,
                prefab.HalfExtents,
                prefab.RotationRadians,
                damage,
                prefab.ShapeType,
                def.TypeId,
                targetMask,
                def.PierceCount,
                def.RepeatHitCooldown,
                def.Tracking,
                childSpawn,
                def.DirectDamageEnabled,
                default,
                impactAoe,
                stackEffect,
                impactProjectile,
                def.CritChance,
                def.CritMultiplier,
                def.Count,
                def.SpreadDegrees,
                def.JitterDegrees));
        }

        private static void SpawnAoe(
            RuntimeAoeDefinition def,
            Vector2 origin,
            Vector2 aimWorldPos,
            CombatRoot root)
        {
            if (root == null || def.TypeId < 0) return;

            Vector2 center = aimWorldPos;
            DamageSnapshot damage = new(Mathf.Max(0f, def.Damage));
            int count = Mathf.Max(1, def.Count);
            AoeSpawnGeometry geometry = def.CreateSpawnGeometry();
            CombatStatusEffectSnapshot stackEffect = BuildAoeStackEffectSnapshot(def);

            for (int i = 0; i < count; i++)
            {
                root.Spawn(new AoeSpawnRequest(
                    def.TypeId,
                    center,
                    root.TargetMask,
                    damage,
                    def.LifetimeSeconds,
                    def.TickIntervalSeconds,
                    geometry,
                    critChance: def.CritChance,
                    critMultiplier: def.CritMultiplier,
                    stackEffect: stackEffect));
            }
        }

        private static CombatStatusEffectSnapshot BuildAoeStackEffectSnapshot(RuntimeAoeDefinition def)
        {
            RuntimeStackTriggerSetup stack = def.StackTriggerSetup;
            if (stack == null || stack.AoeDefinition == null || stack.AoeDefinition.TypeId < 0)
                return default;

            return new CombatStatusEffectSnapshot(
                stack.DebuffStatusId,
                Mathf.Max(1, stack.StacksPerHit),
                Mathf.Max(1, stack.StackThreshold),
                stack.AoeDefinition.TypeId,
                Mathf.Max(0f, stack.AoeDefinition.Damage),
                stack.AoeDefinition.LifetimeSeconds,
                stack.AoeDefinition.TickIntervalSeconds,
                stack.AoeDefinition.CreateSpawnGeometry());
        }

        private static ProjectileImpactAoeSnapshot BuildImpactAoeSnapshot(
            RuntimeProjectileDefinition def,
            int targetMask)
        {
            RuntimeAoeDefinition impact = def.ImpactAoeDefinition;
            if (impact == null || impact.TypeId < 0)
                return default;

            return new ProjectileImpactAoeSnapshot(
                impact.TypeId,
                targetMask,
                Mathf.Max(0f, impact.Damage),
                impact.LifetimeSeconds,
                impact.TickIntervalSeconds,
                impact.CreateSpawnGeometry(),
                impact.CritChance,
                impact.CritMultiplier);
        }

        private static ProjectileImpactProjectileSnapshot BuildImpactProjectileSnapshot(
            RuntimeProjectileDefinition def,
            int targetMask)
        {
            RuntimeProjectileDefinition impact = def.ImpactProjectileDefinition;
            if (impact == null || impact.TypeId < 0)
                return default;

            BasicAttackPrefab prefab = impact.Prefab;
            if (prefab == null)
                return default;

            return new ProjectileImpactProjectileSnapshot(
                impact.TypeId,
                targetMask,
                Mathf.Max(1, impact.Count),
                impact.SpreadDegrees,
                impact.Speed,
                impact.Lifetime,
                prefab.Radius,
                prefab.HalfExtents,
                prefab.RotationRadians,
                prefab.ShapeType,
                new DamageSnapshot(Mathf.Max(0f, impact.Damage)),
                impact.DirectDamageEnabled,
                impact.PierceCount,
                impact.RepeatHitCooldown,
                impact.Tracking,
                BuildImpactAoeSnapshot(impact, targetMask),
                BuildStackEffectSnapshot(impact),
                prefab.VisualScale,
                prefab.VisualRotationDegrees);
        }

        private static CombatStatusEffectSnapshot BuildStackEffectSnapshot(RuntimeProjectileDefinition def)
        {
            RuntimeStackTriggerSetup stack = def.StackTriggerSetup;
            if (stack == null || stack.AoeDefinition == null || stack.AoeDefinition.TypeId < 0)
                return default;

            return new CombatStatusEffectSnapshot(
                stack.DebuffStatusId,
                Mathf.Max(1, stack.StacksPerHit),
                Mathf.Max(1, stack.StackThreshold),
                stack.AoeDefinition.TypeId,
                Mathf.Max(0f, stack.AoeDefinition.Damage),
                stack.AoeDefinition.LifetimeSeconds,
                stack.AoeDefinition.TickIntervalSeconds,
                stack.AoeDefinition.CreateSpawnGeometry());
        }
    }
}
