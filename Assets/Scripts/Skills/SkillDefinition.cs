using System;
using PlayGround.System.Projectile;
using UnityEngine;

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
        public BasicAoePrefab prefab;
        [Min(0.01f)] public float sizeMultiplier = 1f;
        public float damage = 10f;
        [Min(1)] public int count = 1;
        public bool spawnAtAimPosition;
        public bool directDamageEnabled = true;
    }

    [Serializable]
    public sealed class AoeDefinition : AoeDefinitionBase
    {
        public override SkillDefinition DeepCopy() => (AoeDefinition)MemberwiseClone();
    }

    [Serializable]
    public sealed class LingeringAoeDefinition : AoeDefinitionBase
    {
        [Min(0f)] public float lifetimeSeconds = 1f;
        [Min(0f)] public float tickIntervalSeconds = 0.25f;

        public override SkillDefinition DeepCopy() => (LingeringAoeDefinition)MemberwiseClone();
    }
}
