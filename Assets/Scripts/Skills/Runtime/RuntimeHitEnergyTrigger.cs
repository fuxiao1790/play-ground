namespace PlayGround.Skills.Runtime
{
    public sealed class RuntimeHitEnergyTrigger
    {
        public RuntimeSkillDefinition TriggeredSkill { get; set; }
        public float EnergyContributionMultiplier { get; set; } = 1f;
        public float EnergyRequirementMultiplier { get; set; } = 1f;
        public float RetentionSeconds { get; set; }
        public int AccumulatorId { get; set; } = -1;
    }
}
