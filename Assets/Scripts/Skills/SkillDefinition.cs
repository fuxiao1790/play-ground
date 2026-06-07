using System;
using PlayGround.Attack;
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
    public sealed class AoeDefinition : SkillDefinition
    {
        public BasicAoePrefab prefab;
        [Min(0.01f)] public float sizeMultiplier = 1f;
        public float damage = 10f;
        public float lifetimeSeconds;
        public float tickIntervalSeconds;
        [Min(1)] public int count = 1;
        public bool spawnAtAimPosition;
        public bool directDamageEnabled = true;

        public override SkillDefinition DeepCopy() => (AoeDefinition)MemberwiseClone();
    }
}
