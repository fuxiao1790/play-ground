using PlayGround.System.Combat.Core;
using Unity.Entities;

namespace PlayGround.System.Combat.Status
{
    public enum HitEnergySpawnKind
    {
        None = 0,
        ImpactAoe = 1,
        LingeringAoe = 2,
        Projectile = 3
    }

    public struct HitEnergySpawn
    {
        public HitEnergySpawnKind Kind;
        public CombatFaction Faction;
        public Hash128 TemplateKey;

        public readonly bool Enabled =>
            Kind != HitEnergySpawnKind.None
            && !TemplateKey.Equals(default(Hash128));
    }

    // Fire-time hit-energy payload carried by applicators. Accumulated energy controls
    // activation timing only; output behavior is resolved from the registered template.
    public struct HitEnergyPayload
    {
        public int AccumulatorId;
        public float EnergyPerHit;
        public float EnergyRequired;
        public float RetentionSeconds;
        public HitEnergySpawn Spawn;

        public readonly bool Enabled =>
            AccumulatorId >= 0
            && IsFinitePositive(EnergyPerHit)
            && IsFinitePositive(EnergyRequired)
            && RetentionSeconds > 0f
            && Spawn.Enabled;

        private static bool IsFinitePositive(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
