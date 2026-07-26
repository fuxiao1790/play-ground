using PlayGround.System.Combat.Core;
using Unity.Entities;

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
        public float EnergyThreshold;
        public int JitterSeed;
    }

    // ECS Lifecycle: timed-spawn state; present on projectiles and lingering AOEs; absent from impact AOEs; reset when timed spawn is enabled.
    public struct TimedSpawnStateComponent : IComponentData
    {
        public float EnergyAccumulated;
        public int TickIndex;
    }
}
