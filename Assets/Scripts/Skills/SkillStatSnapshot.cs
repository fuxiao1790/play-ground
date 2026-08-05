using PlayGround.Common.Stats;

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
            float areaSizeMultiplier = 1f,
            float baseEnergyGain = 0f,
            float increasedEnergyGain = 1f,
            float energyGainMultiplier = 1f)
        {
            IncreasedRatePercent = increasedRatePercent;
            DamageMultiplier = damageMultiplier;
            CritChance = critChance;
            CritMultiplier = critMultiplier;
            AreaSizeMultiplier = areaSizeMultiplier;
            BaseEnergyGain = baseEnergyGain;
            IncreasedEnergyGain = increasedEnergyGain;
            EnergyGainMultiplier = energyGainMultiplier;
        }

        public float IncreasedRatePercent { get; }
        public float DamageMultiplier { get; }
        public float CritChance { get; }
        public float CritMultiplier { get; }
        public float AreaSizeMultiplier { get; }
        public float BaseEnergyGain { get; }
        public float IncreasedEnergyGain { get; }
        public float EnergyGainMultiplier { get; }
    }

    public static class SkillStatAggregator
    {
        public static SkillStatSnapshot Aggregate(SkillLoadout loadout, UnitStatSheet sheet) =>
            sheet == null
                ? SkillStatSnapshot.Identity
                : new SkillStatSnapshot(
                    sheet.IncreasedRatePercent,
                    sheet.DamageMultiplier,
                    sheet.CritChance,
                    sheet.CritMultiplier,
                    sheet.AreaSizeMultiplier,
                    sheet.BaseEnergyGain,
                    sheet.IncreasedEnergyGain,
                    sheet.EnergyGainMultiplier);
    }
}
