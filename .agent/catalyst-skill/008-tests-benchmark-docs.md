# 008 — Benchmark, regression pass, and docs

## Goal

Prove the 200-catalyst target, characterize the trigger-spawn burst, and record
the domain in the docs so the next reader does not have to re-derive it.

## Benchmark (user runs it; agent reports only from the exported XML)

Add a catalyst case to the existing large benchmark scene setup:
- 200 live catalysts, all three shapes represented.
- Dense friendly projectile volleys crossing the rings, plus one lingering AOE
  parked over a ring.
- Catalyst triggers authored to a cheap effect, then to an expensive one, so
  trigger cost and overlap cost can be read apart.

Measure and record:
- `TargetSpatialHashSystem` gather/build delta with the catalyst lane on versus
  zero catalysts (the zero case must be a count check).
- `ProjectileDiscreteCollisionSystem` and `ProjectileContinuousCollisionSystem`
  delta with 0 / 10 / 200 catalysts.
- `AoeCollisionCore` delta for the parked lingering AOE.
- Trigger-spawn events per frame at the worst crossing, against the existing
  spawn/expansion cost.

Add profiler markers where the existing systems already carry them, so the
catalyst scan shows up separately rather than inside the unit scan.

**Open decision this settles:** whether the `projectiles x catalysts` first-crossing
burst needs a per-frame cap. Do not add one before the numbers say so; if it is
needed, it belongs in `CatalystHitEmission` beside the AOE budget constant, not
in the spawn pipeline.

## Regression pass

Existing suites that must stay green because this plan edits shared files —
collision, broadphase, lifetime, pool cleanup, template registry, and the skill
compiler. Name the affected classes when handing the run to the user; do not
run them.

## Docs

- `Docs/reference/simulation/catalyst-system.md` (new) — domain doc in the shape
  of `projectile-system.md` / `targeted-system.md`: archetype, group identity
  and refresh-or-add rule, stateless orbit angle, broadphase lane, the
  spawn-without-hit-event rule, pierce and gating behavior, budgets.
- `Docs/reference/simulation/index.md` and `Docs/folder-structure.md` — add the
  catalyst runtime map entries.
- `Docs/reference/simulation/skill-ecs-simulation.md` — add Catalyst to the
  skill-domain table.
- `Docs/reference/game-logic/skill-gameplay-system.md` — catalyst skill,
  authoring steps, tag rules, and the `OnHitTrigger`-as-source note.
- `Docs/reference/game-logic/skill-modifiers.md` — record that catalyst uses
  `Duration`, `Rate`, and `ManaCost` and ignores damage/area/pierce supports.
- `Docs/contracts/spawn-events-and-commands.md` — `CatalystSpawnEvent` /
  `CatalystSpawnCommand`, including the `Owner` field that the other events do
  not have.

## Acceptance criteria

- Benchmark XML under `Logs/`, named per `Docs/testing.md` §*Result Files*,
  showing the 200-catalyst case at target frame rate.
- Zero-catalyst cost is indistinguishable from the pre-change baseline.
- Every doc above updated; no doc still lists three combat domains.
- The burst-cap decision recorded in this file with the measured numbers behind
  it.

## Dependencies

001-007.

## Scope

Medium. Mostly measurement and writing; no new runtime behavior unless the
benchmark forces the cap.
