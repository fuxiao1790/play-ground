using PlayGround.System.Aoe;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Entities;

namespace PlayGround.System.Common
{
    // ECS Lifecycle: singleton registry component; added to the shared combat scope entity on first CombatScopeOwner.Acquire and disposed on final CombatScopeOwner.Release.
    public struct ProjectileSpawnTemplate : IComponentData
    {
        public NativeHashMap<Hash128, ProjectileSpawnEvent> Map;
    }

    // ECS Lifecycle: singleton registry component; added to the shared combat scope entity on first CombatScopeOwner.Acquire and disposed on final CombatScopeOwner.Release.
    public struct AoeSpawnTemplate : IComponentData
    {
        public NativeHashMap<Hash128, AoeSpawnEvent> Map;
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
