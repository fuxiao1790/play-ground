using PlayGround.Skills.Modifiers;

namespace PlayGround.Skills
{
    public interface IDamageModifiers
    {
        public interface IBaseValueModifier
        {
            void CollectAdded(AddedSink sink);
        }
    }

    public interface IAreaSizeModifiers
    {
        public interface IIncreasedModifier
        {
            void CollectIncreases(IncreasedSink sink);
        }

        public interface IMultiplierModifier
        {
            void CollectMultipliers(MultiplierSink sink);
        }
    }

    public interface IProjectileSpeedModifiers
    {
        public interface IMultiplierModifier
        {
            void CollectMultipliers(MultiplierSink sink);
        }
    }

    public interface IProjectileLifetimeModifiers
    {
        public interface IMultiplierModifier
        {
            void CollectMultipliers(MultiplierSink sink);
        }
    }

    public interface IRateModifiers
    {
        public interface IIncreasedModifier
        {
            void CollectIncreases(IncreasedSink sink);
        }
    }

    public interface IPierceCountModifiers
    {
        public interface IBaseValueModifier
        {
            void CollectAdded(AddedSink sink);
        }
    }

    public interface IManaModifiers
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
