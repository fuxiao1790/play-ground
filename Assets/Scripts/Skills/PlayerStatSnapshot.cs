namespace PlayGround.Skills
{
    public readonly struct PlayerStatSnapshot
    {
        public static readonly PlayerStatSnapshot Identity = new(1f, 1f, 0f, 1.5f);

        public PlayerStatSnapshot(float castSpeedMultiplier, float damageMultiplier, float critChance, float critMultiplier)
        {
            CastSpeedMultiplier = castSpeedMultiplier;
            DamageMultiplier = damageMultiplier;
            CritChance = critChance;
            CritMultiplier = critMultiplier;
        }

        public float CastSpeedMultiplier { get; }
        public float DamageMultiplier { get; }
        public float CritChance { get; }
        public float CritMultiplier { get; }
    }

    public static class PlayerStatAggregator
    {
        // Stub: returns identity until real stat sources (CharacterStats, items, buffs) exist.
        public static PlayerStatSnapshot Aggregate(PlayerLoadout loadout) =>
            PlayerStatSnapshot.Identity;
    }
}
