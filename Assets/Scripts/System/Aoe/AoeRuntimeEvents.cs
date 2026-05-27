using PlayGround.Common;
using UnityEngine;

namespace PlayGround.System.Aoe
{
    public readonly struct AoeSpawnCommand
    {
        public AoeSpawnCommand(
            int typeId,
            Vector2 position,
            int targetMask,
            DamageSnapshot damage,
            float lifetimeSeconds,
            float tickIntervalSeconds)
        {
            TypeId = typeId;
            Position = position;
            TargetMask = targetMask;
            Damage = damage;
            LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            TickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds);
        }

        public int TypeId { get; }
        public Vector2 Position { get; }
        public int TargetMask { get; }
        public DamageSnapshot Damage { get; }
        public float LifetimeSeconds { get; }
        public float TickIntervalSeconds { get; }
    }

    public readonly struct AoeHitContext
    {
        public AoeHitContext(int aoeId, int typeId, int targetId, Vector2 position, DamageSnapshot damage, IAoeTarget target = null)
        {
            AoeId = aoeId;
            TypeId = typeId;
            TargetId = targetId;
            Position = position;
            Damage = damage;
            Target = target;
        }

        public int AoeId { get; }
        public int TypeId { get; }
        public int TargetId { get; }
        public Vector2 Position { get; }
        public DamageSnapshot Damage { get; }
        public IAoeTarget Target { get; }
    }

    public readonly struct AoeHitEvent
    {
        public AoeHitEvent(int aoeId, int typeId, int targetId, Vector2 position, DamageSnapshot damage)
        {
            AoeId = aoeId;
            TypeId = typeId;
            TargetId = targetId;
            Position = position;
            Damage = damage;
        }

        public int AoeId { get; }
        public int TypeId { get; }
        public int TargetId { get; }
        public Vector2 Position { get; }
        public DamageSnapshot Damage { get; }
    }

    public readonly struct AoeDespawnedEvent
    {
        public AoeDespawnedEvent(int aoeId)
        {
            AoeId = aoeId;
        }

        public int AoeId { get; }
    }

    public readonly struct AoeRuntimeCounters
    {
        public AoeRuntimeCounters(
            int activeAoes,
            int spawnedAoes,
            int despawnedAoes,
            int hitEvents,
            int activeVisuals,
            float simulationMilliseconds)
        {
            ActiveAoes = activeAoes;
            SpawnedAoes = spawnedAoes;
            DespawnedAoes = despawnedAoes;
            HitEvents = hitEvents;
            ActiveVisuals = activeVisuals;
            SimulationMilliseconds = simulationMilliseconds;
        }

        public int ActiveAoes { get; }
        public int SpawnedAoes { get; }
        public int DespawnedAoes { get; }
        public int HitEvents { get; }
        public int ActiveVisuals { get; }
        public float SimulationMilliseconds { get; }
    }
}
