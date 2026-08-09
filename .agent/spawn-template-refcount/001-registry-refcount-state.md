# 001 — Registry refcount state singleton

**Scope:** small. **Depends on:** nothing.

## Change

Add to `Assets/Scripts/System/Spawning/SpawnTemplateComponents.cs`:

```csharp
// Lifetime metadata for one registry entry. Kept out of the template maps so the
// command maps stay command-shaped and Burst-readable exactly as before.
public struct SpawnTemplateRefCount
{
    // Managed claims. RegisterSpawnTemplate +1, UnregisterSpawnTemplate -1.
    public int OwnerCount;
    // ECS entities whose components currently carry this key (live or pooled-dead).
    public int InstanceCount;
    // Registered by an ad-hoc CombatRoot.Spawn path that has no owner to release it.
    // Never erased.
    public bool Pinned;

    public bool Reclaimable => !Pinned && OwnerCount <= 0 && InstanceCount <= 0;
}

// One delta queued from a spawn-apply or pool-cleanup job. Applied single-threaded
// by SpawnTemplateRefCountSystem; never applied from inside a job.
public struct SpawnTemplateRefDelta
{
    public IntervalChildKind Kind;
    public Hash128 Key;
    public int Delta;
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
}
```

Add a kind→registry helper next to it so the three call sites do not each re-derive it:

```csharp
public static class SpawnTemplateRegistryKind
{
    // ImpactAoe and LingeringAoe share the AOE registry.
    public static bool IsAoe(IntervalChildKind kind) =>
        kind == IntervalChildKind.ImpactAoe || kind == IntervalChildKind.LingeringAoe;
}
```

In `CombatScopeOwner`:

- add three static `NativeHashMap<Hash128, SpawnTemplateRefCount>` fields and one
  static `NativeQueue<SpawnTemplateRefDelta>`, mirroring the existing
  `ownedProjectileMap` / `ownedAoeMap` / `ownedTargetedMap` fields
- allocate them alongside the template maps in `Acquire`, `Allocator.Persistent`,
  same `InitialTemplateRegistryCapacity`
- `entityManager.AddComponentData(ownedScope, new SpawnTemplateRegistryState { ... })`
- dispose all four in `DisposeMaps` and null them out, same as the existing maps

## Acceptance criteria

- The three template components are unchanged, byte for byte.
- `SpawnTemplateRegistryState` exists on the scope entity whenever the template
  registries do, and is disposed on the same path (`Release`,
  `ReleaseAfterWorldDispose`, and the world-changed reset at `CombatScopeOwner.cs:39`).
- No leak warnings from the Unity collections leak detector on domain reload or
  play-mode exit.
- Nothing reads the new singleton yet.
