# 005 — Verification

## Goal
Confirm the stats pipeline produces correct, every-frame-honest values and binds
cleanly.

## Manual verification
1. Open the combat scene, add the `CombatStatsDisplay` GameObject (subtask 004).
2. Enter play mode and trigger attacks that spawn projectiles and AOEs.
3. Confirm:
   - On the first spawns of a new render type, `Spawn ECB` rises (cold create).
   - After projectiles/AOEs expire and slots free, subsequent spawns increment
     `Spawn reuse` instead (disabled-slot claim).
   - `Hit events` tracks collisions against targets that frame.
   - `VFX events` tracks dispatched VFX that frame.
4. Idle (no combat): all four read `0` (not stale) — confirms the early-return
   paths in subtask 002 zero the fields.
5. Cross-check against the Unity Profiler `Scripts` counters
   (`*SpawnApplySystem.Cold` / `.Reuse`) — the display's spawn totals should equal
   the sum of the per-system profiler counters.

## Optional PlayMode test
Following the existing `Assets/Tests/PlayMode/` suites (they already use
`GetExistingSystemManaged` and singletons):
- Drive a spawn through the apply systems, tick the world, then read
  `CombatStatsSingleton` from the gather system's world and assert
  `EntitiesSpawnedViaEcb + EntitiesSpawnedViaReuse` equals the spawn count.
- Assert `HitEventsCreated` matches a known hit scenario (reuse the patterns in
  `ProjectileCollisionSimulationTests` / `AoeSimulationTests`).
- Observe through the singleton component (a real diagnostic API), not test-only
  hooks (coding-standards "Test Hooks").

## Acceptance criteria
- Manual checks pass.
- If added, the PlayMode test passes and reads only public/diagnostic state.

## Dependencies
001–004.

## Scope
Small.
