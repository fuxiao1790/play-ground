using PlayGround.Common;
using PlayGround.Skills.Runtime;
using PlayGround.System.Aoe;
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
            ProjectileRoot projectileRoot,
            AoeRoot aoeRoot)
        {
            if (def is RuntimeProjectileDefinition proj)
                SpawnProjectile(proj, origin, aimDir, projectileRoot);
            else if (def is RuntimeAoeDefinition aoe)
                SpawnAoe(aoe, origin, aimWorldPos, aoeRoot);
        }

        private static void SpawnProjectile(
            RuntimeProjectileDefinition def,
            Vector2 origin,
            Vector2 aimDir,
            ProjectileRoot root)
        {
            if (root == null || def.Prefab == null || def.TypeId < 0) return;

            BasicAttackPrefab prefab = def.Prefab;
            DamageSnapshot damage = new(Mathf.Max(0f, def.Damage));
            int count = Mathf.Max(1, def.Count);
            float spread = count > 1 ? def.SpreadDegrees : 0f;
            Vector2 baseDir = aimDir.sqrMagnitude > 0f ? aimDir.normalized : Vector2.right;
            int targetMask = root.TargetMask;

            ProjectileImpactAoeSnapshot impactAoe = BuildImpactAoeSnapshot(def, targetMask);
            ProjectileStackEffectSnapshot stackEffect = BuildStackEffectSnapshot(def);
            ProjectileChildSpawnConfig childSpawn = def.BuildChildSpawnConfig();

            for (int i = 0; i < count; i++)
            {
                float angle = count > 1 ? -spread * 0.5f + spread / (count - 1) * i : 0f;
                if (def.JitterDegrees > 0f)
                    angle += Random.Range(-def.JitterDegrees, def.JitterDegrees);

                Vector2 dir = angle == 0f ? baseDir : (Vector2)(Quaternion.Euler(0f, 0f, angle) * baseDir);

                root.Spawn(new ProjectileSpawnCommand(
                    origin,
                    dir,
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
                    def.CritChance,
                    def.CritMultiplier));
            }
        }

        private static void SpawnAoe(
            RuntimeAoeDefinition def,
            Vector2 origin,
            Vector2 aimWorldPos,
            AoeRoot root)
        {
            if (root == null || def.TypeId < 0) return;

            Vector2 center = def.SpawnAtAimPosition ? aimWorldPos : origin;
            DamageSnapshot damage = new(Mathf.Max(0f, def.Damage));
            int count = Mathf.Max(1, def.Count);

            for (int i = 0; i < count; i++)
            {
                root.Spawn(new AoeSpawnCommand(
                    def.TypeId,
                    center,
                    root.TargetMask,
                    damage,
                    def.LifetimeSeconds,
                    def.TickIntervalSeconds,
                    critChance: def.CritChance,
                    critMultiplier: def.CritMultiplier));
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
                impact.TickIntervalSeconds);
        }

        private static ProjectileStackEffectSnapshot BuildStackEffectSnapshot(RuntimeProjectileDefinition def)
        {
            RuntimeStackTriggerSetup stack = def.StackTriggerSetup;
            if (stack == null || stack.AoeDefinition == null || stack.AoeDefinition.TypeId < 0)
                return default;

            return new ProjectileStackEffectSnapshot(
                stack.DebuffStatusId,
                Mathf.Max(1, stack.StacksPerHit),
                Mathf.Max(1, stack.StackThreshold),
                stack.AoeDefinition.TypeId,
                Mathf.Max(0f, stack.AoeDefinition.Damage),
                stack.AoeDefinition.LifetimeSeconds,
                stack.AoeDefinition.TickIntervalSeconds);
        }
    }
}
