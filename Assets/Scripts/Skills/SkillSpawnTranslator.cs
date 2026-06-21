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
            Spawn(def, origin, aimDir, aimWorldPos, combatRoot, default);
        }

        private static void Spawn(
            RuntimeSkillDefinition def,
            Vector2 origin,
            Vector2 aimDir,
            Vector2 aimWorldPos,
            CombatRoot combatRoot,
            StackEffectSnapshot stackEffect)
        {
            if (def is RuntimeProjectileDefinition proj)
                SpawnProjectile(proj, origin, aimDir, combatRoot, stackEffect);
            else if (def is RuntimeAoeDefinition aoe)
                SpawnAoe(aoe, origin, aimWorldPos, combatRoot, stackEffect);
            else if (def is RuntimeStackingSkillDefinition stacking)
                Spawn(
                    stacking.ApplicatorDefinition,
                    origin,
                    aimDir,
                    aimWorldPos,
                    combatRoot,
                    BuildStackEffectSnapshot(stacking, combatRoot));
        }

        private static void SpawnProjectile(
            RuntimeProjectileDefinition def,
            Vector2 origin,
            Vector2 aimDir,
            CombatRoot root,
            StackEffectSnapshot stackEffect)
        {
            if (root == null || def.Prefab == null || def.TypeId < 0) return;

            BasicAttackPrefab prefab = def.Prefab;
            DamageSnapshot damage = new(Mathf.Max(0f, def.Damage));
            Vector2 baseDir = aimDir.sqrMagnitude > 0f ? aimDir.normalized : Vector2.right;
            int targetMask = root.TargetMask;

            ProjectileImpactAoeSnapshot impactAoe = BuildImpactAoeSnapshot(def, targetMask);
            ProjectileImpactProjectileSnapshot impactProjectile = BuildImpactProjectileSnapshot(def, targetMask, root.Faction);
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
            CombatRoot root,
            StackEffectSnapshot stackEffect)
        {
            if (root == null || def.TypeId < 0) return;

            Vector2 center = aimWorldPos;
            DamageSnapshot damage = new(Mathf.Max(0f, def.Damage));
            int count = Mathf.Max(1, def.Count);
            AoeSpawnGeometry geometry = def.CreateSpawnGeometry();

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
            int targetMask,
            CombatFaction faction)
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
                visualScale: prefab.VisualScale,
                visualRotationDegrees: prefab.VisualRotationDegrees);
        }

        private static StackEffectSnapshot BuildStackEffectSnapshot(
            RuntimeStackingSkillDefinition stacking,
            CombatRoot root)
        {
            if (stacking == null || root == null || stacking.DebuffKey < 0)
                return default;

            if (stacking.DetonationDefinition is not RuntimeAoeDefinition aoe || aoe.TypeId < 0)
                return default;

            int threshold = Mathf.Max(1, stacking.StackThreshold);
            AoeSpawnGeometry geometry = aoe.CreateSpawnGeometry();
            return new StackEffectSnapshot
            {
                DebuffKey = stacking.DebuffKey,
                Threshold = threshold,
                Lifetime = Mathf.Max(0f, stacking.DebuffLifetimeSeconds),
                Contribution = new StackContribution
                {
                    Damage = Mathf.Max(0f, aoe.Damage) / threshold,
                    ProjectileCount = 0,
                    AreaSize = Mathf.Max(0.01f, aoe.AreaSize) / threshold
                },
                Detonation = new DetonationSnapshot
                {
                    Kind = StackDetonationKind.Aoe,
                    Faction = root.Faction,
                    TargetMask = root.TargetMask,
                    TypeId = aoe.TypeId,
                    LifetimeSeconds = aoe.LifetimeSeconds,
                    TickIntervalSeconds = aoe.TickIntervalSeconds,
                    AoeGeometry = geometry,
                    CritChance = aoe.CritChance,
                    CritMultiplier = aoe.CritMultiplier
                }
            };
        }
    }
}
