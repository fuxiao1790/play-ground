using PlayGround.Common;
using UnityEngine;

namespace PlayGround.Attack
{
    public readonly struct ProjectileAoeSpawnRequest
    {
        public ProjectileAoeSpawnRequest(
            int effectTypeId,
            Vector2 position,
            DamageSnapshot damage,
            float lifetimeSeconds,
            float tickIntervalSeconds)
        {
            EffectTypeId = effectTypeId;
            Position = position;
            Damage = damage;
            LifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            TickIntervalSeconds = Mathf.Max(0f, tickIntervalSeconds);
        }

        public int EffectTypeId { get; }
        public Vector2 Position { get; }
        public DamageSnapshot Damage { get; }
        public float LifetimeSeconds { get; }
        public float TickIntervalSeconds { get; }
    }
}
