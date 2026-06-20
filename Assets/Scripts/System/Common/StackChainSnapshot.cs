using PlayGround.System.Aoe;
using Unity.Collections;

namespace PlayGround.System.Common
{
    // Chain is resolved to plain data at root spawn; in-flight entities never read authoring or a registry.
    public struct StackStage
    {
        public StackStage(
            int debuffStatusId,
            int stacksPerHit,
            int stackThreshold,
            int aoeTypeId,
            float aoeDamage,
            float aoeLifetimeSeconds,
            float aoeTickIntervalSeconds,
            AoeSpawnGeometry aoeGeometry)
        {
            DebuffStatusId = debuffStatusId;
            StacksPerHit = stacksPerHit;
            StackThreshold = stackThreshold;
            AoeTypeId = aoeTypeId;
            AoeDamage = aoeDamage;
            AoeLifetimeSeconds = aoeLifetimeSeconds;
            AoeTickIntervalSeconds = aoeTickIntervalSeconds;
            AoeGeometry = aoeGeometry;
        }

        public int DebuffStatusId;
        public int StacksPerHit;
        public int StackThreshold;
        public int AoeTypeId;
        public float AoeDamage;
        public float AoeLifetimeSeconds;
        public float AoeTickIntervalSeconds;
        public AoeSpawnGeometry AoeGeometry;
    }

    public struct StackChainSnapshot
    {
        public CombatFaction Faction;
        public int TargetMask;
        public FixedList512Bytes<StackStage> Stages;

        public readonly bool Enabled => Stages.Length > 0;
        public readonly int Length => Stages.Length;

        public readonly StackChainSnapshot Tail()
        {
            var tail = new StackChainSnapshot
            {
                Faction = Faction,
                TargetMask = TargetMask,
                Stages = default
            };

            for (int i = 1; i < Stages.Length; i++)
                tail.Stages.Add(Stages[i]);

            return tail;
        }
    }
}
