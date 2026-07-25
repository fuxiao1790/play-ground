using PlayGround.Skills;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Authoring;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Projectiles;
using Hash128 = Unity.Entities.Hash128;

namespace PlayGround.Skills.Runtime
{
    public sealed class RuntimeChildSpawnSetup
    {
        public int JitterSeed { get; set; }
        public RuntimeProjectileDefinition ChildDefinition { get; set; }
        public float EnergyPerSecond { get; set; }
        public float EnergyThreshold { get; set; }
        public float EnergyThresholdJitter { get; set; }
        public ProjectileChildSpawnBehavior Behavior { get; set; }
        public Hash128 TemplateKey { get; set; }
    }

    public sealed class RuntimeAoeIntervalSpawnSetup
    {
        public int JitterSeed { get; set; }
        public RuntimeAoeDefinition ChildDefinition { get; set; }
        public float EnergyPerSecond { get; set; }
        public float EnergyThreshold { get; set; }
        public float EnergyThresholdJitter { get; set; }
        // Per-tick burst count; maps to the child AOE echo count when building templates.
        public int Count { get; set; }
        public float ScatterRadius { get; set; }
        public Hash128 TemplateKey { get; set; }
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
        public float ManaCost { get; set; }
        public float ArmSeconds { get; set; }
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
    }
}
