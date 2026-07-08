using PlayGround.Common;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Common
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
