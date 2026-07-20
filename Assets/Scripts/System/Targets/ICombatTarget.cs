using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using System.Collections.Generic;
using PlayGround.Common;
using Unity.Entities;
using UnityEngine;

namespace PlayGround.System.Combat.Targets
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
        CombatFaction CombatFaction => CombatFaction.None;
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
        float CombatMaxHealth => 1f;
        float CombatCurrentHealth => CombatMaxHealth;
        bool IsCombatTargetActive { get; }
        void ReceiveHit(in CombatHitData hit);

        void ReceiveCombatTick(
            in CombatTickResult result,
            IReadOnlyList<StatusStackSnapshot> stacks)
        {
            if (result.DamageTaken > 0f)
            {
                var aggregateHit = new CombatHitData(
                    CombatHitKind.Projectile,
                    new DamageSnapshot(result.DamageTaken, result.CritCount > 0),
                    Vector2.zero);
                ReceiveHit(in aggregateHit);
            }

            if (stacks.Count > 0)
            {
                ReceiveStatus(stacks);
            }
        }

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
