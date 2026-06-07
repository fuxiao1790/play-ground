using System;
using PlayGround.Mob;
using UnityEngine;

namespace PlayGround.Skills
{
    [Serializable]
    public abstract class TriggerLink
    {
        public SkillSet source;
        public SkillSet target;
    }

    [Serializable]
    public sealed class ChildSpawnTrigger : TriggerLink
    {
        [Min(0.01f)] public float intervalSeconds = 0.5f;
        [Min(1)] public int spawnCount = 1;
        [Range(0f, 180f)] public float sideSpreadDegrees = 30f;
    }

    [Serializable]
    public sealed class OnImpactAoeTrigger : TriggerLink { }

    [Serializable]
    public sealed class OnExpireTrigger : TriggerLink { }

    [Serializable]
    public sealed class OnStackTrigger : TriggerLink
    {
        public MobDebuffStatus debuffStatus = MobDebuffStatus.Volatile;
        [Min(1)] public int stacksPerHit = 1;
        [Min(1)] public int stackThreshold = 3;
    }
}
