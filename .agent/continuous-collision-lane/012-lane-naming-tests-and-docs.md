# 012 — Lane Naming: Tests And Documentation

**Depends on:** 010, 011
**Scope:** medium — pure rename

## Goal

Finish the vocabulary change everywhere it is read rather than compiled. Tasks 010 and 011
already update every *reference* in test bodies (they have to, or nothing builds); this task
renames the test files, classes, methods, and helpers that carry the lane word, plus the docs.

## Test files

| Now | New |
|---|---|
| `Assets/Tests/PlayMode/SweptProjectileSimulationTests.cs` | `ProjectileContinuousSimulationTests.cs` |
| `Assets/Tests/EditMode/SweptProjectileAuthoringEditModeTests.cs` | `ProjectileContinuousAuthoringEditModeTests.cs` |
| `Assets/Tests/EditMode/CombatSweepMathEditModeTests.cs` | **unchanged** — see the `CombatSweepMath` decision in [010](010-lane-naming-simulation.md) |

Move the `.cs.meta` with each. Class names follow the file names.

## Test members

`ProjectileContinuousSimulationTests`:

| Now | New |
|---|---|
| `CreateSweptProjectile` | `CreateContinuousProjectile` |
| `SweptLaneHasNoTrackingAndMovesExactlyOnce` | `ContinuousLaneHasNoTrackingAndMovesExactlyOnce` |
| `SweptImpactAoeSpawnsAtImpactInsteadOfFrameEnd` | `ContinuousImpactAoeSpawnsAtImpactInsteadOfFrameEnd` |
| `ContactGatePreventsRepeatHitAcrossMultipleSweepFrames` | `ContactGatePreventsRepeatHitAcrossMultipleStepFrames` |

`ProjectileContinuousAuthoringEditModeTests`:

| Now | New |
|---|---|
| `OnValidate_ReportsAuthoredSweptTrackingConflict` | `OnValidate_ReportsAuthoredContinuousTrackingConflict` |
| `Compiler_BlocksSweptProjectileWhenHomingSupportEnablesTracking` | `Compiler_BlocksContinuousProjectileWhenHomingSupportEnablesTracking` |
| `SweptFlagReachesCommandsForDirectIntervalImpactAndStackPaths` | `ContinuousFlagReachesCommandsForDirectIntervalImpactAndStackPaths` |
| `SweepRouting_IsNotStatOrBehaviorContextModifiable` | `LaneRouting_IsNotStatOrBehaviorContextModifiable` |

`ProjectileSpawnPipelineTests` — `DiscreteLaneDoesNotReuseDisabledSweptSlots` →
`DiscreteLaneDoesNotReuseDisabledContinuousSlots`; locals `sweptSlots` / `sweptSlot` /
`disabledSwept` → `continuousSlots` / `continuousSlot` / `disabledContinuous`.

`CombatPoolCleanupSystemTests` — `CreateDisabledSweptProjectiles` →
`CreateDisabledContinuousProjectiles`, `DisabledSweptProjectileCount` →
`DisabledContinuousProjectileCount`, `SweptProjectileQuery` → `ContinuousProjectileQuery`.

**Test names are the record of what the feature guarantees.** Two are worth reading rather
than mechanically substituting: `LaneRouting_IsNotStatOrBehaviorContextModifiable` is more
accurate than the original — what is unmodifiable is which lane the projectile lands in, not a
"sweep" setting; and `…AcrossMultipleStepFrames` keeps the meaning ("more than one integration
step") without implying a swept-geometry concept.

## Documentation

Six files under `Docs/` reference the old vocabulary:

- `Docs/reference/simulation/projectile-system.md`
- `Docs/reference/simulation/ecs-notes.md`
- `Docs/reference/game-logic/skill-system.md`
- `Docs/contracts/spawn-events-and-commands.md`
- `Docs/folder-structure.md`
- `Docs/coding-standards.md`

`folder-structure.md` lists the five renamed files by path and must match exactly — it is the
"find files quickly" index named in `Docs/project-overview.md`, so a stale entry is worse than
no entry.

Apply the same split as the code: **lane references become `discrete`/`continuous`; geometry
references may keep "swept volume", "swept box", "travel corridor".** Where a doc says "the
swept lane resolves collision against the swept box", the first is a lane and the second is
geometry.

## Plan documents

The plan directory and eight of its files are named in the superseded vocabulary. Rename last,
after the code lands, so nothing is chasing a moving target mid-refactor:

| Now | New |
|---|---|
| `.agent/swept-projectile-lane/` | `.agent/continuous-collision-lane/` |
| `001-swept-types.md` | `001-continuous-types.md` |
| `002-swept-box-geometry.md` | `002-corridor-geometry.md` |
| `006-sweep-origin-capture.md` | `006-step-origin-capture.md` |
| `007-swept-collision-system.md` | `007-continuous-collision-system.md` |
| `index.md` frontmatter `name: swept-projectile-lane` | `name: continuous-collision-lane` |

003, 004, 005, 008, 009 keep their file names; their bodies still need the vocabulary pass.

The cross-links in `index.md` and between task files are relative and **will break** — fix them
in the same commit. Task bodies describing the superseded design (the Revision Log's "Was"
columns, `SweptProjectileMovementSystem` in 006's history note) keep the old identifier names:
they are describing code that no longer exists, and rewriting them to the new vocabulary would
make the log claim a rename happened twice.

## Acceptance Criteria

- `grep -ri swept` over `Assets/`, `Docs/`, and `.agent/` returns only: `CombatSweepMath` and
  its members, deliberate geometry prose, and Revision Log entries describing superseded
  designs.
- Test **counts** are identical before and after — a rename that drops or duplicates a test is
  the failure mode here, and the compile blocker means the runner is the only thing that
  catches it.
- `Docs/folder-structure.md` lists the five renamed system files at their new paths.
- Every renamed test file moved with its `.cs.meta`.
- Plan cross-links resolve after the directory rename.
- No test body logic changed — assertions, setup, and frame counts identical.
