using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Projectiles
{
    // ECS Lifecycle: base projectile component; added by spawn materialization; kept until root teardown; reset on reuse.
    public struct ProjectileIdentityComponent : IComponentData
    {
        public CombatFaction Faction;
        public int ProjectileId;
        public int TypeId;
    }

    // ECS Lifecycle: base projectile tag; added at entity creation; kept until root teardown; gates projectile systems from common combat components.
    public struct ProjectileTag : IComponentData
    {
    }

    // ECS Lifecycle: base projectile component; added by spawn materialization; kept until root teardown; reset on reuse; carries fire-time hit-spawn template reference data.
    public struct ProjectileHitComponent : IComponentData
    {
        public int PierceRemaining;
        public float RepeatHitCooldownSeconds;
        public ProjectileHitPayload HitPayload;
    }

    // ECS Lifecycle: enableable base projectile component; added by spawn materialization; kept until root teardown; reset and enabled only for homing projectiles on reuse.
    public struct ProjectileTrackingComponent : IComponentData, IEnableableComponent
    {
        public bool TrackingEnabled;
        public float TrackingTurnSpeedRadians;
        public float TrackingQueryCooldownRemaining;
        public float TrackingQueryIntervalSeconds;
        public int TrackedTargetId;
        public int TrackedTargetIndex;
        public float2 TrackedTargetPosition;
        public uint TrackingRandomState;
    }

    // ECS Lifecycle: projectile buffer; added by spawn materialization; kept until root teardown; cleared on reuse.
    [InternalBufferCapacity(CollisionConstants.MaxProjectileGateCapacity)] // = 16
    public struct ProjectileContactGateElement : IBufferElementData
    {
        public int TargetId;
        public float CooldownRemaining;
    }

}
