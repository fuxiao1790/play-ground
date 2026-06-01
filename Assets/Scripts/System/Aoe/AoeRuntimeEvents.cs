using PlayGround.Common;
using PlayGround.System.Common;
using UnityEngine;

namespace PlayGround.System.Aoe
{
    public readonly struct AoeProjectileBurstSnapshot
    {
        public AoeProjectileBurstSnapshot(
            int projectileTypeId,
            int targetMask,
            int count,
            float spreadDegrees,
            float speed,
            float lifetimeSeconds,
            float radius,
            Vector2 halfExtents,
            float rotationRadians,
            CombatShapeType shapeType,
            DamageSnapshot damage,
            bool directDamageEnabled = true,
            int pierceCount = 0,
            float repeatHitCooldownSeconds = 0f)
        {
            Enabled = projectileTypeId >= 0;
            ProjectileTypeId = projectileTypeId;
            TargetMask = targetMask;
            Count = Mathf.Max(1, count);
            SpreadDegrees = Mathf.Max(0f, spreadDegrees);
            Speed = Mathf.Max(0f, speed);
            LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            Radius = Mathf.Max(0f, radius);
            HalfExtents = halfExtents;
            RotationRadians = rotationRadians;
            ShapeType = shapeType;
            Damage = damage;
            DirectDamageEnabled = directDamageEnabled;
            PierceCount = Mathf.Max(0, pierceCount);
            RepeatHitCooldownSeconds = Mathf.Max(0f, repeatHitCooldownSeconds);
        }

        public bool Enabled { get; }
        public int ProjectileTypeId { get; }
        public int TargetMask { get; }
        public int Count { get; }
        public float SpreadDegrees { get; }
        public float Speed { get; }
        public float LifetimeSeconds { get; }
        public float Radius { get; }
        public Vector2 HalfExtents { get; }
        public float RotationRadians { get; }
        public CombatShapeType ShapeType { get; }
        public DamageSnapshot Damage { get; }
        public bool DirectDamageEnabled { get; }
        public int PierceCount { get; }
        public float RepeatHitCooldownSeconds { get; }
    }

    public readonly struct AoeSpawnCommand
    {
        public AoeSpawnCommand(
            int typeId,
            Vector2 position,
            int targetMask,
            DamageSnapshot damage,
            float lifetimeSeconds,
            float tickIntervalSeconds,
            AoeProjectileBurstSnapshot projectileBurst = default)
        {
            TypeId = typeId;
            Position = position;
            TargetMask = targetMask;
            Damage = damage;
            LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            TickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds);
            ProjectileBurst = projectileBurst;
        }

        public int TypeId { get; }
        public Vector2 Position { get; }
        public int TargetMask { get; }
        public DamageSnapshot Damage { get; }
        public float LifetimeSeconds { get; }
        public float TickIntervalSeconds { get; }
        public AoeProjectileBurstSnapshot ProjectileBurst { get; }
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
