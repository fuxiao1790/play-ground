# 002 — CombatRoot owner-count API

**Scope:** small-medium. **Depends on:** 001.

## Change

`Assets/Scripts/System/Core/CombatRoot.cs`.

### Register increments the owner count

The three `RegisterSpawnTemplate` overloads (`:195`, `:293`, `:314`) currently add
the entry only when absent and return the key. They keep that, and additionally bump
`OwnerCount`:

```csharp
public Hash128 RegisterSpawnTemplate(in ProjectileSpawnCommand template)
{
    EnsureRuntimeReady();

    ProjectileSpawnCommand normalizedTemplate = SpawnTemplateFor(in template);
    Hash128 key = SpawnTemplateHash.Of(in normalizedTemplate);
    ProjectileSpawnTemplate registry = entityManager.GetComponentData<ProjectileSpawnTemplate>(scopeEntity);
    if (!registry.Map.ContainsKey(key))
    {
        entityManager.CompleteAllTrackedJobs();
        registry.Map.Add(key, normalizedTemplate);
    }

    AddOwner(IntervalChildKind.Projectile, key, pinned: false);
    return key;
}
```

The AOE and targeted overloads pass `IntervalChildKind.ImpactAoe` and
`IntervalChildKind.Targeted`. Kind here only selects the counter map, and
`ImpactAoe`/`LingeringAoe` resolve to the same one.

### Shared private helpers

```csharp
private void AddOwner(IntervalChildKind kind, Hash128 key, bool pinned)
private ref NativeHashMap<Hash128, SpawnTemplateRefCount> CountsFor(IntervalChildKind kind)
```

`AddOwner` reads the entry (default when absent), increments `OwnerCount` — or sets
`Pinned = true` when `pinned` — and writes it back with `map[key] = entry`.

### Unregister

```csharp
// Drops one managed claim on a registry entry. The entry is not erased here: it
// survives until no ECS entity still carries the key, then SpawnTemplateRefCountSystem
// reclaims it. Safe to call with an unknown or default key.
public void UnregisterSpawnTemplate(IntervalChildKind kind, Hash128 key)
```

Behavior:

- `default(Hash128)` or unknown key → no-op, no throw. Callers hold keys across
  recompiles and rebinds and cannot cheaply prove liveness.
- known key → `OwnerCount = max(0, OwnerCount - 1)`, write back. No erase, no
  `ContainsKey` removal.
- `Pinned` entries → decrement is harmless; the pin still blocks reclaim.

`EnsureRuntimeReady()` is **not** appropriate here — unregister runs during teardown
when the root may already be shutting down. Guard on `scopeEntity != Entity.Null &&
entityManager.Exists(scopeEntity)` and return silently otherwise.

### Pinned ad-hoc registration

`Spawn(ProjectileSpawnRequest)` (`:189`) and `Spawn(AoeSpawnRequest)` (`:542`)
register a template per cast and have no owner that will ever release it. Route them
through a private `RegisterPinnedSpawnTemplate` that is identical to the public
overload except it calls `AddOwner(..., pinned: true)` instead of incrementing.
Without this, every cast would add an owner count that is never dropped.

## Acceptance criteria

- `RegisterSpawnTemplate` still returns the same content hash and still dedupes;
  `AoePlayModeTests.CombatRootRegisterSpawnTemplateDeduplicatesOnScope` passes unchanged.
- Registering the same template twice yields `OwnerCount == 2` and one map entry.
- `UnregisterSpawnTemplate` on an unknown or default key does not throw.
- `UnregisterSpawnTemplate` never removes an entry from the template map.
- Repeated `Spawn(ProjectileSpawnRequest)` with identical content leaves exactly one
  pinned entry, whatever the call count.
