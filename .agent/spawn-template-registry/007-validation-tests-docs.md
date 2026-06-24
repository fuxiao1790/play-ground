# 007 — Validation, tests, docs

## Scope

Verify the crash fix, the events-as-templates registry semantics, and the unified tick system;
update docs.

## Changes

1. **Tests**:
   - Regression: `BareMinimumPrototypePlayModeTests`, `AoePlayModeTests`,
     `ProjectileSpawnPipelineTests` pass unchanged (proj→proj cadence + deterministic ids).
   - EditMode: keep `sizeof(AoeSpawnCommand) < 4096`; update the dedup test to register
     `ProjectileSpawnEvent`/`AoeSpawnEvent` (not `…TemplateData`) and assert identical-behavior
     events share one key.
   - PlayMode: the lingering-AOE-source → projectile/AOE child test
     (`LingeringAoeIntervalChildren…UntilSourceExpires`) — children spawn over the source
     lifetime, stop at expiry, no Burst exception/freeze. Add a zero-interval guard test (a
     spawner configured with interval 0 must not hang and must not spawn unboundedly).
   - Dynamic count: changing `spawnCount` → new key; revert → prior key reused.

2. **Docs** (`Docs/game-logic/skill-system.md`): update the registry section to "spawn events are
   stored as templates" — one template kind per domain holding `NativeHashMap<Hash128, …SpawnEvent>`,
   the unified `TimedSpawnComponent` + single `TimedSpawnSystem`, the canonical
   event→expansion→command→apply path (no conversion), content-hash dedup, never-recycle, and the
   loop guard. Remove references to `…TemplateData` and the two old tick systems.

## Acceptance criteria

- Listed PlayMode/EditMode tests green; no editor freeze on fire.
- `Allocation size is too large` exception gone.
- Docs reflect the events-as-templates + unified-system design.

## Dependencies

001–006.
