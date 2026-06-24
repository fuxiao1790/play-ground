using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Common
{
    public enum IntervalChildKind
    {
        Projectile = 0,
        Aoe = 1
    }

    public struct IntervalProjectileChild
    {
        public int TypeId;
        public int ChildCountPerTick;
        public ProjectileChildSpawnPatternType SpawnPatternType;
        public float SideSpreadDegrees;
        public float Speed;
        public float Lifetime;
        public float Radius;
        public float2 HalfExtents;
        public float RotationRadians;
        public CombatShapeType ShapeType;
        public float DamageAmount;
        public bool DirectDamageEnabled;
        public int PierceCount;
        public float RepeatHitCooldownSeconds;
        public float VisualScale;
        public float VisualRotationSin;
        public float VisualRotationCos;
        public bool TrackingEnabled;
        public float TrackingTurnSpeedRadians;
        public float TrackingQueryIntervalSeconds;
        public float TrackingInitialQueryDelaySeconds;
        public EntityId SourceNodeId;
        public ProjectileImpactAoeSnapshot ImpactAoe;
        public StackEffectSnapshot StackEffect;
        public ProjectileImpactProjectileSnapshot ImpactProjectile;
    }

    public struct IntervalAoeChild
    {
        public int TypeId;
        public float Lifetime;
        public float RepeatHitCooldownSeconds;
        public float Radius;
        public float2 HalfExtents;
        public float RotationRadians;
        public CombatShapeType ShapeType;
        public float AreaSize;
        public CombatHitPayload HitPayload;
        public AoeProjectileBurstSnapshot ProjectileBurst;
        public AoeOnHitSpawnSnapshot AoeSpawn;
        public CombatRenderComponent Render;
        public int Count;
    }
}
