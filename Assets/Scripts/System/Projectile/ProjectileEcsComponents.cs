using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Projectile
{
    public struct ProjectileComponent : IComponentData
    {
        public Entity Scope;
        public int ProjectileId;
        public int TypeId;
        public int TargetMask;
        public float2 Position;
        public float2 Velocity;
        public float Radius;
        public float2 HalfExtents;
        public float RotationRadians;
        public float2 BoundsMin;
        public float2 BoundsMax;
        public float RemainingLifetime;
        public float DamageAmount;
        public bool DirectDamageEnabled;
        public ProjectileShapeType ShapeType;
        public int PierceRemaining;
        public float RepeatHitCooldownSeconds;
        public bool TrackingEnabled;
        public float TrackingRangeSquared;
        public float TrackingTurnSpeedRadians;
        public float TrackingQueryCooldownRemaining;
        public float TrackingQueryIntervalSeconds;
        public int TrackedTargetId;
        public int TrackedTargetIndex;
        public BlobAssetReference<ProjectileChildSpawnerBlob> ChildSpawnerConfig;
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

    public struct ProjectileRenderBatch : IComponentData
    {
        public Entity Scope;
        public int TypeId;
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

    public struct ProjectileRenderType0BatchTag : IComponentData {}
    public struct ProjectileRenderType1BatchTag : IComponentData {}
    public struct ProjectileRenderType2BatchTag : IComponentData {}
    public struct ProjectileRenderType3BatchTag : IComponentData {}
    public struct ProjectileRenderType4BatchTag : IComponentData {}
    public struct ProjectileRenderType5BatchTag : IComponentData {}
    public struct ProjectileRenderType6BatchTag : IComponentData {}
    public struct ProjectileRenderType7BatchTag : IComponentData {}
    public struct ProjectileRenderType8BatchTag : IComponentData {}
    public struct ProjectileRenderType9BatchTag : IComponentData {}
    public struct ProjectileRenderType10BatchTag : IComponentData {}
    public struct ProjectileRenderType11BatchTag : IComponentData {}
    public struct ProjectileRenderType12BatchTag : IComponentData {}
    public struct ProjectileRenderType13BatchTag : IComponentData {}
    public struct ProjectileRenderType14BatchTag : IComponentData {}
    public struct ProjectileRenderType15BatchTag : IComponentData {}

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
        public float DamageAmount;
        public bool DirectDamageEnabled;
        public uint Order;
    }

    public struct ProjectileRenderElement : IBufferElementData
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

    // Added to each child entity by ProjectileChildSpawnSystem via ECB.
    // Read and removed by ProjectileRoot.DrainChildSpawnRequests in LateUpdate,
    // which assigns the child's ProjectileId and fires the ChildSpawnRequested event.
    public struct ProjectileChildSpawnedComponent : IComponentData
    {
        public Entity Scope;
        public int ParentProjectileId;
        public int ParentProjectileTypeId;
        public int SpawnerId;
        public int TickIndex;
    }

    public struct ProjectileChildSpawnerBlob
    {
        public int SpawnerId;
        public int TypeId;
        public int ChildCountPerTick;
        public ProjectileChildSpawnPatternType SpawnPatternType;
        public float SideSpreadDegrees;
        public float IntervalSeconds;
        public float IntervalJitterSeconds;
        public float Radius;
        public float2 HalfExtents;
        public float RotationRadians;
        public ProjectileShapeType ShapeType;
        public int TargetMask;
        public float VisualScale;
        public float VisualRotationSin;
        public float VisualRotationCos;
    }

    // Stat-derived child spawner values live here so they can be updated when player stats change.
    // The blob only stores static authored/prefab data.
    public struct ProjectileChildSpawnerStatsComponent : IComponentData
    {
        public float Speed;
        public float Lifetime;
        public float DamageAmount;
        public bool DirectDamageEnabled;
        public int PierceCount;
        public float RepeatHitCooldownSeconds;
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
        public float DamageAmount;
        public bool DirectDamageEnabled;
        public uint Order;
    }

}
