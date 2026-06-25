using PlayGround.Skills;
using PlayGround.System.Aoe;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.Skills.Runtime
{
    public sealed class RuntimeAoeDefinition : RuntimeSkillDefinition
    {
        public GameObject VisualPrefab { get; set; }
        public Collider2D CollisionShape { get; set; }
        [Min(0.01f)] public float AreaSize { get; set; } = 1f;
        public float VisualRotationDegrees { get; set; }
        public VisualEffectAsset SpawnEffect { get; set; }
        public VisualEffectAsset HitEffect { get; set; }
        public VisualEffectAsset ExpireEffect { get; set; }
        public VisualEffectAsset PulseEffect { get; set; }
        public float LifetimeSeconds { get; set; }
        public float TickIntervalSeconds { get; set; }
        public int Count { get; set; } = 1;
        public bool DirectDamageEnabled { get; set; } = true;

        // Compiled from ProjectileIntervalSpawnTrigger on lingering AOEs; null if none.
        public RuntimeChildSpawnSetup ChildSpawnSetup { get; set; }

        // Compiled from AoeIntervalSpawnTrigger on lingering AOEs; null if none.
        public RuntimeAoeIntervalSpawnSetup AoeIntervalSpawnSetup { get; set; }

        // Compiled from OnAoeHitSpawnTrigger; null if none. The concrete ECS
        // snapshot is bounded by AoeOnHitSpawnSnapshot.MaxStackChainLinks.
        public RuntimeSkillDefinition OnHitAoeSpawnDefinition { get; set; }

        // Compiled from OnImpactProjectileTrigger on an AOE source; null if none.
        // Fires as an AoeProjectileBurstSnapshot when this AOE hits a target.
        public RuntimeProjectileDefinition OnHitProjectileSpawnDefinition { get; set; }

        // Compiled from StackTrigger; null if none.
        public RuntimeStackingDetonation StackingDetonation { get; set; }

        public AoeSpawnGeometry CreateSpawnGeometry()
        {
            return AoeSpawnGeometry.FromTemplate(
                VisualPrefab,
                CollisionShape,
                AreaSize,
                VisualRotationDegrees);
        }

        public AoeTypeDefinition CreateTypeDefinition()
        {
            var def = new AoeTypeDefinition();
            def.Configure(
                VisualPrefab,
                CollisionShape,
                VisualRotationDegrees,
                0,
                SpawnEffect,
                HitEffect,
                ExpireEffect,
                PulseEffect);
            return def;
        }
    }
}
