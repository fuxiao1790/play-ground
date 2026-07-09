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

namespace PlayGround.System.Combat.Lifetime
{
    // ECS Lifecycle: shared transient combat spatial component; owned by reusable projectile/AOE archetypes and reset on spawn.
    public struct CombatKinematicsComponent : IComponentData
    {
        public float2 Position;
        public float2 Velocity;
    }

    // ECS Lifecycle: enableable common occupancy flag; added to reusable combat entities at creation; enabled on spawn, disabled on despawn.
    public struct Active : IComponentData, IEnableableComponent
    {
    }

    // ECS Lifecycle: enableable common arming pause flag; added to reusable combat entities at creation; enabled on spawn while ArmSeconds counts down, disabled when armed or despawned.
    public struct ArmingTag : IComponentData, IEnableableComponent
    {
    }

    // ECS Lifecycle: common arming timer; present on reusable combat entities; reset on spawn and counted down only while ArmingTag is enabled.
    public struct CombatArmingComponent : IComponentData
    {
        public float Remaining;
    }

    // ECS Lifecycle: common lifetime timer; present on projectiles and lingering AOEs; absent from impact AOEs. CombatLifetimeSystem counts Remaining down and disables Active on expiry.
    public struct CombatLifetimeComponent : IComponentData
    {
        public float Remaining;
    }
}
