# 006 — PlayMode tests + scene wiring / verification

**Scope:** medium. **Depends on:** 001-005.

## New file: `Assets/Tests/PlayMode/MobSpawnControllerPlayModeTests.cs`
Follow the fixture patterns already in
[BareMinimumPrototypePlayModeTests.cs](../../Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs)
(`CreateProjectileHitFixture` / `CombatAtlasTestFixture`, tagged combat root). Build mob
prefabs via `MobSpawnTable.Configure(new[]{ mobPrefab })` and a code-created `SpawnController`.

**Test Hooks rule (coding-standards.md):** observe only through public runtime APIs and real
gameplay effects — reference equality of the recycled `MobRoot`, `ActiveCount`, `IsAlive`,
`CurrentHealth`, real damage. Do **not** add production-only counters/flags for tests. Test-only
listener components live under `Assets/Tests/`.

Cases:
1. **Pool reuse** — spawn a mob, kill it (`SoftDie`), let one frame reclaim, spawn again →
   the same instance object is reused (assert **reference equality** of the returned `MobRoot`
   — a real effect, no Instantiate counter needed).
2. **Stream holds cap** — small cap, high rate; after N frames `ActiveCount == cap` and does
   not exceed it.
3. **Reused mob re-registers** — after reuse, a proxy exists and the mob takes damage again
   (drive a hit / assert `IsCombatTargetActive` and health decrement).
4. **Per-life reset** — a reused mob has `CurrentHealth == MaxHealth`, `IsAlive`, colliders/
   sprite enabled, zero velocity.
5. **Reclaim accounting** — killing K mobs decrements `ActiveCount` by K after the next frame
   and returns exactly K instances to the pool.

## Manual scene verification (`Assets/Scenes/BenchmarkLarge.unity` — currently zero mobs)
1. Create SO assets: a `MobSpawnTable` (Slime/Skeleton/Bat), a `ContinuousStreamBehaviour`
   (~300 cap on controller, ~50/s), a `FixedPointPlacement`.
2. Add a `SpawnController` GameObject; wire table/behaviour/placement, `cap = 300`,
   `combatRoot` = the tagged `PlayerProjectileRoot`, `target` = player; add a few child
   `SpawnPoint`s around the arena.
3. Enter Play: confirm mob count climbs to and holds at cap, kills recycle, and the Profiler
   shows steady-state ~0 GC alloc with **no** Instantiate/Destroy spikes after warmup.

## Acceptance criteria
- All five PlayMode cases pass in the editor test runner.
- Manual run holds at cap with recycling and flat GC.
