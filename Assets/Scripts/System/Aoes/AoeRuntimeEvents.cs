using PlayGround.Common;
using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using UnityEngine;

namespace PlayGround.System.Combat.Aoes
{
    public readonly struct AoeSpawnRequest
    {
        public AoeSpawnRequest(
            int typeId,
            Vector2 position,
            DamageSnapshot damage,
            float lifetimeSeconds,
            float tickIntervalSeconds,
            AoeSpawnGeometry geometry = default,
            float areaSize = 0f,
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
            Damage = damage;
            LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            TickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds);
            Geometry = geometry;
            AreaSize = geometry.IsValid ? geometry.AreaSize : Mathf.Max(0f, areaSize);
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
        public DamageSnapshot Damage { get; }
        public float LifetimeSeconds { get; }
        public float TickIntervalSeconds { get; }
        public AoeSpawnGeometry Geometry { get; }
        public float AreaSize { get; }
        public OnHitSpawnRef OnHitSpawn { get; }
        public float CritChance { get; }
        public float CritMultiplier { get; }
        public StackEffectSnapshot StackEffect { get; }
        public EntityId SourceNodeId { get; }
        public bool HasTimedSpawner { get; }
        public TimedSpawnComponent TimedSpawn { get; }
    }

}
