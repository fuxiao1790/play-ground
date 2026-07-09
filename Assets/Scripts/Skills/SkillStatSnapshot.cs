namespace PlayGround.Skills
{
    public readonly struct SkillStatSnapshot
    {
        public static readonly SkillStatSnapshot Identity = new(0f, 1f, 0f, 1.5f, 1f);

        public SkillStatSnapshot(
            float increasedRatePercent,
            float damageMultiplier,
            float critChance,
            float critMultiplier,
            float areaSizeMultiplier = 1f)
        {
            IncreasedRatePercent = increasedRatePercent;
            DamageMultiplier = damageMultiplier;
            CritChance = critChance;
            CritMultiplier = critMultiplier;
            AreaSizeMultiplier = areaSizeMultiplier;
        }

        public float IncreasedRatePercent { get; }
        public float DamageMultiplier { get; }
        public float CritChance { get; }
        public float CritMultiplier { get; }
        public float AreaSizeMultiplier { get; }
    }

    public static class SkillStatAggregator
    {
        // Stub: returns identity until real stat sources (CharacterStats, items, buffs) exist.
        public static SkillStatSnapshot Aggregate(SkillLoadout loadout) =>
            SkillStatSnapshot.Identity;
    }
}
