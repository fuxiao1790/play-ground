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
using PlayGround.Common;
using UnityEngine;

namespace PlayGround.System.Combat.Application
{
    // Fire-time hit snapshot shared by all combat domains (projectile, AOE). Carries damage, crit, and stack data snapshotted at spawn time.
    public struct CombatHitPayload
    {
        public float DamageAmount;
        public float CritChance;
        public float CritMultiplier;
        public bool DirectDamageEnabled;
        public EntityId SourceNodeId;
        public StackEffectSnapshot StackEffect;
    }
}
