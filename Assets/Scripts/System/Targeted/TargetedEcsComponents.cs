using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Targeted
{
    // ECS Lifecycle: base targeted tag; added at entity creation; kept until root teardown;
    // gates targeted systems from common combat components.
    public struct TargetedTag : IComponentData { }

    // ECS Lifecycle: base targeted component; added by spawn materialization; reset on reuse.
    public struct TargetedIdentityComponent : IComponentData
    {
        public CombatFaction Faction;
        public int TargetedId;
        public int TypeId;
        public int InstanceIndex;
    }

    // ECS Lifecycle: base targeted component; added by spawn materialization; reset on reuse and on
    // interval walk restart. Authoritative walk state; CombatKinematicsComponent is a derived mirror.
    public struct TargetedChainComponent : IComponentData
    {
        public float2 Origin;
        public float2 AcquireAnchor;
        public float2 LinkSource;
        public float2 LinkTarget;
        public int LastTargetKey;
        public int LinkIndex;
        public float LinkGateRemaining;
    }

    // ECS Lifecycle: base targeted component; added by spawn materialization; reset on reuse.
    // ChainDistance is the reach of every hop, including link 0 out from the acquisition anchor.
    public struct TargetedResolveConfig : IComponentData
    {
        public float ChainDistance;
        public float ChainDamageFalloff;
        public float ChainDelay;
        public int ChainCount;
    }

    // ECS Lifecycle: base targeted component; added by spawn materialization; reset on reuse.
    // Stores only this domain's VFX ids. LinkId is the repeating in-flight effect slot.
    public struct TargetedVfxIds : IComponentData
    {
        public int SpawnId;
        public int HitId;
        public int ExpireId;
        public int LinkId;
        public int ArmingId;
    }

    // ECS Lifecycle: base targeted component; added by spawn materialization; reset on reuse.
    // Carries authored visual-only VFX sizes. A chain has no gameplay area to reuse, so these
    // values never fold through the AreaSize stat.
    public struct TargetedVfxSizeComponent : IComponentData
    {
        public float EffectSize;
        public float LinkWidth;
    }
}
