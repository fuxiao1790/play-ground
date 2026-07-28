using PlayGround.Skills.Modifiers;

namespace PlayGround.Skills
{
    public interface IBaseValueModifier
    {
        void CollectAdded(AddedSink sink);
    }

    public interface IIncreasedModifier
    {
        void CollectIncreases(IncreasedSink sink);
    }

    public interface IMultiplierModifier
    {
        void CollectMultipliers(MultiplierSink sink);
    }

    public interface IProjectileBehaviorModifier
    {
        void ApplyToProjectile(ProjectileBehaviorContext ctx);
    }

    public interface IAoeBehaviorModifier
    {
        void ApplyToAoe(AoeBehaviorContext ctx);
    }

}
