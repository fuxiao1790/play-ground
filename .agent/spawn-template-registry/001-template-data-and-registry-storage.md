# 001 — Events-as-templates registry storage

## Scope

Store spawn events themselves in per-domain singleton registries on the shared scope entity,
with content-hash keys. No separate template-data struct.

## Changes

1. **Singleton registry components** (`Assets/Scripts/System/Common/SpawnTemplateComponents.cs`):
   ```
   struct ProjectileSpawnTemplate : IComponentData { public NativeHashMap<Hash128, ProjectileSpawnEvent> Map; }
   struct AoeSpawnTemplate        : IComponentData { public NativeHashMap<Hash128, AoeSpawnEvent>        Map; }
   ```
   The value type is the existing spawn event — there is no `ProjectileSpawnTemplateData` /
   `AoeSpawnTemplateData`.

2. **Content hashing helper**: `Hash128 SpawnTemplateHash.Of(in T evt)` over the blittable event
   bytes (`xxHash3.Hash128` on `UnsafeUtility.AddressOf` + `sizeof`). 128-bit key → no `MemCmp`
   confirmation needed. Callers must pass the event with **per-instance fields default**
   (`Position`, `Faction`, `BaseProjectileId`/`AoeId`, `JitterSeed`, `DeterministicIdTickIndex`)
   so the hash reflects behavior only.

3. **Scope-entity ownership** in `Assets/Scripts/System/Common/CombatEcsComponents.cs`
   (`CombatScopeOwner`):
   - In `Acquire` (first creation of the scope entity), `AddComponentData` both registry
     components with `new NativeHashMap<Hash128,…>(capacity, Allocator.Persistent)`.
   - In `Release` (before `DestroyEntity`), read both components and `Dispose()` the maps.

## Acceptance criteria

- Compiles; both registry components exist on the shared scope entity after a `CombatRoot`
  binds; maps disposed exactly once on last release (no leak).
- `SpawnTemplateHash.Of` returns equal keys for byte-identical events (per-instance fields
  default) and distinct keys otherwise.
- `ProjectileSpawnEvent`/`AoeSpawnEvent` remain blittable (EditMode `IsBlittable` guard).

## Dependencies

None (foundation).
