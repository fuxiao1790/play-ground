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
    public readonly struct ProjectileAoeSpawnRequest
    {
        public ProjectileAoeSpawnRequest(
            int effectTypeId,
            Vector2 position,
            DamageSnapshot damage,
            float lifetimeSeconds,
            float tickIntervalSeconds,
            AoeSpawnGeometry geometry = default,
            float areaSize = 0f)
        {
            EffectTypeId = effectTypeId;
            Position = position;
            Damage = damage;
            LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            TickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds);
            Geometry = geometry;
            AreaSize = geometry.IsValid ? geometry.AreaSize : Mathf.Max(0f, areaSize);
        }

        public int EffectTypeId { get; }
        public Vector2 Position { get; }
        public DamageSnapshot Damage { get; }
        public float LifetimeSeconds { get; }
        public float TickIntervalSeconds { get; }
        public AoeSpawnGeometry Geometry { get; }
        public float AreaSize { get; }
    }
}
