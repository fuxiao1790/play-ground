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

namespace PlayGround.System.Combat.Core
{
    // ECS Lifecycle: unified scope tag; added once on the single shared combat
    // scope entity (ref-counted across both CombatRoot instances), kept until
    // the last CombatRoot releases it. Serves both the projectile and AOE
    // domains for both factions; CombatFaction on each element disambiguates.
    public struct CombatScope : IComponentData
    {
    }

    // Identifies which CombatRoot produced/owns a piece of combat data on the
    // single shared scope. Replaces the old per-faction Scope entity reference.
    // None is the sentinel for an unbound/never-spawned identity (mirrors the
    // role Entity.Null played before).
    public enum CombatFaction : byte
    {
        None = 0,
        Player = 1,
        Mob = 2
    }
}
