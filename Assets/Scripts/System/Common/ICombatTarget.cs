using System.Collections.Generic;
using PlayGround.Common;
using PlayGround.System.Aoe;
using Unity.Entities;
using UnityEngine;

namespace PlayGround.System.Common
{
    public enum CombatHitKind
    {
        Projectile = 0,
        Aoe = 1
    }

    public readonly struct CombatStatusEffectSnapshot
    {
        public CombatStatusEffectSnapshot(
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
            CombatStatusEffectSnapshot stackEffect = default,
            EntityId sourceNodeId = default)
        {
            Kind = kind;
            Damage = damage;
            Position = position;
            DirectDamageEnabled = directDamageEnabled;
            StackEffect = stackEffect;
            SourceNodeId = sourceNodeId;
        }

        public CombatHitKind Kind { get; }
        public DamageSnapshot Damage { get; }
        public Vector2 Position { get; }
        public bool DirectDamageEnabled { get; }
        public CombatStatusEffectSnapshot StackEffect { get; }
        public EntityId SourceNodeId { get; }
    }

    public interface ICombatTarget
    {
        int TargetId { get; }
        Entity CombatTargetProxy
        {
            get => Entity.Null;
            set { }
        }
        Vector2 CombatTargetPosition { get; }
        float CombatTargetRadius { get; }
        Vector2 CombatTargetHalfExtents { get; }
        float CombatTargetRotationRadians { get; }
        CombatShapeType CombatTargetShapeType { get; }
        int CombatTargetMask { get; }
        bool IsCombatTargetActive { get; }
        void ReceiveHit(in CombatHitData hit);

        void ReceiveHits(IReadOnlyList<CombatHitData> hits)
        {
            for (int i = 0; i < hits.Count; i++)
            {
                var h = hits[i];
                ReceiveHit(in h);
            }
        }
    }
}
