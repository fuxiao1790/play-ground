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

    // ECS Lifecycle: base projectile component; added by spawn materialization; kept until root teardown; reset on reuse.
    public struct ProjectileHitComponent : IComponentData
    {
        public int PierceRemaining;
        public float RepeatHitCooldownSeconds;
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

    // ECS Lifecycle: continuous projectile archetype discriminator; added at entity creation by
    // ProjectileContinuousSpawnApplySystem; never added or removed at runtime; present only on the
    // continuous archetype and absent from the discrete one. Mutually exclusive with
    // ProjectileTrackingComponent, which the continuous archetype does not carry at all.
    public struct ProjectileContinuousTag : IComponentData
    {
    }

    // ECS Lifecycle: continuous-archetype-only component; added at entity creation; seeded to the
    // spawn position on reuse and overwritten every frame by ProjectileContinuousOriginSystem
    // before ProjectileMovementSystem integrates; kept until root teardown.
    public struct ProjectileContinuousStepComponent : IComponentData
    {
        // World position the projectile occupied at the start of the current frame's step.
        // Continuous collision sweeps from here to CombatKinematicsComponent.Position.
        // Because continuous projectiles never track, this segment is the exact path travelled,
        // not an approximation of a curve.
        public float2 Origin;
    }

    // ECS Lifecycle: projectile buffer; added by spawn materialization; kept until root teardown; cleared on reuse.
    [InternalBufferCapacity(CollisionConstants.MaxProjectileGateCapacity)] // = 16
    public struct ProjectileContactGateElement : IBufferElementData
    {
        public int TargetId;
        public float CooldownRemaining;
    }

}
