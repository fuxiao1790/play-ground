namespace PlayGround.Skills.Modifiers
{
    public readonly struct AddedSink
    {
        private readonly StatModifierAccumulator accumulator;

        public AddedSink(StatModifierAccumulator accumulator)
        {
            this.accumulator = accumulator;
        }

        public void Add(SkillStat stat, float amount)
        {
            accumulator.AddAdded(stat, amount);
        }
    }

    public readonly struct IncreasedSink
    {
        private readonly StatModifierAccumulator accumulator;

        public IncreasedSink(StatModifierAccumulator accumulator)
        {
            this.accumulator = accumulator;
        }

        public void AddPercent(SkillStat stat, float percent)
        {
            accumulator.AddIncreasedPercent(stat, percent);
        }

        public void AddFactor(SkillStat stat, float factor)
        {
            accumulator.AddIncreasedFactor(stat, factor);
        }
    }

    public readonly struct MultiplierSink
    {
        private readonly StatModifierAccumulator accumulator;

        public MultiplierSink(StatModifierAccumulator accumulator)
        {
            this.accumulator = accumulator;
        }

        public void Add(SkillStat stat, float mul)
        {
            accumulator.AddMultiplier(stat, mul);
        }
    }
}
