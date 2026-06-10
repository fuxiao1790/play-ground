using PlayGround.Common;
using PlayGround.System.Common;
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
            float tickIntervalSeconds,
            AoeProjectileBurstSnapshot projectileBurst = default,
            float critChance = 0f,
            float critMultiplier = 1.5f)
        {
            TypeId = typeId;
            Position = position;
            TargetMask = targetMask;
            Damage = damage;
            LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            TickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds);
            ProjectileBurst = projectileBurst;
            CritChance = critChance;
            CritMultiplier = critMultiplier;
        }

        public int TypeId { get; }
        public Vector2 Position { get; }
        public int TargetMask { get; }
        public DamageSnapshot Damage { get; }
        public float LifetimeSeconds { get; }
        public float TickIntervalSeconds { get; }
        public AoeProjectileBurstSnapshot ProjectileBurst { get; }
        public float CritChance { get; }
        public float CritMultiplier { get; }
    }

    public readonly struct AoeHitContext
    {
        public AoeHitContext(
            int aoeId,
            int typeId,
            int targetId,
            Vector2 position,
            DamageSnapshot damage,
            AoeProjectileBurstSnapshot projectileBurst = default,
            IAoeTarget target = null)
        {
            AoeId = aoeId;
            TypeId = typeId;
            TargetId = targetId;
            Position = position;
            Damage = damage;
            ProjectileBurst = projectileBurst;
            Target = target;
        }

        public int AoeId { get; }
        public int TypeId { get; }
        public int TargetId { get; }
        public Vector2 Position { get; }
        public DamageSnapshot Damage { get; }
        public AoeProjectileBurstSnapshot ProjectileBurst { get; }
        public IAoeTarget Target { get; }
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
