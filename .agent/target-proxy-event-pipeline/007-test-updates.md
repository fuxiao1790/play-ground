# 007 — PlayMode Test Updates

## Change

Any test that hand-rolls a scope entity and calls `CombatTargetProxy.Create/Push/Delete/PushResourceMaxes`
directly needs two kinds of change:

**(a) Buffer wiring** — add the three new buffer types in `SetUp`, mirroring the
existing `AddBuffer<ProjectileSpawnEvent>(...)` calls. Confirmed exact locations:
`ProjectileCollisionSimulationTests.cs:71-75`, `AoeSimulationTests.cs:85-88`.

**(b) World tick between enqueue and assertion** — every call site that currently does
`Entity proxy = CombatTargetProxy.Create(...)` (or reads/asserts proxy component data
immediately after `Push`/`Delete`/`PushResourceMaxes`) must insert a world update (or
directly invoke the relevant apply system) between the call and the assertion, and
switch from using the return value of `Create` (now `bool`) to reading
`target.CombatTargetProxy` after the tick. Confirmed exact call sites:
`AoeSimulationTests.cs:546,1557`, `ProjectileCollisionSimulationTests.cs:94,106,122,135,145,593`.

`AoeSimulationTests.cs:724-748`
(`TargetProxyLifecycle_CreatePushDeleteControlsCollisionVisibility`) is a good
representative case to fix first — it currently asserts `entityManager.Exists(target.Proxy)`
with zero ticks between `Create`/`Push`/`Delete` and the assertions, and exercises all
three mutation paths in one test.

`MobSpawnControllerPlayModeTests.cs`/`AoePlayModeTests.cs` go through the real
`CombatRoot`/`CombatScopeOwner` (not a hand-rolled scope), so they pick up the new
buffers automatically via 001's `CombatScopeOwner` change — no direct buffer-wiring
edits needed there, only tick-timing adjustments if any existing assertion happens to
be same-frame with no tick in between (audit `MobSpawnControllerPlayModeTests.cs:69-70`
and `AoePlayModeTests.cs:1136-1140` specifically, both of which assert on
`target.CombatTargetProxy`/`reused.CombatTargetProxy` shortly after a spawn/reuse path).

## Acceptance Criteria

- Full existing PlayMode suite (`AoeSimulationTests.cs`, `ProjectileCollisionSimulationTests.cs`,
  `AoePlayModeTests.cs`, `MobSpawnControllerPlayModeTests.cs`, `ProjectileTrackingSimulationTests.cs`)
  passes after updates, with no reduction in what's actually being asserted (i.e. don't
  weaken assertions to make them pass — fix the timing).
- No test relies on a synchronous `Create`/`Push`/`Delete` bypass; all go through the
  same enqueue → tick → assert shape.

## Dependencies

Depends on 001-006 (the full pipeline must exist and be correct for tests to validate
against).

## Scope

Medium-large in surface area (many call sites across several large test files) but
mechanically repetitive — the same "insert a tick, read the field instead of the return
value" pattern applies at each site once established on the representative case above.
