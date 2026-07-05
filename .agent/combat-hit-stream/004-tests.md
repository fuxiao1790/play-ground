# 004 — Repoint tests onto stream injection

## Goal
`AoeSimulationTests` injects synthetic hits by reflecting the `HitQueue` field and
`Enqueue`-ing. That field no longer exists. Repoint the two helpers onto the new
`InjectTestHits` entry added in 001.

## Changes
`Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `QueueStackHit` / `QueueDirectHit`: build a single `CombatHitEvent` and pass it via
  `InjectTestHits`:
  ```csharp
  var hits = new NativeArray<CombatHitEvent>(1, Allocator.Temp);
  hits[0] = new CombatHitEvent { … };
  hitApply.InjectTestHits(hits);   // hitApply resolved as today (via the world/system)
  hits.Dispose();
  ```
  If multiple hits are queued before one finalize tick, either accumulate into a list and
  inject once, or extend `InjectTestHits` to append into an existing
  `ProjectileHitStream` — check each test's call pattern (some queue several hits before a
  single `RunFinalize`).
- Remove the reflection `HitQueue()` helper (lines ~1693–1699) if nothing else uses it;
  otherwise repoint it.
- Resolve `hitApply` the way the surrounding test already obtains systems (the reflection
  helper currently reads it from a captured field — reuse that reference, just drop the
  queue reflection).

## Acceptance criteria
- PlayMode test assembly compiles.
- `QueueStackHit`/`QueueDirectHit`-based tests pass with identical assertions.

## Notes / risks
- Watch tests that enqueue **multiple** hits before a single finalize step — a naive
  one-lane-per-call `InjectTestHits` that overwrites `ProjectileHitStream` would drop
  earlier hits. Prefer collecting all hits for a tick and injecting once, or make
  `InjectTestHits` append.

## Dependencies
001 (needs `InjectTestHits`). Do after 002/003 so the full build compiles together.
