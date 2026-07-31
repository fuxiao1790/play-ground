namespace PlayGround.Common.Modifiers
{
    // The single base/added/increased/multiplier fold shape for any numeric
    // stat modifier in the codebase, not just skills - one formula, reused by
    // every system that combines a base value with flat additions, summed
    // percent increases, and multipliers, so no two systems drift apart with
    // their own hand-rolled math. The skill system is the current concrete
    // consumer, via StatModifierAccumulator and the per-edge trigger-link
    // resolves (mana cost factor, interval energy gain) that fall outside it.
    public static class StatFold
    {
        public static float Resolve(float baseValue, float added, float increasedPercent, float multiplier)
        {
            float effectiveBase = baseValue + added;
            return effectiveBase * (1f + increasedPercent) * multiplier;
        }
    }
}
