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
            HitEnergyPayload hitEnergy = default,
            EntityId sourceNodeId = default)
        {
            Kind = kind;
            Damage = damage;
            Position = position;
            DirectDamageEnabled = directDamageEnabled;
            HitEnergy = hitEnergy;
            SourceNodeId = sourceNodeId;
        }

        public CombatHitKind Kind { get; }
        public DamageSnapshot Damage { get; }
        public Vector2 Position { get; }
        public bool DirectDamageEnabled { get; }
        public HitEnergyPayload HitEnergy { get; }
        public EntityId SourceNodeId { get; }
    }

    public readonly struct HitEnergyProgress
    {
        public HitEnergyProgress(
            int accumulatorId,
            float storedEnergy,
            float energyRequired,
            float retentionRemaining)
        {
            AccumulatorId = accumulatorId;
            StoredEnergy = storedEnergy;
            EnergyRequired = energyRequired;
            RetentionRemaining = retentionRemaining;
        }

        public int AccumulatorId { get; }
        public float StoredEnergy { get; }
        public float EnergyRequired { get; }
        public float RetentionRemaining { get; }
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
        float CombatHealthRegenPerSecond => 0f;
        float CombatMaxMana => 0f;
        float CombatCurrentMana => CombatMaxMana;
        float CombatManaRegenPerSecond => 0f;
        bool IsCombatTargetActive { get; }
        void ReceiveHit(in CombatHitData hit);

        void ReceiveCombatTick(
            in CombatTickResult result,
            IReadOnlyList<HitEnergyProgress> hitEnergyProgress)
        {
            if (result.DamageTaken > 0f)
            {
                var aggregateHit = new CombatHitData(
                    CombatHitKind.Projectile,
                    new DamageSnapshot(result.DamageTaken, result.CritCount > 0),
                    Vector2.zero);
                ReceiveHit(in aggregateHit);
            }

            if (hitEnergyProgress.Count > 0)
            {
                ReceiveHitEnergyProgress(hitEnergyProgress);
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

        void ReceiveHitEnergyProgress(IReadOnlyList<HitEnergyProgress> progress)
        {
        }

        void ReceiveSpawnRejected(int castToken)
        {
        }

        void ReceiveCombat(
            IReadOnlyList<CombatHitData> hits,
            IReadOnlyList<HitEnergyProgress> hitEnergyProgress)
        {
            ReceiveHits(hits);
            if (hitEnergyProgress.Count > 0)
            {
                ReceiveHitEnergyProgress(hitEnergyProgress);
            }
        }
    }
}
