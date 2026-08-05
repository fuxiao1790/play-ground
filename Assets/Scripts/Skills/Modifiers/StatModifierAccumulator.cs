using PlayGround.Common.Modifiers;

namespace PlayGround.Skills.Modifiers
{
    public sealed class StatModifierAccumulator
    {
        private static readonly int StatCount = global::System.Enum.GetValues(typeof(SkillStat)).Length;

        private readonly float[] added = new float[StatCount];
        private readonly float[] increased = new float[StatCount];
        private readonly float[] mul = new float[StatCount];

        public StatModifierAccumulator()
        {
            for (var i = 0; i < StatCount; i++)
            {
                increased[i] = 1f;
                mul[i] = 1f;
            }
        }

        public void AddAdded(SkillStat stat, float amount)
        {
            added[(int)stat] += amount;
        }

        public void AddIncreasedPercent(SkillStat stat, float percent)
        {
            increased[(int)stat] += percent;
        }

        public void AddIncreasedFactor(SkillStat stat, float factor)
        {
            increased[(int)stat] += factor - 1f;
        }

        public void AddMultiplier(SkillStat stat, float multiplier)
        {
            mul[(int)stat] *= multiplier;
        }

        public float Resolve(SkillStat stat, float baseValue)
        {
            var index = (int)stat;
            return StatFold.Resolve(baseValue, added[index], increased[index], mul[index]);
        }
    }
}
