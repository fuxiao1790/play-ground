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
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PlayGround.System.Combat.Application
{
    // ECS Lifecycle: transient native hit payload; not added to entities; enqueued during collision,
    // grouped and aggregated by CombatApplyFinalizeSingleSystem, then handed to CombatApplyBridge.
    public struct CombatHitEvent
    {
        public Entity TargetProxy;
        public CombatHitKind Kind;
        public float DamageAmount;
        public float CritChance;
        public float CritMultiplier;
        public bool DirectDamageEnabled;
        public float2 HitPosition;
        public EntityId SourceNodeId;
        public int SourceId;
        public int TypeId;
        public StackEffectSnapshot StackEffect;
    }
}
