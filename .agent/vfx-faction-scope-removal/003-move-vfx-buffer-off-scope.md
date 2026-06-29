---
name: 003-move-vfx-buffer-off-scope
description: Move the VFX staging buffer off CombatScope onto a dedicated VfxSingleton entity owned by the dispatch system
---

# 003 — Move the VFX staging buffer off `CombatScope`

## Goal

The `DynamicBuffer<VfxSpawnRequestElement>` no longer lives on the gameplay
`CombatScope` entity. It lives on a dedicated entity created and owned by
`CombatVfxDispatchSystem`, resolved by producers through a new `VfxSingleton`
tag. This mirrors `CombatBatchedRenderSystem` owning its
`CombatRenderResourceRegistry` singleton entity.

## Changes

### New tag — [VfxEcsComponents.cs](../../Assets/Scripts/System/Vfx/VfxEcsComponents.cs)

Add a zero-size marker:

```csharp
// ECS Lifecycle: VFX staging singleton tag; created by CombatVfxDispatchSystem.OnCreate
// (or by test setup); hosts the DynamicBuffer<VfxSpawnRequestElement> drained each frame.
public struct VfxSingleton : IComponentData
{
}
```

### [CombatVfxDispatchSystem.cs](../../Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs) — own the entity

- `OnCreate`: create the staging entity and its buffer, then build the query
  against the dedicated entity:
  ```csharp
  protected override void OnCreate()
  {
      Entity vfxEntity = EntityManager.CreateEntity(typeof(VfxSingleton));
      EntityManager.AddBuffer<VfxSpawnRequestElement>(vfxEntity);

      vfxQuery = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<VfxSingleton>());
  }
  ```
  (Rename the `scopeQuery` field to `vfxQuery`.) Guard against double-create the
  same way other singletons do if `OnCreate` can run on an already-populated
  world — not expected here, but if a `VfxSingleton` already exists, reuse it
  instead of creating a second (a second would break the singleton query).
- `OnUpdate`: resolve the entity from `vfxQuery.GetSingletonEntity()` and get
  the buffer from it (same as before, different query). The drain body is from
  002.

### [CombatEcsComponents.cs](../../Assets/Scripts/System/Common/CombatEcsComponents.cs) — stop coupling to scope

- In `CombatScopeOwner.Acquire`, remove
  `entityManager.AddBuffer<VfxSpawnRequestElement>(ownedScope);` (line 92).
- Drop the `using PlayGround.System.Vfx;` import if nothing else in the file
  needs it.

### Producers — resolve `VfxSingleton` instead of `CombatScope`

Replace the scope-entity resolution used **only** to host the VFX buffer. Each
of these passes `Scope = …` into a flush job; repoint it to the VFX singleton.
Keep their `CombatScope` usage for non-VFX purposes intact.

- [CombatLifetimeSystem.cs](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs)
  — `scopeQuery` is used solely to get the flush `Scope` (line 35). Switch the
  query to `VfxSingleton` and pass that entity as `VfxFlushJob.Scope`. (Confirm
  `CombatScope` isn't used elsewhere in this system; if not, replace the query
  type outright.)
- [AoePulseVfxSystem.cs](../../Assets/Scripts/System/Aoe/AoePulseVfxSystem.cs)
  — same: `scopeQuery` (line 19, 34) only feeds the flush `Scope`. Switch to
  `VfxSingleton`.
- [ProjectileCollisionSystem.cs:130](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs#L130)
  — `Scope = SystemAPI.GetSingletonEntity<CombatScope>()` → `GetSingletonEntity<VfxSingleton>()`.
  This system uses `CombatScope` only for the flush host here; verify and switch.
- [ImpactAoeCollisionSystem.cs:49,124](../../Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs#L49)
  — it resolves `combatScope` via `TryGetSingletonEntity<CombatScope>()` and
  passes it as `Scope` (line 124). Add a `VfxSingleton` resolution for the flush
  host and pass that instead. Keep any genuine `CombatScope` dependency if one
  exists; if the scope entity is fetched purely for the VFX flush, switch the
  lookup to `VfxSingleton`.
- [LingeringAoeCollisionSystem.cs:49,124](../../Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs#L49)
  — same as Impact.
- [AoeSpawnExpansionSystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs)
  — it writes the VFX buffer directly from `scopes[0]` (lines 100-137). Resolve
  the VFX buffer from the `VfxSingleton` entity instead
  (`SystemAPI.GetSingletonEntity<VfxSingleton>()` →
  `EntityManager.GetBuffer<VfxSpawnRequestElement>(vfxEntity)`). The
  `_scopeQuery` for `AoeSpawnEvent` drain stays (that is gameplay); only the VFX
  buffer source moves. Guard the VFX write with the singleton existing rather
  than `scopes.Length > 0`.

## Verification notes for the implementer

- Before switching each producer's query, confirm whether that system uses the
  `CombatScope` singleton for anything besides hosting the VFX buffer. The greps
  show these systems fetch the scope entity only to pass as the flush `Scope`,
  but verify per file (especially `ImpactAoeCollisionSystem` /
  `LingeringAoeCollisionSystem`, which early-out on
  `TryGetSingletonEntity<CombatScope>()`).
- `SystemAPI.GetSingletonEntity<VfxSingleton>()` is valid in Burst `ISystem`
  (same usage shape as the existing `CombatScope` calls).

## Acceptance criteria

- `VfxSingleton` tag exists; `CombatVfxDispatchSystem` creates the entity +
  buffer in `OnCreate` and resolves it in `OnUpdate`.
- `CombatScope` no longer has a `VfxSpawnRequestElement` buffer; no production
  code reads/writes the VFX buffer from the scope entity.
- All flush jobs and `AoeSpawnExpansionSystem` write to the `VfxSingleton`
  entity's buffer.
- Runtime VFX still appears in the default world (the dispatch system creates the
  entity before simulation runs).

## Dependencies

- Builds on 001 (field removed) and 002 (dispatch drain rewritten). The
  `OnCreate`/`OnUpdate` edits here are in different regions than 002's drain
  rewrite.
- Requires 004 (test setup) to land together so play-mode tests still find the
  singleton.

## Scope

Medium. One new tag, one system `OnCreate`/query change, one scope-owner line
removed, six producer repoints.
