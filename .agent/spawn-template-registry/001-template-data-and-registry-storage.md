# 001 — Template data structs + registry storage

## Scope

Introduce the cold spawn-template data structs, the per-domain singleton registry components,
the content-hash helper, and the scope-entity ownership of the maps.

## Changes

1. **Template data structs** (in `Assets/Scripts/System/Common/`, e.g. new
   `SpawnTemplateData.cs`, or repurpose `IntervalChildTemplates.cs`):
   - `ProjectileSpawnTemplateData` ≈ today's `IntervalProjectileChild` (TypeId,
     count/pattern/`SideSpreadDegrees`, Speed, Lifetime, geometry, `DamageAmount`,
     `DirectDamageEnabled`, PierceCount, RepeatHitCooldown, visual, tracking, `ImpactAoe`,
     `StackEffect`, `ImpactProjectile`) **minus per-instance fields** (`SourceNodeId` and any
     position/direction — those are set at spawn). Keep fan-out and damage/crit.
   - `AoeSpawnTemplateData` ≈ today's `IntervalAoeChild` (TypeId, Count, Lifetime,
     RepeatHitCooldown, geometry, AreaSize, `HitPayload`, `ProjectileBurst`, `AoeSpawn`,
     `Render`) minus per-instance `SourceNodeId`.
   - Both must be blittable (no managed refs) so they hash and live in a `NativeHashMap`.

2. **Singleton registry components** (`Assets/Scripts/System/Common/SpawnTemplateComponents.cs`):
   ```
   struct ProjectileSpawnTemplate : IComponentData { public NativeHashMap<Hash128, ProjectileSpawnTemplateData> Map; }
   struct AoeSpawnTemplate        : IComponentData { public NativeHashMap<Hash128, AoeSpawnTemplateData>        Map; }
   ```

3. **Content hashing helper**: `Hash128 SpawnTemplateHash.Of(in T template)` using
   `xxHash3.Hash128` over the struct bytes (`UnsafeUtility.AddressOf` + `sizeof`). 128-bit key
   → collisions negligible, no `MemCmp` confirmation needed.

4. **Scope-entity ownership** in `Assets/Scripts/System/Common/CombatEcsComponents.cs`
   (`CombatScopeOwner`):
   - In `Acquire`, when the scope entity is first created, `AddComponentData` both registry
     components with freshly allocated `NativeHashMap<Hash128,…>(capacity, Allocator.Persistent)`.
   - In `Release`, before `DestroyEntity`, read the components and `Dispose()` both maps.

## Acceptance criteria

- Project compiles; both registry components exist on the shared scope entity after a
  `CombatRoot` binds; maps are disposed exactly once on last release (no leak warning).
- `SpawnTemplateHash.Of` returns identical keys for byte-identical templates and distinct keys
  for differing ones.
- Template data structs are blittable (compile-time `UnsafeUtility.IsBlittable`/Burst check).

## Dependencies

None (foundation task).
