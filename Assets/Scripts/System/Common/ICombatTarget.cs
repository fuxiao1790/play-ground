using System.Collections.Generic;
using PlayGround.Common;
using Unity.Entities;
using UnityEngine;

namespace PlayGround.System.Common
{
    public enum CombatHitKind
    {
        Projectile = 0,
        Aoe = 1
    }

    public readonly struct CombatHitData
    {
        public CombatHitData(
            CombatHitKind kind,
            DamageSnapshot damage,
            Vector2 position,
            bool directDamageEnabled = true,
            StackEffectSnapshot stackEffect = default,
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
        public StackEffectSnapshot StackEffect { get; }
        public EntityId SourceNodeId { get; }
    }

    public readonly struct StatusStackSnapshot
    {
        public StatusStackSnapshot(int debuffKey, int count, float lifetimeRemaining)
        {
            DebuffKey = debuffKey;
            Count = count;
            LifetimeRemaining = lifetimeRemaining;
        }

        public int DebuffKey { get; }
        public int Count { get; }
        public float LifetimeRemaining { get; }
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

        void ReceiveStatus(IReadOnlyList<StatusStackSnapshot> stacks)
        {
        }

        void ReceiveCombat(
            IReadOnlyList<CombatHitData> hits,
            IReadOnlyList<StatusStackSnapshot> stacks)
        {
            ReceiveHits(hits);
            if (stacks.Count > 0)
            {
                ReceiveStatus(stacks);
            }
        }
    }
}
