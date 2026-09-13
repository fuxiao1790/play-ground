using PlayGround.System.Combat.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Spawning
{
    // ECS Lifecycle: enableable timed-spawn data; present on projectiles and lingering AOEs; absent from impact AOEs.
    // Enabled only while timed children should emit; lifetime remains plain timer data on the same source entity.
    public struct TimedSpawnComponent : IComponentData, IEnableableComponent
    {
        public CombatFaction Faction;
        public int SourceId;
        public IntervalChildKind ChildKind;
        public Hash128 TemplateKey;
        public float EnergyPerSecond;
        public float InitialEnergyPercent;
        public float EnergyThreshold;
        public int JitterSeed;
    }

    public static class TimedSpawnInitialEnergy
    {
        public static float Roll(in TimedSpawnComponent spawn)
        {
            float maxEnergy = math.max(0f, spawn.EnergyThreshold)
                * math.saturate(spawn.InitialEnergyPercent * 0.01f);
            if (maxEnergy <= 0f)
                return 0f;

            // Expansion stamps SourceId per materialized spawner. Do not use the
            // template's JitterSeed here: every spawner needs its own offset.
            uint seed = unchecked((uint)spawn.SourceId);
            seed = (seed * 397u) ^ 0xA511E9B3u;
            var random = new Random(seed != 0 ? seed : 1u);
            return random.NextFloat(-maxEnergy, maxEnergy);
        }
    }

    // ECS Lifecycle: timed-spawn state; present on projectiles and lingering AOEs; absent from impact AOEs; reset when timed spawn is enabled.
    public struct TimedSpawnStateComponent : IComponentData
    {
        public float EnergyAccumulated;
        public int TickIndex;
    }
}
