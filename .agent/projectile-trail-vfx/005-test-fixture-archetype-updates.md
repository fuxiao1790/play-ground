# 005 — Test Fixture Archetype Updates

## Scope

Task 004 adds `in ProjectileTrailVfxComponent` as a required `Execute`
parameter on `ProjectileMovementJob`. Under Unity Entities' `IJobEntity`
codegen, every component named in `Execute` becomes a required term of that
job's implicit query. Any projectile entity a test hand-builds with
`entityManager.CreateEntity(typeof(ProjectileTag), ...)` — rather than through
`ProjectileDiscreteSpawnApplySystem`/`ProjectileContinuousSpawnApplySystem` —
and that does **not** include `ProjectileTrailVfxComponent` in that list will
silently stop matching `ProjectileMovementJob`'s query. That is not a compile
error: the entity simply never gets its position integrated again by that job
in whatever world the test runs. This is a real, mechanical regression risk in
existing tests, not a hypothetical one, and must be fixed alongside 004 rather
than discovered later as an unexplained test failure.

## Candidate Files (verify each, patch as needed)

Found via `grep "CreateEntity(" + "typeof(ProjectileContactGateElement)"` (the
full discrete/continuous archetype marker) and a follow-up check for
lighter/partial projectile fixtures:

- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs` (confirmed,
  lines 595–610 build the full archetype without a trail component).
- `Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs`
- `Assets/Tests/EditMode/CombatPoolCleanupSystemTests.cs`
- `Assets/Tests/PlayMode/ProjectileContinuousSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs` (confirmed,
  lines 316–327 build a lighter archetype — no `ProjectileContactGateElement`
  either — for tracking-only scenarios; check whether the test's world
  actually schedules `ProjectileMovementSystem` before deciding this one truly
  needs the new component, since a tracking-only fixture may never run
  movement at all).

This list was built by grepping for `CreateEntity(` co-occurring with
`typeof(ProjectileTag)`/`typeof(ProjectileContactGateElement)`; re-run the same
search at implementation time in case new test files were added since this
plan was written, and check each file's `[Test]`/`[UnityTest]` methods for
whether they actually step a world containing `ProjectileMovementSystem` —
only those are affected. A file that only exercises collision, tracking
acquisition math, or spawn-pipeline expansion without stepping movement does
not need the new component, even if it hand-builds a projectile entity.

## Change Per Affected File

Add `typeof(ProjectileTrailVfxComponent)` to the entity's component list in
each affected `CreateEntity(...)` call. No explicit `SetComponentData` call is
required afterward unless the specific test asserts on trail behavior — the
implicit all-zero default (`TrailId = 0, Width = 0, StepDistance = 0,
LastEmitPosition = float2.zero`) is correct for every existing test, since none
of them are testing trail VFX and `ProjectileMovementJob` returns immediately
on `TrailId <= 0`, before ever reading `StepDistance` or `LastEmitPosition`.

## Acceptance Criteria

- Every projectile entity built directly via `entityManager.CreateEntity` in a
  test that also exercises `ProjectileMovementSystem` carries
  `ProjectileTrailVfxComponent` after this task.
- No test's assertions change as a result — this task only restores each
  fixture's entities to matching the production query shape; it must not
  change any test's expected values.
- Per the project's testing rules, this plan does not run these tests. Name
  them precisely for the user to run after implementation:
  - EditMode: `CombatPoolCleanupSystemTests` (all methods touching
    projectile entities).
  - PlayMode: `ProjectileCollisionSimulationTests`,
    `CombatPoolCleanupSystemTests`, `ProjectileContinuousSimulationTests`,
    `ProjectileSpawnPipelineTests`, `ProjectileTrackingSimulationTests` (all
    methods).

## Dependencies

Depends on 003 (component must exist) and 004 (004 is what makes this
mandatory rather than optional). Should land in the same change set as 004 —
do not merge 004 without this, or the listed tests will regress silently.
