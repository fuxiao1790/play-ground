using PlayGround.Skills.Runtime;

namespace PlayGround.Skills
{
    public abstract class ConversionSupport : SkillSupport
    {
        public abstract bool ConvertsToTriggeredOnly { get; }

        public virtual RuntimeSkillDefinition Compile(
            SkillDefinition definition,
            RuntimeSkillDefinition runtime,
            SkillStatSnapshot snapshot)
        {
            return runtime;
        }
    }
}
