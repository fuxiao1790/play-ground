using PlayGround.Skills.Runtime;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Stacking", fileName = "StackingSupport")]
    public sealed class StackingSupport : ConversionSupport
    {
        [SerializeField, Min(1)] private int stackThreshold = 3;
        [SerializeField, Min(0f)] private float debuffLifetimeSeconds = 4f;
        [SerializeField, Min(1)] private int stacksPerHit = 1;

        public int StackThreshold => stackThreshold;
        public float DebuffLifetimeSeconds => debuffLifetimeSeconds;
        public int StacksPerHit => stacksPerHit;

        public override bool ConvertsToTriggeredOnly => true;

        public override RuntimeSkillDefinition Compile(
            SkillDefinition definition,
            RuntimeSkillDefinition runtime,
            SkillStatSnapshot snapshot)
        {
            if (runtime == null || runtime is RuntimeStackingDetonation)
                return runtime;

            return new RuntimeStackingDetonation
            {
                Detonation = runtime,
                StackThreshold = Mathf.Max(1, stackThreshold),
                DebuffLifetimeSeconds = Mathf.Max(0f, debuffLifetimeSeconds),
                StacksPerHit = Mathf.Max(1, stacksPerHit),
            };
        }
    }
}
