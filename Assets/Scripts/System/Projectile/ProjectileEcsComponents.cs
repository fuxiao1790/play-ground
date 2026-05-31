using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Projectile
{
    public struct ProjectileIdentityComponent : IComponentData
    {
        public Entity Scope;
        public int ProjectileId;
        public int TypeId;
    }

    public struct ProjectileKinematicsComponent : IComponentData
    {
        public float2 Position;
        public float2 Velocity;
    }

    public struct ProjectileCollisionComponent : IComponentData
    {
        public float Radius;
        public float2 HalfExtents;
        public float RotationRadians;
        public float2 BoundsMin;
        public float2 BoundsMax;
        public ProjectileShapeType ShapeType;
    }

    public struct ProjectileLifetimeComponent : IComponentData
    {
        public float RemainingLifetime;
    }

    public struct ProjectileHitComponent : IComponentData
    {
        public int TargetMask;
        public ProjectileHitPayload HitPayload;
        public int PierceRemaining;
        public float RepeatHitCooldownSeconds;
    }

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

    public struct ProjectileChildSpawnStateComponent : IComponentData
    {
        public float ChildSpawnCooldownRemaining;
        public int ChildSpawnTickIndex;
    }

    public struct ProjectileRenderComponent : IComponentData
    {
        public int IsRenderable;
        public float VisualScale;
        public float VisualRotationSin;
        public float VisualRotationCos;
    }

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

    public struct ProjectileRenderScope : ISharedComponentData, global::System.IEquatable<ProjectileRenderScope>
    {
        public Entity Scope;
        public readonly bool Equals(ProjectileRenderScope other) => Scope == other.Scope;
        public override int GetHashCode() => Scope.GetHashCode();
    }

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

    public struct ProjectileHitElement : IBufferElementData
    {
        public int ProjectileId;
        public int ProjectileTypeId;
        public int TargetId;
        public float2 Position;
        public ProjectileHitPayload HitPayload;
        public uint Order;
    }

    public struct ProjectileRenderElement : IComponentData
    {
        public Matrix4x4 objectToWorld;
    }

    public struct ProjectileContactGateElement : IBufferElementData
    {
        public int TargetId;
        public float CooldownRemaining;
    }

    public struct ProjectileScope : IComponentData
    {
    }

    public struct ProjectileActiveTag : IComponentData, IEnableableComponent
    {
    }

    public struct ProjectileChildSpawnerTag : IComponentData
    {
    }

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
