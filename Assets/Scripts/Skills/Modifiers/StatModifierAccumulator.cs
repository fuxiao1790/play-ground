namespace PlayGround.Skills.Modifiers
{
    public enum MultiplierTiming
    {
        Pre,
        Post,
    }

    public sealed class StatModifierAccumulator
    {
        private static readonly int StatCount = System.Enum.GetValues(typeof(SkillStat)).Length;

        private readonly float[] added = new float[StatCount];
        private readonly float[] increased = new float[StatCount];
        private readonly float[] preMul = new float[StatCount];
        private readonly float[] postMul = new float[StatCount];

        public StatModifierAccumulator()
        {
            for (var i = 0; i < StatCount; i++)
            {
                preMul[i] = 1f;
                postMul[i] = 1f;
            }
        }

        public void AddAdded(SkillStat stat, float amount)
        {
            added[(int)stat] += amount;
        }

        public void AddIncreased(SkillStat stat, float percent)
        {
            increased[(int)stat] += percent;
        }

        public void AddMultiplier(SkillStat stat, float mul, MultiplierTiming timing)
        {
            var index = (int)stat;
            if (timing == MultiplierTiming.Pre)
            {
                preMul[index] *= mul;
                return;
            }

            postMul[index] *= mul;
        }

        public float Resolve(SkillStat stat, float baseValue)
        {
            var index = (int)stat;
            var effectiveBase = (baseValue * preMul[index]) + added[index];
            return effectiveBase * (1f + increased[index]) * postMul[index];
        }
    }
}
