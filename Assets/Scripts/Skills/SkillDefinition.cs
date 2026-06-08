using System;
using PlayGround.System.Projectile;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.Skills
{
    [Serializable]
    public abstract class SkillDefinition
    {
        public abstract SkillDefinition DeepCopy();
    }

    [Serializable]
    public sealed class ProjectileDefinition : SkillDefinition
    {
        public BasicAttackPrefab prefab;
        public float speed = 16f;
        public float lifetime = 1.5f;
        public float damage = 10f;
        public int count = 1;
        public float spreadDegrees;
        public float jitterDegrees;
        public int pierceCount;
        public float repeatHitCooldown;
        public bool directDamageEnabled = true;
        public bool trackingEnabled;
        public float trackingRange;
        public float trackingTurnSpeedDegrees;
        public float trackingQueryIntervalSeconds;
        public float trackingInitialDelaySeconds;

        public ProjectileTrackingConfig GetTrackingConfig() =>
            new(trackingEnabled, trackingRange, trackingTurnSpeedDegrees,
                trackingQueryIntervalSeconds, trackingInitialDelaySeconds);

        public override SkillDefinition DeepCopy() => (ProjectileDefinition)MemberwiseClone();
    }

    [Serializable]
    public abstract class AoeDefinitionBase : SkillDefinition
    {
        [Min(0.01f)] public float sizeMultiplier = 1f;
        public float damage = 10f;
        [Min(1)] public int count = 1;
        public bool spawnAtAimPosition;
        public bool directDamageEnabled = true;

        public abstract GameObject VisualPrefab { get; }
        public abstract Collider2D CollisionShape { get; }
        public abstract float VisualRotationDegrees { get; }
        public abstract VisualEffectAsset SpawnEffect { get; }
        public abstract VisualEffectAsset HitEffect { get; }
        public abstract VisualEffectAsset ExpireEffect { get; }
        public abstract VisualEffectAsset PulseEffect { get; }
    }

    [Serializable]
    public sealed class AoeDefinition : AoeDefinitionBase
    {
        public BasicAoePrefab prefab;

        public override GameObject VisualPrefab => prefab != null ? prefab.gameObject : null;
        public override Collider2D CollisionShape => prefab != null ? prefab.Hurtbox : null;
        public override float VisualRotationDegrees => prefab != null ? prefab.VisualRotationDegrees : 0f;
        public override VisualEffectAsset SpawnEffect => prefab != null ? prefab.SpawnEffect : null;
        public override VisualEffectAsset HitEffect => prefab != null ? prefab.HitEffect : null;
        public override VisualEffectAsset ExpireEffect => prefab != null ? prefab.ExpireEffect : null;
        public override VisualEffectAsset PulseEffect => null;

        public override SkillDefinition DeepCopy() => (AoeDefinition)MemberwiseClone();
    }

    [Serializable]
    public sealed class LingeringAoeDefinition : AoeDefinitionBase
    {
        public LingeringAoePrefab prefab;
        [Min(0f)] public float lifetimeSeconds = 1f;
        [Min(0f)] public float tickIntervalSeconds = 0.25f;

        public override GameObject VisualPrefab => prefab != null ? prefab.gameObject : null;
        public override Collider2D CollisionShape => prefab != null ? prefab.Hurtbox : null;
        public override float VisualRotationDegrees => prefab != null ? prefab.VisualRotationDegrees : 0f;
        public override VisualEffectAsset SpawnEffect => prefab != null ? prefab.SpawnEffect : null;
        public override VisualEffectAsset HitEffect => prefab != null ? prefab.HitEffect : null;
        public override VisualEffectAsset ExpireEffect => prefab != null ? prefab.ExpireEffect : null;
        public override VisualEffectAsset PulseEffect => prefab != null ? prefab.PulseEffect : null;

        public override SkillDefinition DeepCopy() => (LingeringAoeDefinition)MemberwiseClone();
    }
}
