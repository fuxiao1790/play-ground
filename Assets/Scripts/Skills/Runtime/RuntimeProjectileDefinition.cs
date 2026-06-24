using PlayGround.Skills;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace PlayGround.Skills.Runtime
{
    public sealed class RuntimeChildSpawnSetup
    {
        public int JitterSeed { get; set; }
        public RuntimeProjectileDefinition ChildDefinition { get; set; }
        public float IntervalSeconds { get; set; }
        public float IntervalJitterSeconds { get; set; }
        public ProjectileChildSpawnBehavior Behavior { get; set; }
        public Hash128 TemplateKey { get; set; }
        public ProjectileSpawnTemplateData TemplateData { get; set; }
        public ProjectileChildSpawnConfig SpawnConfig { get; set; }
        public bool HasRegisteredTemplate => !TemplateKey.Equals(default(Hash128));
    }

    public sealed class RuntimeAoeIntervalSpawnSetup
    {
        public int JitterSeed { get; set; }
        public RuntimeAoeDefinition ChildDefinition { get; set; }
        public float IntervalSeconds { get; set; }
        public float IntervalJitterSeconds { get; set; }
        public int Count { get; set; }
        public float SideSpreadDegrees { get; set; }
        public Hash128 TemplateKey { get; set; }
        public AoeSpawnTemplateData TemplateData { get; set; }
        public bool HasRegisteredTemplate => !TemplateKey.Equals(default(Hash128));
    }

    public sealed class RuntimeProjectileDefinition : RuntimeSkillDefinition
    {
        public BasicAttackPrefab Prefab { get; set; }
        public float Speed { get; set; } = 16f;
        public float Lifetime { get; set; } = 1.5f;
        public int Count { get; set; } = 1;
        public float SpreadDegrees { get; set; }
        public float JitterDegrees { get; set; }
        public int PierceCount { get; set; }
        public float RepeatHitCooldown { get; set; }
        public bool DirectDamageEnabled { get; set; } = true;
        public ProjectileTrackingConfig Tracking { get; set; }

        // Compiled from ProjectileIntervalSpawnTrigger; null if none.
        public RuntimeChildSpawnSetup ChildSpawnSetup { get; set; }

        // Compiled from AoeIntervalSpawnTrigger; null if none.
        public RuntimeAoeIntervalSpawnSetup AoeIntervalSpawnSetup { get; set; }

        // Compiled from OnImpactAoeTrigger; null if none.
        public RuntimeAoeDefinition ImpactAoeDefinition { get; set; }

        // Compiled from OnImpactProjectileTrigger; null if none.
        public RuntimeProjectileDefinition ImpactProjectileDefinition { get; set; }

        // Compiled from StackTrigger; null if none.
        public RuntimeStackingDetonation StackingDetonation { get; set; }

        public ProjectileChildSpawnConfig BuildChildSpawnConfig(StackEffectSnapshot stackEffect = default)
        {
            RuntimeChildSpawnSetup setup = ChildSpawnSetup;
            if (setup == null || setup.ChildDefinition == null || setup.ChildDefinition.TypeId < 0)
                return ProjectileChildSpawnConfig.Disabled;

            if (setup.HasRegisteredTemplate)
                return setup.SpawnConfig;

            RuntimeProjectileDefinition child = setup.ChildDefinition;
            BasicAttackPrefab prefab = child.Prefab;
            return new ProjectileChildSpawnConfig(
                setup.JitterSeed,
                child.TypeId,
                Mathf.Max(0.01f, setup.IntervalSeconds),
                setup.IntervalJitterSeconds,
                child.Speed,
                child.Lifetime,
                prefab.Radius,
                prefab.HalfExtents,
                prefab.ShapeType,
                prefab.RotationRadians,
                new PlayGround.Common.DamageSnapshot(Mathf.Max(0f, child.Damage)),
                0,
                child.DirectDamageEnabled,
                child.PierceCount,
                child.RepeatHitCooldown,
                prefab.VisualScale,
                prefab.VisualRotationDegrees,
                child.Tracking,
                setup.Behavior,
                impactAoe: BuildChildImpactAoeSnapshot(child),
                stackEffect: stackEffect,
                impactProjectile: BuildChildImpactProjectileSnapshot(child),
                templateKey: setup.TemplateKey);
        }

        private static ProjectileImpactAoeSnapshot BuildChildImpactAoeSnapshot(RuntimeProjectileDefinition child)
        {
            RuntimeAoeDefinition impact = child.ImpactAoeDefinition;
            if (impact == null || impact.TypeId < 0)
                return default;
            return new ProjectileImpactAoeSnapshot(
                impact.TypeId, 0, Mathf.Max(0f, impact.Damage),
                impact.LifetimeSeconds, impact.TickIntervalSeconds,
                impact.CreateSpawnGeometry(),
                impact.CritChance, impact.CritMultiplier);
        }

        private static ProjectileImpactProjectileSnapshot BuildChildImpactProjectileSnapshot(RuntimeProjectileDefinition child)
        {
            RuntimeProjectileDefinition impact = child.ImpactProjectileDefinition;
            if (impact == null || impact.TypeId < 0)
                return default;
            BasicAttackPrefab prefab = impact.Prefab;
            if (prefab == null)
                return default;
            return new ProjectileImpactProjectileSnapshot(
                impact.TypeId, 0, Mathf.Max(1, impact.Count), impact.SpreadDegrees,
                impact.Speed, impact.Lifetime, prefab.Radius, prefab.HalfExtents,
                prefab.RotationRadians, prefab.ShapeType,
                new PlayGround.Common.DamageSnapshot(Mathf.Max(0f, impact.Damage)),
                impact.DirectDamageEnabled, impact.PierceCount, impact.RepeatHitCooldown,
                impact.Tracking,
                BuildChildImpactAoeSnapshot(impact),
                visualScale: prefab.VisualScale,
                visualRotationDegrees: prefab.VisualRotationDegrees);
        }
    }
}
