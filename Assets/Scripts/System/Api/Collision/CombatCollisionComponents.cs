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
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Collision
{
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

    // ECS Lifecycle: enableable common collision gate; added to reusable combat entities at creation; enabled when the entity produces collision effects; disabled for visual-only or despawned entities so collision jobs skip them.
    public struct CombatCollisionActiveTag : IComponentData, IEnableableComponent
    {
    }

    // ECS Lifecycle: common scope buffer; owned by domain scope entities; lifecycle and clearing rules are defined by each domain.
    public struct CombatTargetElement : IBufferElementData
    {
        public CombatFaction Faction;
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
