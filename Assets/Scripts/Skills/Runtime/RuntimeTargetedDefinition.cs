using PlayGround.Skills;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Targeted;

namespace PlayGround.Skills.Runtime
{
    public sealed class RuntimeTargetedDefinition : RuntimeSkillDefinition
    {
        public TargetedPrefab Prefab { get; set; }
        public int Count { get; set; } = 1;
        public float AcquireRadius { get; set; }
        public int MaxTargets { get; set; } = 1;
        public float ChainRadius { get; set; }
        public float ChainDamageFalloff { get; set; }
        public float ChainDelaySeconds { get; set; }
        public float LifetimeSeconds { get; set; }
        public float TickIntervalSeconds { get; set; }
        public float ManaCost { get; set; }
        public float ArmSeconds { get; set; }
        public bool DirectDamageEnabled { get; set; } = true;
        public bool SpawnBlocked { get; set; }
        public TargetedVfxIds VfxIds { get; set; }
        public TargetedVfxSizeComponent VfxSize { get; set; }

        // Compiled from targeted-as-source trigger links in the next trigger task; declared now
        // so the runtime shape remains stable while task 010 adds the links.
        public RuntimeAoeDefinition OnHitAoeSpawnDefinition { get; set; }
        public RuntimeProjectileDefinition OnHitProjectileSpawnDefinition { get; set; }

        // Compiled from StackTrigger; null when no stacking detonation is attached.
        public RuntimeStackingDetonation StackingDetonation { get; set; }

        private TargetedTypeDefinition typeDefinition;

        public TargetedTypeDefinition CreateTypeDefinition()
        {
            if (typeDefinition != null)
                return typeDefinition;

            typeDefinition = new TargetedTypeDefinition();
            typeDefinition.Configure(
                Prefab != null ? Prefab.gameObject : null,
                Prefab?.VisualRotationDegrees ?? 0f,
                0,
                Prefab?.SpawnEffect,
                Prefab?.HitEffect,
                Prefab?.ExpireEffect,
                Prefab?.LinkEffect,
                Prefab?.ArmingEffect,
                VfxSize.EffectSize,
                VfxSize.LinkWidth);
            return typeDefinition;
        }
    }
}
