using PlayGround.System.Common;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Aoe
{
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

    // ECS Lifecycle: enableable AOE tag; added at entity creation; kept until root teardown; enabled when the AOE produces collision effects (damage, stack, projectile burst); disabled for visual-only AOEs so the collision job skips them entirely.
    public struct AoeCollisionActiveTag : IComponentData, IEnableableComponent
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
        public CombatHitPayload HitPayload;
        public AoeProjectileBurstSnapshot ProjectileBurst;
    }

    // ECS Lifecycle: base AOE component; added by spawn materialization; kept until root teardown; reset on reuse; carries fire-time gameplay area size for VFX dispatch.
    public struct AoeAreaComponent : IComponentData
    {
        public float Size;
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
        // V1 keeps lifetime/tick authoring data on requests for future lingering AOE phases; pulse systems ignore both.
        public float Lifetime;
        public float RepeatHitCooldownSeconds;
        public CombatHitPayload HitPayload;
        public float AreaSize;
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


    // ECS Lifecycle: base AOE component; added at entity creation; kept until root teardown; reset on reuse; used by AoeLifetimeSystem for pulse VFX ticks on lingering AOEs.
    public struct AoePulseVfxComponent : IComponentData
    {
        public float RemainingInterval;
        public float Interval;
    }

}
