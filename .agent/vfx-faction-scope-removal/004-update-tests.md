---
name: 004-update-tests
description: Create the VFX singleton entity in play-mode test setups instead of adding the VFX buffer to the scope entity
---

# 004 — Update play-mode test setup

## Goal

The 4 play-mode tests that add `VfxSpawnRequestElement` to the `CombatScope`
entity must instead create a dedicated `VfxSingleton` entity hosting that
buffer, because (a) the buffer no longer lives on the scope (003) and (b) these
tests don't run `CombatVfxDispatchSystem`, so nothing auto-creates the singleton.

## Affected tests

Each currently does `entityManager.AddBuffer<VfxSpawnRequestElement>(scopeEntity);`
right after building the scope entity:

- [ProjectileSpawnPipelineTests.cs:41](../../Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs#L41)
- [SpawnCommandUnificationTests.cs:51](../../Assets/Tests/PlayMode/SpawnCommandUnificationTests.cs#L51)
- [AoeSimulationTests.cs:70](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L70)
- [ProjectileCollisionSimulationTests.cs:55](../../Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs#L55)

## Change (per file)

Replace the scope-buffer add with a dedicated entity:

```csharp
// remove: entityManager.AddBuffer<VfxSpawnRequestElement>(scopeEntity);
Entity vfxEntity = entityManager.CreateEntity(typeof(VfxSingleton));
entityManager.AddBuffer<VfxSpawnRequestElement>(vfxEntity);
```

- Track `vfxEntity` as a field if a test later reads the VFX buffer for
  assertions; otherwise a local in `SetUp` is fine (the entity lives for the
  world's lifetime and is freed on `testWorld.Dispose()`).
- No teardown change needed — `DynamicBuffer` memory is owned by the
  `EntityManager` and released with the world.
- Ensure exactly one `VfxSingleton` exists per test world (these tests don't add
  `CombatVfxDispatchSystem`, so there's no auto-created one to collide with). If
  any future/edited test does include the dispatch system, it must NOT also
  create the entity manually.

## Verification

- Check whether any of these tests assert on VFX buffer contents (search for
  `VfxSpawnRequestElement` reads). The greps show only the setup `AddBuffer`
  calls today, so a setup-only change should suffice — but confirm before
  finalizing.
- `ProjectileTrackingSimulationTests` creates a scope entity but does NOT add the
  VFX buffer (it doesn't run VFX-emitting flush against a VFX host); confirm it
  still passes. If it runs a system that now requires `VfxSingleton`, add the
  entity there too.

## Acceptance criteria

- All 4 listed tests create a `VfxSingleton` entity with the
  `VfxSpawnRequestElement` buffer and no longer add that buffer to the scope.
- The VFX-emitting simulation systems resolve `GetSingletonEntity<VfxSingleton>()`
  successfully under test.
- Play-mode suite passes.

## Scope

Small. ~2 line change in each of 4 files plus a verification pass.
