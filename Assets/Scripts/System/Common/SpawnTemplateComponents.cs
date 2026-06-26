using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Common
{
    // ECS Lifecycle: singleton registry component; added to the shared combat scope entity on first CombatScopeOwner.Acquire and disposed on final CombatScopeOwner.Release.
    // Registry contract: Map stores projectile command-shaped templates keyed by content hash. CombatRoot writes templates only from managed pre-tick code; systems and jobs read this map as [ReadOnly] during the simulation tick.
    public struct ProjectileSpawnTemplate : IComponentData
    {
        public NativeHashMap<Hash128, ProjectileSpawnCommand> Map;
    }

    // ECS Lifecycle: singleton registry component; added to the shared combat scope entity on first CombatScopeOwner.Acquire and disposed on final CombatScopeOwner.Release.
    // Registry contract: Map stores AOE command-shaped templates keyed by content hash. CombatRoot writes templates only from managed pre-tick code; systems and jobs read this map as [ReadOnly] during the simulation tick.
    public struct AoeSpawnTemplate : IComponentData
    {
        public NativeHashMap<Hash128, AoeSpawnCommand> Map;
    }

    public static class SpawnTemplateLimits
    {
        public const int MaxSpawnChainDepth = 3;
    }

    public static class SpawnTemplateHash
    {
        public static Hash128 Of<T>(in T evt)
            where T : unmanaged
        {
            return new Hash128(xxHash3.Hash128(evt));
        }

    }
}
