using PlayGround.Common;
using PlayGround.System.Common;
using UnityEngine;

namespace PlayGround.System.Aoe
{
    public readonly struct AoeSpawnRequest
    {
        public AoeSpawnRequest(
            int typeId,
            Vector2 position,
            int targetMask,
            DamageSnapshot damage,
            float lifetimeSeconds,
            float tickIntervalSeconds,
            AoeSpawnGeometry geometry = default,
            float areaSize = 0f,
            AoeProjectileBurstSnapshot projectileBurst = default,
            AoeOnHitSpawnSnapshot aoeSpawn = default,
            float critChance = 0f,
            float critMultiplier = 1.5f,
            StackEffectSnapshot stackEffect = default,
            EntityId sourceNodeId = default,
            bool hasTimedSpawner = false,
            TimedSpawnComponent timedSpawn = default,
            OnHitSpawnRef onHitSpawn = default)
        {
            TypeId = typeId;
            Position = position;
            TargetMask = targetMask;
            Damage = damage;
            LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            TickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds);
            Geometry = geometry;
            AreaSize = geometry.IsValid ? geometry.AreaSize : Mathf.Max(0f, areaSize);
            ProjectileBurst = projectileBurst;
            AoeSpawn = aoeSpawn;
            OnHitSpawn = onHitSpawn;
            CritChance = critChance;
            CritMultiplier = critMultiplier;
            StackEffect = stackEffect;
            SourceNodeId = sourceNodeId;
            HasTimedSpawner = hasTimedSpawner;
            TimedSpawn = timedSpawn;
        }

        public int TypeId { get; }
        public Vector2 Position { get; }
        public int TargetMask { get; }
        public DamageSnapshot Damage { get; }
        public float LifetimeSeconds { get; }
        public float TickIntervalSeconds { get; }
        public AoeSpawnGeometry Geometry { get; }
        public float AreaSize { get; }
        public AoeProjectileBurstSnapshot ProjectileBurst { get; }
        public AoeOnHitSpawnSnapshot AoeSpawn { get; }
        public OnHitSpawnRef OnHitSpawn { get; }
        public float CritChance { get; }
        public float CritMultiplier { get; }
        public StackEffectSnapshot StackEffect { get; }
        public EntityId SourceNodeId { get; }
        public bool HasTimedSpawner { get; }
        public TimedSpawnComponent TimedSpawn { get; }
    }

    public readonly struct AoeRuntimeCounters
    {
        public AoeRuntimeCounters(
            int activeAoes,
            int spawnedAoes,
            int despawnedAoes,
            int hitEvents,
            int activeVisuals,
            int renderBatches)
        {
            ActiveAoes = activeAoes;
            SpawnedAoes = spawnedAoes;
            DespawnedAoes = despawnedAoes;
            HitEvents = hitEvents;
            ActiveVisuals = activeVisuals;
            RenderBatches = renderBatches;
        }

        public int ActiveAoes { get; }
        public int SpawnedAoes { get; }
        public int DespawnedAoes { get; }
        public int DespawnedOrReusedAoes => DespawnedAoes;
        public int HitEvents { get; }
        public int ActiveVisuals { get; }
        public int RenderBatches { get; }
    }
}
