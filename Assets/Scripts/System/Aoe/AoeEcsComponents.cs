using PlayGround.System.Common;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Aoe
{
    // ECS Lifecycle: scope tag/component; added at root setup; kept until root teardown.
    public struct AoeScope : IComponentData
    {
    }

    // ECS Lifecycle: base AOE tag; added at entity creation; kept until root teardown; gates AOE systems from common combat components.
    public struct AoeTag : IComponentData
    {
    }

    // ECS Lifecycle: base AOE component; added by spawn materialization; kept until root teardown; reset on reuse.
    public struct AoeIdentityComponent : IComponentData
    {
        public Entity Scope;
        public int AoeId;
        public int TypeId;
    }

    // ECS Lifecycle: enableable AOE tag; added by spawn materialization; kept until root teardown; enabled on spawn, disabled on despawn.
    public struct AoeActiveTag : IComponentData, IEnableableComponent
    {
    }

    // ECS Lifecycle: base AOE component; added by spawn materialization; kept until root teardown; reset on reuse; lingering AOEs disable through lifetime expiry.
    public struct AoeLifetimeComponent : IComponentData
    {
        public float RemainingLifetime;
        public int IsPulse;
    }

    // ECS Lifecycle: base AOE component; added by spawn materialization; kept until root teardown; reset on reuse.
    public struct AoeHitGateComponent : IComponentData
    {
        public float RepeatHitCooldownSeconds;
    }

    // ECS Lifecycle: base AOE component; added by spawn materialization; kept until root teardown; reset on reuse; carries fire-time hit-spawn snapshot data.
    public struct AoeHitSpawnComponent : IComponentData
    {
        public AoeProjectileBurstSnapshot ProjectileBurst;
    }

    // ECS Lifecycle: AOE buffer; added by spawn materialization; kept until root teardown; cooldown entries tick down while active and are cleared on reuse.
    public struct AoeContactGateElement : IBufferElementData
    {
        public int TargetId;
        public float CooldownRemaining;
    }

    // ECS Lifecycle: scope buffer; added at root setup; kept until root teardown; drained by AoeSpawnSystem.
    public struct AoeSpawnRequestElement : IBufferElementData
    {
        public int AoeId;
        public int TypeId;
        public int TargetMask;
        // V1 keeps lifetime/tick authoring data on requests for future lingering AOE phases; pulse systems ignore both.
        public float Lifetime;
        public float RepeatHitCooldownSeconds;
        public float DamageAmount;
        public float Radius;
        public float RotationRadians;
        public float2 Position;
        public float2 HalfExtents;
        public float2 BoundsMin;
        public float2 BoundsMax;
        public CombatShapeType ShapeType;
        public CombatRenderComponent Render;
        public AoeProjectileBurstSnapshot ProjectileBurst;
    }

    // ECS Lifecycle: scope buffer; added at root setup; kept until root teardown; cleared during simulation/replay.
    public struct AoeHitElement : IBufferElementData
    {
        public int AoeId;
        public int TypeId;
        public int TargetId;
        public float2 Position;
        public float DamageAmount;
        public AoeProjectileBurstSnapshot ProjectileBurst;
    }

    // ECS Lifecycle: scope buffer; added at root setup; kept until root teardown; drained by AoeSpawnSystem into its reuse pool; carries the last prepared render matrix for one-frame pulse visual submission.
    public struct AoeRecycleElement : IBufferElementData
    {
        public Entity AoeEntity;
        public int TypeId;
        public CombatRenderElement Render;
    }

    // ECS Lifecycle: transient native payload; not added to entities; queued during collision hit flush.
    public struct AoePendingHit
    {
        public Entity Scope;
        public int AoeId;
        public int TypeId;
        public int TargetId;
        public float2 Position;
        public float DamageAmount;
        public AoeProjectileBurstSnapshot ProjectileBurst;
    }

    // ECS Lifecycle: transient native payload; not added to entities; queued during lifetime/collision recycle flush.
    public struct AoePendingRecycle
    {
        public Entity Scope;
        public Entity AoeEntity;
        public int TypeId;
        public CombatRenderElement Render;
    }
}
