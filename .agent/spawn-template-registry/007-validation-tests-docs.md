# 007 — Validation, tests, docs

## Scope

Verify the crash fix and the registry semantics; update docs.

## Changes

1. **Tests**:
   - Regression: `Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs`,
     `AoePlayModeTests.cs`, `ProjectileSpawnPipelineTests.cs` pass unchanged (proj→proj cadence
     and deterministic ids preserved).
   - New EditMode/PlayMode coverage:
     - **Dedup**: two compiled setups with identical child behavior resolve to the same
       `TemplateKey` and a single registry entry; differing behavior → distinct keys.
     - **Dynamic count**: changing `spawnCount` yields a new `TemplateKey`/entry; re-selecting a
       prior count reuses its entry.
     - **Crash repro**: enter Play with the lingering-AOE-source → AOE/projectile child loadout
       that previously threw; assert children spawn over the source lifetime, stop at expiry,
       and no Burst exception is raised.

2. **Docs**: update `Docs/game-logic/skill-system.md` to describe the spawn-template registry —
   the single template kind per domain, the three data tiers (registry / slim timer config /
   hot timer state), content-hash dedup, and the never-recycle policy (with the refcount +
   grace-period sweep noted as the future enhancement).

## Acceptance criteria

- All listed PlayMode/EditMode tests green.
- `sizeof(AoeSpawnCommand)` reduced below the `NativeStream` ~4 KB block; the
  `Allocation size is too large` exception is gone.
- Docs reflect the registry design.

## Dependencies

001–006.
