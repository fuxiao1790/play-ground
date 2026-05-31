using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Projectile
{
    // ECS Lifecycle: base projectile component; added by archetype creation; kept until root teardown; reset on reuse.
    public struct ProjectileIdentityComponent : IComponentData
    {
        public Entity Scope;
        public int ProjectileId;
        public int TypeId;
    }

    // ECS Lifecycle: base projectile component; added by archetype creation; kept until root teardown; reset on reuse.
    public struct ProjectileKinematicsComponent : IComponentData
    {
        public float2 Position;
        public float2 Velocity;
    }

    // ECS Lifecycle: base projectile component; added by archetype creation; kept until root teardown; reset on reuse.
    public struct ProjectileCollisionComponent : IComponentData
    {
        public float Radius;
        public float2 HalfExtents;
        public float RotationRadians;
        public float2 BoundsMin;
        public float2 BoundsMax;
        public ProjectileShapeType ShapeType;
    }

    // ECS Lifecycle: base projectile component; added by archetype creation; kept until root teardown; active tag disabled on expiry.
    public struct ProjectileLifetimeComponent : IComponentData
    {
        public float RemainingLifetime;
    }

    // ECS Lifecycle: base projectile component; added by archetype creation; kept until root teardown; reset on reuse.
    public struct ProjectileHitComponent : IComponentData
    {
        public int TargetMask;
        public ProjectileHitPayload HitPayload;
        public int PierceRemaining;
        public float RepeatHitCooldownSeconds;
    }

    // ECS Lifecycle: base projectile component; added by archetype creation; kept until root teardown; reset on reuse.
    public struct ProjectileTrackingComponent : IComponentData
    {
        public bool TrackingEnabled;
        public float TrackingRangeSquared;
        public float TrackingTurnSpeedRadians;
        public float TrackingQueryCooldownRemaining;
        public float TrackingQueryIntervalSeconds;
        public int TrackedTargetId;
        public int TrackedTargetIndex;
    }

    // ECS Lifecycle: optional child-spawner component; added to child-spawning archetypes; kept until root teardown; reset on reuse.
    public struct ProjectileChildSpawnStateComponent : IComponentData
    {
        public float ChildSpawnCooldownRemaining;
        public int ChildSpawnTickIndex;
    }

    // ECS Lifecycle: base projectile component; added by archetype creation; kept until root teardown; reset on reuse.
    public struct ProjectileRenderComponent : IComponentData
    {
        public int IsRenderable;
        public float VisualScale;
        public float VisualRotationSin;
        public float VisualRotationCos;
    }

    // ECS Lifecycle: structural render tag; added at entity creation; kept until root teardown; reuse only with same render type.
    public struct ProjectileRenderType0Tag : IComponentData {}
    public struct ProjectileRenderType1Tag : IComponentData {}
    public struct ProjectileRenderType2Tag : IComponentData {}
    public struct ProjectileRenderType3Tag : IComponentData {}
    public struct ProjectileRenderType4Tag : IComponentData {}
    public struct ProjectileRenderType5Tag : IComponentData {}
    public struct ProjectileRenderType6Tag : IComponentData {}
    public struct ProjectileRenderType7Tag : IComponentData {}
    public struct ProjectileRenderType8Tag : IComponentData {}
    public struct ProjectileRenderType9Tag : IComponentData {}
    public struct ProjectileRenderType10Tag : IComponentData {}
    public struct ProjectileRenderType11Tag : IComponentData {}
    public struct ProjectileRenderType12Tag : IComponentData {}
    public struct ProjectileRenderType13Tag : IComponentData {}
    public struct ProjectileRenderType14Tag : IComponentData {}
    public struct ProjectileRenderType15Tag : IComponentData {}

    // ECS Lifecycle: shared projectile component; added at entity creation; kept until root teardown; not changed during churn.
    public struct ProjectileRenderScope : ISharedComponentData, global::System.IEquatable<ProjectileRenderScope>
    {
        public Entity Scope;
        public readonly bool Equals(ProjectileRenderScope other) => Scope == other.Scope;
        public override int GetHashCode() => Scope.GetHashCode();
    }

    // ECS Lifecycle: scope buffer; added at root setup; kept until root teardown; cleared and rebuilt during sync.
    public struct ProjectileTargetElement : IBufferElementData
    {
        public int TargetId;
        public int TargetMask;
        public float2 Position;
        public float Radius;
        public float2 HalfExtents;
        public float RotationRadians;
        public float2 BoundsMin;
        public float2 BoundsMax;
        public ProjectileShapeType ShapeType;
    }

    // ECS Lifecycle: scope buffer; added at root setup; kept until root teardown; cleared during simulation/replay.
    public struct ProjectileHitElement : IBufferElementData
    {
        public int ProjectileId;
        public int ProjectileTypeId;
        public int TargetId;
        public float2 Position;
        public ProjectileHitPayload HitPayload;
        public uint Order;
    }

    // ECS Lifecycle: base projectile component; added by archetype creation; kept until root teardown; overwritten during render prep.
    public struct ProjectileRenderElement : IComponentData
    {
        public Matrix4x4 objectToWorld;
    }

    // ECS Lifecycle: projectile buffer; added by archetype creation; kept until root teardown; cleared on reuse.
    public struct ProjectileContactGateElement : IBufferElementData
    {
        public int TargetId;
        public float CooldownRemaining;
    }

    // ECS Lifecycle: scope tag; added at root setup; kept until root teardown.
    public struct ProjectileScope : IComponentData
    {
    }

    // ECS Lifecycle: enableable projectile tag; added by archetype creation; kept until root teardown; enabled on spawn, disabled on despawn.
    public struct ProjectileActiveTag : IComponentData, IEnableableComponent
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
        public ProjectileShapeType ShapeType;
        public float DamageAmount;
        public bool DirectDamageEnabled;
        public int PierceCount;
        public float RepeatHitCooldownSeconds;
        public int TargetMask;
        public float VisualScale;
        public float VisualRotationSin;
        public float VisualRotationCos;
        public bool TrackingEnabled;
        public float TrackingRangeSquared;
        public float TrackingTurnSpeedRadians;
        public float TrackingQueryIntervalSeconds;
        public float TrackingInitialQueryDelaySeconds;
    }

    // ECS Lifecycle: transient native payload; not added to entities; queued during collision hit flush.
    public struct ProjectilePendingHit
    {
        public Entity Scope;
        public int ProjectileId;
        public int ProjectileTypeId;
        public int TargetId;
        public float2 Position;
        public ProjectileHitPayload HitPayload;
        public uint Order;
    }

}
