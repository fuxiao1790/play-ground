using PlayGround.Common;
using UnityEngine;

namespace PlayGround.System.Common
{
    public enum CombatHitKind
    {
        Projectile = 0,
        Aoe = 1
    }

    public readonly struct CombatStackEffectSnapshot
    {
        public CombatStackEffectSnapshot(
            int debuffStatusId,
            int stacksPerHit,
            int stackThreshold,
            int aoeTypeId,
            float aoeDamage,
            float aoeLifetimeSeconds,
            float aoeTickIntervalSeconds)
        {
            Enabled = aoeTypeId >= 0;
            DebuffStatusId = debuffStatusId;
            StacksPerHit = stacksPerHit;
            StackThreshold = stackThreshold;
            AoeTypeId = aoeTypeId;
            AoeDamage = aoeDamage;
            AoeLifetimeSeconds = Mathf.Max(0f, aoeLifetimeSeconds);
            AoeTickIntervalSeconds = Mathf.Max(0f, aoeTickIntervalSeconds);
        }

        public bool Enabled { get; }
        public int DebuffStatusId { get; }
        public int StacksPerHit { get; }
        public int StackThreshold { get; }
        public int AoeTypeId { get; }
        public float AoeDamage { get; }
        public float AoeLifetimeSeconds { get; }
        public float AoeTickIntervalSeconds { get; }
    }

    public readonly struct CombatHitData
    {
        public CombatHitData(
            CombatHitKind kind,
            DamageSnapshot damage,
            Vector2 position,
            bool directDamageEnabled = true,
            CombatStackEffectSnapshot stackEffect = default)
        {
            Kind = kind;
            Damage = damage;
            Position = position;
            DirectDamageEnabled = directDamageEnabled;
            StackEffect = stackEffect;
        }

        public CombatHitKind Kind { get; }
        public DamageSnapshot Damage { get; }
        public Vector2 Position { get; }
        public bool DirectDamageEnabled { get; }
        public CombatStackEffectSnapshot StackEffect { get; }
    }

    public interface ICombatTarget
    {
        int TargetId { get; }
        Vector2 CombatTargetPosition { get; }
        float CombatTargetRadius { get; }
        Vector2 CombatTargetHalfExtents { get; }
        float CombatTargetRotationRadians { get; }
        CombatShapeType CombatTargetShapeType { get; }
        int CombatTargetMask { get; }
        bool IsCombatTargetActive { get; }
        void ReceiveHit(in CombatHitData hit);
    }
}
