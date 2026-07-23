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
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace PlayGround.System.Combat.Spawning
{
    // ECS Lifecycle: transient managed-to-ECS spawn intent appended to the shared combat scope
    // submission buffer before simulation and drained by SpawnIntakeSystem.
    public struct CombatSpawnRequest : IBufferElementData
    {
        public IntervalChildKind Kind;
        public Hash128 TemplateKey;
        public float2 Position;
        public float2 AimDirection;
        public CombatFaction Faction;
        public int SourceId;
        public uint JitterSeed;
        public int ContactGateSeedTargetId;
        public Entity Caster;
    }

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
