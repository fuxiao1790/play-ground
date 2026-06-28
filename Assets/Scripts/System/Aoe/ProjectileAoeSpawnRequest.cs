using PlayGround.Common;
using PlayGround.System.Common;
using UnityEngine;

namespace PlayGround.System.Aoe
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
