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
using PlayGround.System.Combat.Targeted;
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

    // ECS Lifecycle: singleton registry component; added to the shared combat scope entity on first CombatScopeOwner.Acquire and disposed on final CombatScopeOwner.Release.
    // Registry contract: Map stores targeted command-shaped templates keyed by content hash. CombatRoot writes templates only from managed pre-tick code; systems and jobs read this map as [ReadOnly] during the simulation tick.
    public struct TargetedSpawnTemplate : IComponentData
    {
        public NativeHashMap<Hash128, TargetedSpawnCommand> Map;
    }

    // Lifetime metadata for one registry entry. Kept out of the template maps so the
    // command maps stay command-shaped and Burst-readable exactly as before.
    public struct SpawnTemplateRefCount
    {
        // Managed claims. RegisterSpawnTemplate +1, UnregisterSpawnTemplate -1.
        public int OwnerCount;
        // Live ECS entities carrying this key. A spawn emits +1, a despawn emits -1.
        public int InstanceCount;
        // Registered by an ad-hoc CombatRoot.Spawn path that has no owner to release it.
        // Never erased.
        public bool Pinned;

        public bool Reclaimable => !Pinned && OwnerCount <= 0 && InstanceCount <= 0;
    }

    // One delta queued when an entity is spawned or despawned. Applied single-threaded by
    // SpawnTemplateRefCountSystem; spawn and despawn code never touches a count itself.
    public struct SpawnTemplateRefDelta
    {
        public IntervalChildKind Kind;
        public Hash128 Key;
        public int Delta;
    }

    // The single definition of which template keys an entity of each domain carries.
    // Acquire and release are the same walk with an opposite sign, so a spawn and the
    // matching despawn cannot disagree about the key set, and adding a new key-carrying
    // field means editing one method rather than hunting every spawn and death site.
    //
    // Reads the entity's own components rather than the spawn command, so branches that
    // zero a component at materialization (a non-timed source clears TimedSpawnComponent)
    // need no mirroring here.
    public static class SpawnTemplateRefEmit
    {
        public static void Enqueue(
            IntervalChildKind kind,
            Hash128 key,
            int delta,
            NativeQueue<SpawnTemplateRefDelta>.ParallelWriter deltas)
        {
            if (key.Equals(default(Hash128)))
            {
                return;
            }

            deltas.Enqueue(new SpawnTemplateRefDelta { Kind = kind, Key = key, Delta = delta });
        }

        public static void AcquireProjectile(
            in TimedSpawnComponent timed,
            in CombatHitPayload payload,
            NativeQueue<SpawnTemplateRefDelta>.ParallelWriter deltas) =>
            EmitProjectile(in timed, in payload, 1, deltas);

        public static void ReleaseProjectile(
            in TimedSpawnComponent timed,
            in CombatHitPayload payload,
            NativeQueue<SpawnTemplateRefDelta>.ParallelWriter deltas) =>
            EmitProjectile(in timed, in payload, -1, deltas);

        public static void AcquireAoe(
            in TimedSpawnComponent timed,
            in CombatHitPayload payload,
            NativeQueue<SpawnTemplateRefDelta>.ParallelWriter deltas) =>
            EmitAoe(in timed, in payload, 1, deltas);

        public static void ReleaseAoe(
            in TimedSpawnComponent timed,
            in CombatHitPayload payload,
            NativeQueue<SpawnTemplateRefDelta>.ParallelWriter deltas) =>
            EmitAoe(in timed, in payload, -1, deltas);

        // Targeted entities carry no TimedSpawnComponent; the stack detonation key is
        // their only template reference.
        public static void AcquireTargeted(
            in CombatHitPayload payload,
            NativeQueue<SpawnTemplateRefDelta>.ParallelWriter deltas) =>
            Enqueue(IntervalChildKind.Projectile, payload.StackEffect.DetonationKey, 1, deltas);

        public static void ReleaseTargeted(
            in CombatHitPayload payload,
            NativeQueue<SpawnTemplateRefDelta>.ParallelWriter deltas) =>
            Enqueue(IntervalChildKind.Projectile, payload.StackEffect.DetonationKey, -1, deltas);

        private static void EmitProjectile(
            in TimedSpawnComponent timed,
            in CombatHitPayload payload,
            int delta,
            NativeQueue<SpawnTemplateRefDelta>.ParallelWriter deltas)
        {
            Enqueue(timed.ChildKind, timed.TemplateKey, delta, deltas);
            // A stack detonation is always a projectile nova.
            Enqueue(IntervalChildKind.Projectile, payload.StackEffect.DetonationKey, delta, deltas);
        }

        private static void EmitAoe(
            in TimedSpawnComponent timed,
            in CombatHitPayload payload,
            int delta,
            NativeQueue<SpawnTemplateRefDelta>.ParallelWriter deltas)
        {
            Enqueue(timed.ChildKind, timed.TemplateKey, delta, deltas);
            Enqueue(IntervalChildKind.Projectile, payload.StackEffect.DetonationKey, delta, deltas);
        }
    }

    // ECS Lifecycle: singleton refcount state; added to the shared combat scope entity on
    // first CombatScopeOwner.Acquire and disposed on final CombatScopeOwner.Release.
    // Concurrency: the three count maps are read and written only by managed pre-tick code
    // and by SpawnTemplateRefCountSystem. Simulation jobs only ever write Deltas through a
    // ParallelWriter; they never touch the count maps.
    public struct SpawnTemplateRegistryState : IComponentData
    {
        public NativeHashMap<Hash128, SpawnTemplateRefCount> ProjectileCounts;
        public NativeHashMap<Hash128, SpawnTemplateRefCount> AoeCounts;
        public NativeHashMap<Hash128, SpawnTemplateRefCount> TargetedCounts;
        public NativeQueue<SpawnTemplateRefDelta> Deltas;
        // Set by managed unregisters and delta draining; avoids map scans when no lifetime
        // state changed since the previous late-simulation sweep.
        public bool IsDirty;
    }

    public static class SpawnTemplateRegistryKind
    {
        // ImpactAoe and LingeringAoe share the AOE registry.
        public static bool IsAoe(IntervalChildKind kind) =>
            kind == IntervalChildKind.ImpactAoe || kind == IntervalChildKind.LingeringAoe;
    }

    public static class SpawnTemplateLimits
    {
        public const int MaxSpawnChainDepth = 3;
    }

    // Interval-child-kind completeness is a property of the authored template, not of any
    // one hit or tick, so it is checked once here when a template is registered rather than
    // on every collision/timer read of the stamped-out Kind value.
    public static class SpawnTemplateValidation
    {
        public static void EnsureValidChildKind(TimedSpawnComponent timedSpawn)
        {
            if (!timedSpawn.TemplateKey.Equals(default(Unity.Entities.Hash128)))
            {
                EnsureValidChildKind(timedSpawn.ChildKind);
            }
        }

        private static void EnsureValidChildKind(IntervalChildKind kind)
        {
            if (kind == IntervalChildKind.Projectile
                || kind == IntervalChildKind.ImpactAoe
                || kind == IntervalChildKind.LingeringAoe
                || kind == IntervalChildKind.Targeted)
            {
                return;
            }

            throw new global::System.InvalidOperationException($"Unhandled interval child kind {kind}.");
        }
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
