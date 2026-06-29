# 007 — Update PlayMode tests

**Files:**
`Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`,
`Assets/Tests/PlayMode/AoeSimulationTests.cs`,
`Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`,
`Assets/Tests/PlayMode/SpawnCommandUnificationTests.cs`
**Depends on:** 001–005 (so producers null-guard the collector)
**Scope:** small

## Change

Each test currently sets up the VFX singleton manually:
```csharp
Entity vfxEntity = entityManager.CreateEntity(typeof(VfxSingleton));
entityManager.AddBuffer<VfxSpawnRequestElement>(vfxEntity);
```
Remove both lines from all four files.

After the producer changes, VFX writing is guarded by
`vfx != null && vfx.HasQueue`. Test worlds that don't create
`CombatVfxDispatchSystem` simply skip VFX writes — no setup needed and no assert
changes (none of these tests assert on VFX output).

## Notes

- Confirm none of the four tests later reference `vfxEntity`,
  `VfxSpawnRequestElement`, or assert buffer contents. If one does, repoint it at
  a `CombatVfxDispatchSystem` instance and read `PendingSpawns` instead. (Grep
  shows only the two setup lines per file today.)
- If any test world *does* include `CombatVfxDispatchSystem` and asserts VFX, it
  must add that system explicitly; current tests do not.

## Acceptance criteria

- No `VfxSingleton` / `VfxSpawnRequestElement` reference remains in `Assets/Tests`.
- All four PlayMode test suites compile and pass unchanged in intent.
