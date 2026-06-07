namespace PlayGround.Skills
{
    public readonly struct PlayerStatSnapshot
    {
        public static readonly PlayerStatSnapshot Identity = new(1f, 1f);

        public PlayerStatSnapshot(float castSpeedMultiplier, float damageMultiplier)
        {
            CastSpeedMultiplier = castSpeedMultiplier;
            DamageMultiplier = damageMultiplier;
        }

        public float CastSpeedMultiplier { get; }
        public float DamageMultiplier { get; }
    }

    public static class PlayerStatAggregator
    {
        // Stub: returns identity until real stat sources (CharacterStats, items, buffs) exist.
        public static PlayerStatSnapshot Aggregate(PlayerLoadout loadout) =>
            PlayerStatSnapshot.Identity;
    }
}
