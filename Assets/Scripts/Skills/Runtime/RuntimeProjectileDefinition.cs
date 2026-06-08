using PlayGround.Skills;
using PlayGround.System.Projectile;
using UnityEngine;

namespace PlayGround.Skills.Runtime
{
    public sealed class RuntimeChildSpawnSetup
    {
        public int SpawnerId { get; set; }
        public RuntimeProjectileDefinition ChildDefinition { get; set; }
        public float IntervalSeconds { get; set; }
        public float IntervalJitterSeconds { get; set; }
        public ProjectileChildSpawnBehavior Behavior { get; set; }
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

        // Compiled from ChildSpawnTrigger; null if none.
        public RuntimeChildSpawnSetup ChildSpawnSetup { get; set; }

        // Compiled from OnImpactAoeTrigger; null if none.
        public RuntimeAoeDefinition ImpactAoeDefinition { get; set; }

        // Compiled from OnStackTrigger; null if none.
        public RuntimeStackTriggerSetup StackTriggerSetup { get; set; }

        public ProjectileChildSpawnConfig BuildChildSpawnConfig()
        {
            RuntimeChildSpawnSetup setup = ChildSpawnSetup;
            if (setup == null || setup.ChildDefinition == null || setup.ChildDefinition.TypeId < 0)
                return ProjectileChildSpawnConfig.Disabled;

            RuntimeProjectileDefinition child = setup.ChildDefinition;
            BasicAttackPrefab prefab = child.Prefab;
            return new ProjectileChildSpawnConfig(
                setup.SpawnerId,
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
                stackEffect: BuildChildStackEffect(child.StackTriggerSetup));
        }

        private static ProjectileStackEffectSnapshot BuildChildStackEffect(RuntimeStackTriggerSetup stack)
        {
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
