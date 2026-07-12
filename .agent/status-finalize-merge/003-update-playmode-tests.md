# 003 — Update PlayMode tests (compile rider on 001)

## Goal

Three PlayMode tests register or hold `StatusProcessSystem`. After 001 that type
is gone; the merged `CombatApplyFinalizeSingleSystem` now performs the status
pass, so the tests must stop referencing the deleted system while still driving
the same simulation.

## Changes

- **`AoeSimulationTests`**
  ([AoeSimulationTests.cs:35](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L35),
  [:55](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L55)):
  remove the `private StatusProcessSystem statusProcess;` field and its
  `GetOrCreateSystemManaged<StatusProcessSystem>()` init. If the test ticks
  `statusProcess` explicitly in its update loop, replace those ticks with the
  merged finalize system's tick (the system that now runs the status pass). If it
  only created it to register it in the group, delete the lines — the merged
  system is already registered as the finalize system.
  The stacking-detonation assertion around
  [:1112](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L1112) stays; only its
  comment referencing "spawned by StatusProcessSystem" should be updated.

- **`SpawnCommandUnificationTests`**
  ([:53](../../Assets/Tests/PlayMode/SpawnCommandUnificationTests.cs#L53)):
  remove `simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<StatusProcessSystem>());`.
  Ensure the merged finalize system is in the update list (it already must be for
  finalize); the status pass rides along with it.

- **`ProjectileCollisionSimulationTests`**
  ([:52](../../Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs#L52)):
  same removal as SpawnCommandUnificationTests.

## Acceptance criteria

- `rg "StatusProcessSystem"` over `Assets/Tests` returns no matches.
- Each affected test still registers/ticks the merged finalize system so the
  status pass executes during the test.
- **User-run:** all three suites compile and pass in the Unity Test Runner.

## Scope / risk

Low–medium. Test wiring only, but the tests are the *only* real verification the
harness can point at — they must actually still exercise the status pass, not
just compile. Confirm each affected test still advances stack lifetime /
detonation (i.e. the merged system is genuinely ticked), not silently dropped.
