using Unity.Entities;
using Unity.Mathematics;

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
        public int ChildSpawnerId;
        public float ChildSpawnIntervalSeconds;
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

    public struct ProjectileTargetElement : IBufferElementData
    {
        public int TargetId;
        public int TargetMask;
        public float2 Position;
        public float Radius;
        public float2 HalfExtents;
        public float RotationRadians;
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

    public struct ProjectileChildSpawnRequestElement : IBufferElementData
    {
        public int ProjectileId;
        public int ProjectileTypeId;
        public int ChildSpawnerId;
        public int TickIndex;
        public float2 Position;
        public float2 Velocity;
        public float DamageAmount;
        public bool DirectDamageEnabled;
        public uint Order;
    }

    public struct ProjectileRenderElement : IBufferElementData
    {
        public int TypeId;
        public float4x4 Matrix;
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

    public struct ProjectilePendingChildSpawn
    {
        public Entity Scope;
        public int ProjectileId;
        public int ProjectileTypeId;
        public int ChildSpawnerId;
        public int TickIndex;
        public float2 Position;
        public float2 Velocity;
        public float DamageAmount;
        public uint Order;
    }

    public struct ProjectilePendingRender
    {
        public Entity Scope;
        public float4x4 Matrix;
    }
}
