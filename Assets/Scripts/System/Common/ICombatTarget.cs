using PlayGround.Common;
using PlayGround.System.Aoe;
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
            float aoeTickIntervalSeconds,
            AoeSpawnGeometry aoeGeometry = default,
            float aoeAreaSize = 1f)
        {
            Enabled = aoeTypeId >= 0;
            DebuffStatusId = debuffStatusId;
            StacksPerHit = stacksPerHit;
            StackThreshold = stackThreshold;
            AoeTypeId = aoeTypeId;
            AoeDamage = aoeDamage;
            AoeLifetimeSeconds = Mathf.Max(0f, aoeLifetimeSeconds);
            AoeTickIntervalSeconds = Mathf.Max(0f, aoeTickIntervalSeconds);
            AoeGeometry = aoeGeometry;
            AoeAreaSize = aoeGeometry.IsValid ? aoeGeometry.AreaSize : Mathf.Max(0.01f, aoeAreaSize);
        }

        public bool Enabled { get; }
        public int DebuffStatusId { get; }
        public int StacksPerHit { get; }
        public int StackThreshold { get; }
        public int AoeTypeId { get; }
        public float AoeDamage { get; }
        public float AoeLifetimeSeconds { get; }
        public float AoeTickIntervalSeconds { get; }
        public AoeSpawnGeometry AoeGeometry { get; }
        public float AoeAreaSize { get; }
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

    // Scene-facing hit context. Keep this free of internal spawn/effect payloads;
    // core follow-up spawns belong on CombatHitEffectElement via HitEffect.
    public readonly struct CombatHitContext
    {
        public CombatHitContext(
            CombatHitKind kind,
            int sourceId,
            int typeId,
            int targetId,
            Vector2 position,
            DamageSnapshot damage,
            ICombatTarget target = null,
            EntityId sourceNodeId = default)
        {
            Kind = kind;
            SourceId = sourceId;
            TypeId = typeId;
            TargetId = targetId;
            Position = position;
            Damage = damage;
            Target = target;
            SourceNodeId = sourceNodeId;
        }

        public CombatHitKind Kind { get; }
        public int SourceId { get; }
        public int TypeId { get; }
        public int TargetId { get; }
        public Vector2 Position { get; }
        public DamageSnapshot Damage { get; }
        public ICombatTarget Target { get; }
        public EntityId SourceNodeId { get; }
    }

    // External scene listeners receive only the hit context. Internal combat
    // routing may subscribe to HitEffect for spawn-on-hit payloads.
    public delegate void CombatHitHandler(in CombatHitContext context);
    public delegate void CombatHitEffectHandler(in CombatHitContext context, in CombatHitEffectElement effect);

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
