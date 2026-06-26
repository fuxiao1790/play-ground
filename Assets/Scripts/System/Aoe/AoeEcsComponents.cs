using PlayGround.Skills.Runtime;
using PlayGround.System.Common;
using Unity.Entities;

namespace PlayGround.System.Aoe
{
    // ECS Lifecycle: base AOE tag; added at entity creation; kept until root teardown; gates AOE systems from common combat components.
    public struct AoeTag : IComponentData
    {
    }

    // ECS Lifecycle: base AOE component; added by spawn materialization; kept until root teardown; reset on reuse.
    public struct AoeIdentityComponent : IComponentData
    {
        public CombatFaction Faction;
        public int AoeId;
        public int TypeId;
    }

    // ECS Lifecycle: enableable AOE tag; added at entity creation; kept until root teardown; enabled when the AOE produces collision effects (damage, stack, projectile burst); disabled for visual-only or despawned AOEs so collision and contact-gate jobs skip them entirely.
    public struct AoeCollisionActiveTag : IComponentData, IEnableableComponent
    {
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
        public AoeOnHitSpawnSnapshot AoeSpawn;
        public RuntimeStackingDetonation ProjectileBurstStacking;
    }

    // ECS Lifecycle: base AOE component; added by spawn materialization; kept until root teardown; reset on reuse; carries fire-time gameplay area size for VFX dispatch.
    public struct AoeAreaComponent : IComponentData
    {
        public float Size;
    }

    // ECS Lifecycle: lingering-only AOE buffer; added by lingering spawn materialization; kept until root teardown; cooldown entries tick down while AoeCollisionActiveTag is enabled and are cleared on reuse.
    [InternalBufferCapacity(CollisionConstants.MaxAoeTargetsPerTick)] // = 32
    public struct AoeContactGateElement : IBufferElementData
    {
        public int TargetId;
        public float CooldownRemaining;
    }


    // ECS Lifecycle: lingering-only AOE component; added at entity creation; kept until root teardown; reset on reuse; used by AoePulseVfxSystem for pulse VFX ticks on lingering AOEs.
    public struct AoePulseVfxComponent : IComponentData
    {
        public float RemainingInterval;
        public float Interval;
    }

}
