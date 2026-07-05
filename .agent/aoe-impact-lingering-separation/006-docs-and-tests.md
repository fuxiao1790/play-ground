# 006 — Docs + tests

## Goal
Update documentation and tests to the split event types and expansion systems. Last task.

Depends on: 001–004 (005 optional).

## Tests
Sites reflecting on the old names (from grep):
- [AoeSimulationTests.cs](../../Assets/Tests/PlayMode/AoeSimulationTests.cs) — creates
  `AoeSpawnExpansionSystem`, reflects a private `EventQueue` field
  ([:1702](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L1702)), asserts `AoeEventQueue().Count`,
  and authors `DetonationKind = StackDetonationKind.Aoe` ([:1556](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L1556)).
  Repoint to the two new expansion systems + the matching event queue by expected variant;
  update detonation kinds to `ImpactAoe`/`LingeringAoe`.
- [SpawnCommandUnificationTests.cs](../../Assets/Tests/PlayMode/SpawnCommandUnificationTests.cs),
  [ProjectileCollisionSimulationTests.cs](../../Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs),
  [AoePlayModeTests.cs](../../Assets/Tests/PlayMode/AoePlayModeTests.cs) — same style of updates
  (system creation, detonation kind).
- Add/adjust a test asserting an **impact** template routes to the impact lane and a
  **lingering** template to the lingering lane end-to-end (event → command list → entity),
  proving the producer-side classification.

## Docs
Update references to `AoeSpawnEvent` / `AoeSpawnExpansionSystem`:
- [Docs/contracts/spawn-events-and-commands.md](../../Docs/contracts/spawn-events-and-commands.md)
- [Docs/reference/simulation/aoe-system.md](../../Docs/reference/simulation/aoe-system.md),
  [project-aoe-system-common.md](../../Docs/reference/simulation/project-aoe-system-common.md),
  [spawn-template-registry.md](../../Docs/reference/simulation/spawn-template-registry.md),
  [project-ecs-implementation.md](../../Docs/reference/simulation/project-ecs-implementation.md)
- [Docs/flows/spawn-event-to-entity.md](../../Docs/flows/spawn-event-to-entity.md)
- Document the new invariant: **AOE variant (impact/lingering) is decided at authoring from
  child `Lifetime` and carried on `IntervalChildKind` / `StackDetonationKind`; producers route
  by it, exactly like projectile vs aoe.**
- Update `Docs/todo.md`: strike the "lingering aoe and impact aoe are not separated cleanly"
  item (collision/spawn-expansion/spawn-apply now uniform).

## Acceptance criteria
- All PlayMode/EditMode AOE + projectile spawn tests compile and pass.
- Docs describe the split lanes and the authoring-time classification rule.

## Scope: medium (mechanical renames + one new routing assertion + doc prose).
