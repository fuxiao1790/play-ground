using PlayGround.System.Common;
using Unity.Entities;

namespace PlayGround.System.Aoe
{
    // ECS Lifecycle: base AOE tag; added at entity creation; kept until root teardown; gates AOE systems from common combat components.
    public struct AoeTag : IComponentData
    {
    }

    // ECS Lifecycle: lingering-AOE discriminator tag; added at entity creation; kept until root teardown; present only on lingering AOEs (duration-based, repeat-hit). Absence marks an impact AOE (single contact pass).
    public struct LingeringAoeTag : IComponentData
    {
    }

    // ECS Lifecycle: base AOE component; added by spawn materialization; kept until root teardown; reset on reuse.
    public struct AoeIdentityComponent : IComponentData
    {
        public CombatFaction Faction;
        public int AoeId;
        public int TypeId;
    }

    // ECS Lifecycle: base AOE component; added by spawn materialization; kept until root teardown; reset on reuse.
    // RepeatHitCooldownSeconds is the lingering tick interval; Remaining counts down to the next collision pass and is seeded to 0 so first tick fires immediately.
    public struct AoeHitGateComponent : IComponentData
    {
        public float RepeatHitCooldownSeconds;
        public float Remaining;
    }

    // ECS Lifecycle: base AOE component; added by spawn materialization; kept until root teardown; reset on reuse; carries fire-time hit-spawn template reference data.
    public struct AoeHitSpawnComponent : IComponentData
    {
        public CombatHitPayload HitPayload;
        public OnHitSpawnRef OnHitSpawn;
    }

    // ECS Lifecycle: base AOE component; added by spawn materialization; kept until root teardown; reset on reuse; carries fire-time gameplay area size for VFX dispatch.
    public struct AoeAreaComponent : IComponentData
    {
        public float Size;
    }

    // ECS Lifecycle: lingering-only AOE component; added at entity creation; kept until root teardown; reset on reuse; used by AoePulseVfxSystem for pulse VFX ticks on lingering AOEs.
    public struct AoePulseVfxComponent : IComponentData
    {
        public float RemainingInterval;
        public float Interval;
    }

}
