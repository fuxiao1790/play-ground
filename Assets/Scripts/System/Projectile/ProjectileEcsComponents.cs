using PlayGround.System.Common;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Projectile
{
    // ECS Lifecycle: base projectile component; added by spawn materialization; kept until root teardown; reset on reuse.
    public struct ProjectileIdentityComponent : IComponentData
    {
        public CombatFaction Faction;
        public int ProjectileId;
        public int TypeId;
    }

    // ECS Lifecycle: base projectile tag; added at entity creation; kept until root teardown; gates projectile systems from common combat components.
    public struct ProjectileTag : IComponentData
    {
    }

    // ECS Lifecycle: base projectile component; added by spawn materialization; kept until root teardown; reset on reuse; carries fire-time hit-spawn snapshot data.
    public struct ProjectileHitComponent : IComponentData
    {
        public int PierceRemaining;
        public float RepeatHitCooldownSeconds;
        public ProjectileHitPayload HitPayload;
    }

    // ECS Lifecycle: enableable base projectile component; added by spawn materialization; kept until root teardown; reset and enabled only for homing projectiles on reuse.
    public struct ProjectileTrackingComponent : IComponentData, IEnableableComponent
    {
        public bool TrackingEnabled;
        public float TrackingTurnSpeedRadians;
        public float TrackingQueryCooldownRemaining;
        public float TrackingQueryIntervalSeconds;
        public int TrackedTargetId;
        public int TrackedTargetIndex;
        public float2 TrackedTargetPosition;
        public uint TrackingRandomState;
    }

    // ECS Lifecycle: optional child-spawner component; added to child-spawning archetypes; kept until root teardown; reset on reuse.
    public struct ProjectileChildSpawnStateComponent : IComponentData
    {
        public float ChildSpawnCooldownRemaining;
        public int ChildSpawnTickIndex;
    }

    // ECS Lifecycle: projectile buffer; added by spawn materialization; kept until root teardown; cleared on reuse.
    public struct ProjectileContactGateElement : IBufferElementData
    {
        public int TargetId;
        public float CooldownRemaining;
    }

    // ECS Lifecycle: enableable projectile tag; added by spawn materialization; kept until root teardown; enabled on spawn, disabled on despawn.
    public struct ProjectileActiveTag : IComponentData, IEnableableComponent
    {
    }

    // ECS Lifecycle: enableable projectile tag; added at entity creation; kept until root teardown; enabled when the projectile produces collision effects (damage, stack, impact AOE/projectile); disabled for visual-only projectiles so the collision job skips them entirely.
    public struct ProjectileCollisionActiveTag : IComponentData, IEnableableComponent
    {
    }

    // ECS Lifecycle: optional child-spawner tag; added to child-spawning archetypes; kept until root teardown.
    public struct ProjectileChildSpawnerTag : IComponentData
    {
    }

    // ECS Lifecycle: optional child-spawner component; added to child-spawning archetypes; kept until root teardown; reset on reuse.
    public struct ProjectileChildSpawnerComponent : IComponentData
    {
        public int SpawnerId;
        public int TypeId;
        public int ChildCountPerTick;
        public ProjectileChildSpawnPatternType SpawnPatternType;
        public float SideSpreadDegrees;
        public float IntervalSeconds;
        public float IntervalJitterSeconds;
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
        public CombatStatusEffectSnapshot StackEffect;
        public ProjectileImpactProjectileSnapshot ImpactProjectile;
    }

}
