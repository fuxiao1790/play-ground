using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Common
{
    // ECS Lifecycle: common combat component; owned by domain entities; lifecycle is defined by each domain tag/scope.
    public struct CombatKinematicsComponent : IComponentData
    {
        public float2 Position;
        public float2 Velocity;
    }

    // ECS Lifecycle: common combat component; owned by domain entities; lifecycle is defined by each domain tag/scope.
    public struct CombatCollisionComponent : IComponentData
    {
        public CombatShapeType ShapeType;
        public float Radius;
        public float2 HalfExtents;
        public float RotationRadians;
        public float2 BoundsMin;
        public float2 BoundsMax;
    }

    // ECS Lifecycle: common combat component; owned by domain entities; lifecycle is defined by each domain tag/scope.
    public struct CombatHitComponent : IComponentData
    {
        public int TargetMask;
        public float DamageAmount;
        public bool DirectDamageEnabled;
        public EntityId SourceNodeId;
    }

    // ECS Lifecycle: common scope buffer; owned by domain scope entities; lifecycle and clearing rules are defined by each domain.
    public struct CombatTargetElement : IBufferElementData
    {
        public int TargetId;
        public int TargetMask;
        public float2 Position;
        public CombatShapeType ShapeType;
        public float Radius;
        public float2 HalfExtents;
        public float RotationRadians;
        public float2 BoundsMin;
        public float2 BoundsMax;
    }
}
