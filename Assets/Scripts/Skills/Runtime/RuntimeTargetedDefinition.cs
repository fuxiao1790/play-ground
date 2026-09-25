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
        public int EchoCount { get; set; } = 1;
        public int ChainCount { get; set; } = 1;
        public float ChainDistance { get; set; }
        public float ChainDamageFalloff { get; set; }
        public float ChainDelay { get; set; }

        // Not authored. A chain despawns when its walk ends; this is the computed backstop that
        // stops a stalled instance leaking a pooled slot. See LifetimeFor.
        public float LifetimeSeconds { get; set; }
        public float ManaCost { get; set; }
        public float ArmSeconds { get; set; }
        public bool DirectDamageEnabled { get; set; } = true;
        public bool SpawnBlocked { get; set; }
        public TargetedVfxIds VfxIds { get; set; }
        public TargetedVfxSizeComponent VfxSize { get; set; }

        // Compiled from HitEnergyTrigger; null when no hit-energy edge is attached.
        public RuntimeHitEnergyTrigger HitEnergyTrigger { get; set; }

        // Margin over the walk's worst case so a frame-rate hitch or a catch-up update can never
        // expire an instance that still has links owed to it.
        private const float LifetimeMarginSeconds = 0.5f;

        public static float LifetimeFor(int chainCount, float chainDelay) =>
            (UnityEngine.Mathf.Max(1, chainCount) * UnityEngine.Mathf.Max(0f, chainDelay))
            + LifetimeMarginSeconds;

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
