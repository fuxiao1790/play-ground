using System;
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
using PlayGround.System.Combat.Vfx;
using UnityEngine;
using UnityEngine.Serialization;
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
        [FormerlySerializedAs("spawnEnergyCost")]
        [Min(0f)] public float manaCost = 1f;
        [Min(0f)] public float armSeconds;
        public bool directDamageEnabled = true;
        [FormerlySerializedAs("sweptCollision")]
        public bool continuousCollision;
        public bool trackingEnabled;
        public float trackingTurnSpeedDegrees;
        public float trackingQueryIntervalSeconds;
        public float trackingInitialDelaySeconds;

        public ProjectileTrackingConfig GetTrackingConfig() =>
            new(trackingEnabled, trackingTurnSpeedDegrees,
                trackingQueryIntervalSeconds, trackingInitialDelaySeconds);
        public AudioClip SpawnSound => prefab != null ? prefab.SpawnSound : null;
        public float SpawnSoundRadius => prefab != null ? prefab.SpawnSoundRadius : 0f;

        public void OnValidate()
        {
            if (continuousCollision && trackingEnabled)
                Debug.LogError("Projectile definitions cannot enable both continuous collision and tracking.");
        }

        public override SkillDefinition DeepCopy() => (ProjectileDefinition)MemberwiseClone();
    }

    [Serializable]
    public abstract class AoeDefinitionBase : SkillDefinition
    {
        [FormerlySerializedAs("sizeMultiplier")]
        [Min(0.01f)] public float baseAreaSize = 1f;
        public float damage = 10f;
        [FormerlySerializedAs("count")]
        [Min(1)] public int echoCount = 1;
        [Min(0f)] public float scatterRadius = 0f;
        [FormerlySerializedAs("spawnEnergyCost")]
        [Min(0f)] public float manaCost = 1f;
        [Min(0f)] public float armSeconds;
        public bool directDamageEnabled = true;

        public abstract GameObject VisualPrefab { get; }
        public abstract Collider2D CollisionShape { get; }
        public abstract float VisualRotationDegrees { get; }
        public abstract VisualEffectAsset SpawnEffect { get; }
        public abstract AudioClip SpawnSound { get; }
        public abstract float SpawnSoundRadius { get; }
        public abstract VisualEffectAsset HitEffect { get; }
        public abstract VisualEffectAsset ExpireEffect { get; }
        public abstract VisualEffectAsset PulseEffect { get; }
        public abstract VisualEffectAsset ArmingEffect { get; }
        public abstract VfxDataShape SpawnEffectShape { get; }
        public abstract VfxDataShape HitEffectShape { get; }
        public abstract VfxDataShape ExpireEffectShape { get; }
        public abstract VfxDataShape PulseEffectShape { get; }
        public abstract VfxDataShape ArmingEffectShape { get; }
    }

    [Serializable]
    public sealed class AoeDefinition : AoeDefinitionBase
    {
        public BasicAoePrefab prefab;

        public override GameObject VisualPrefab => prefab != null ? prefab.gameObject : null;
        public override Collider2D CollisionShape => prefab != null ? prefab.Hurtbox : null;
        public override float VisualRotationDegrees => prefab != null ? prefab.VisualRotationDegrees : 0f;
        public override VisualEffectAsset SpawnEffect => prefab != null ? prefab.SpawnEffect : null;
        public override AudioClip SpawnSound => prefab != null ? prefab.SpawnSound : null;
        public override float SpawnSoundRadius => prefab != null ? prefab.SpawnSoundRadius : 0f;
        public override VisualEffectAsset HitEffect => prefab != null ? prefab.HitEffect : null;
        public override VisualEffectAsset ExpireEffect => prefab != null ? prefab.ExpireEffect : null;
        public override VisualEffectAsset PulseEffect => null;
        public override VisualEffectAsset ArmingEffect => prefab != null ? prefab.ArmingEffect : null;
        public override VfxDataShape SpawnEffectShape => prefab != null ? prefab.SpawnEffectShape : VfxDataShape.ImpactCircle;
        public override VfxDataShape HitEffectShape => prefab != null ? prefab.HitEffectShape : VfxDataShape.ImpactCircle;
        public override VfxDataShape ExpireEffectShape => prefab != null ? prefab.ExpireEffectShape : VfxDataShape.ImpactCircle;
        public override VfxDataShape PulseEffectShape => VfxDataShape.ImpactCircle;
        public override VfxDataShape ArmingEffectShape => prefab != null ? prefab.ArmingEffectShape : VfxDataShape.ImpactCircle;

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
        public override AudioClip SpawnSound => prefab != null ? prefab.SpawnSound : null;
        public override float SpawnSoundRadius => prefab != null ? prefab.SpawnSoundRadius : 0f;
        public override VisualEffectAsset HitEffect => prefab != null ? prefab.HitEffect : null;
        public override VisualEffectAsset ExpireEffect => prefab != null ? prefab.ExpireEffect : null;
        public override VisualEffectAsset PulseEffect => prefab != null ? prefab.PulseEffect : null;
        public override VisualEffectAsset ArmingEffect => prefab != null ? prefab.ArmingEffect : null;
        public override VfxDataShape SpawnEffectShape => prefab != null ? prefab.SpawnEffectShape : VfxDataShape.ImpactCircle;
        public override VfxDataShape HitEffectShape => prefab != null ? prefab.HitEffectShape : VfxDataShape.ImpactCircle;
        public override VfxDataShape ExpireEffectShape => prefab != null ? prefab.ExpireEffectShape : VfxDataShape.ImpactCircle;
        public override VfxDataShape PulseEffectShape => prefab != null ? prefab.PulseEffectShape : VfxDataShape.ImpactCircle;
        public override VfxDataShape ArmingEffectShape => prefab != null ? prefab.ArmingEffectShape : VfxDataShape.ImpactCircle;

        public override SkillDefinition DeepCopy() => (LingeringAoeDefinition)MemberwiseClone();
    }

    // One targeted variant, not two. A chain's lifetime is its walk: the instance expires the
    // instant it uses its last chain, so there is no authored lifetime and no tick interval to
    // reconcile against the per-link delay. Repeated chains come from an interval trigger on a
    // projectile or lingering AOE source, the same way every other repeating child does.
    [Serializable]
    public sealed class TargetedDefinition : SkillDefinition
    {
        public TargetedPrefab prefab;
        [Min(0f)] public float damage = 10f;
        [Min(0f)] public float manaCost = 1f;
        public bool directDamageEnabled = true;
        [FormerlySerializedAs("count")]
        [Min(1)] public int echoCount = 1;
        [FormerlySerializedAs("maxTargets")]
        [Min(1)] public int chainCount = 1;
        [FormerlySerializedAs("chainRadius")]
        [Min(0f)] public float chainDistance = 4f;
        [FormerlySerializedAs("chainDelaySeconds")]
        [Min(0f)] public float chainDelay;
        [Min(0f)] public float chainDamageFalloff;
        [Min(0f)] public float armSeconds;

        public TargetedPrefab Prefab => prefab;
        public AudioClip SpawnSound => prefab != null ? prefab.SpawnSound : null;
        public float SpawnSoundRadius => prefab != null ? prefab.SpawnSoundRadius : 0f;

        public override SkillDefinition DeepCopy() => (TargetedDefinition)MemberwiseClone();
    }
}
