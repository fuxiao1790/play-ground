# 006 — Docs

Update the design references to describe echo and the now-symmetric AOE spawn
path.

## Changes

### `Docs/reference/game-logic/skill-system.md`
- `AoeDefinition` / `LingeringAoeDefinition` behavior lists: replace `count` with
  `echoCount, scatterRadius` (the AOE analog of projectile `count, spreadDegrees`).
- Supports table (~line 384): add
  `| Multiple AOEs | IAoeBehaviorModifier | Sets AOE echoCount, scatterRadius |`.
- Compatible-tags table (~line 398): add `| Multiple AOEs | Aoe |`.
- Interval directionality note (~line 537): update "AOE child … `Count > 1` emits
  multiple AOE spawn events at that center" to describe echo scatter — copies fan
  across `scatterRadius` (random disk) around the center; `spawnCount` remains
  additive with the child's `echoCount`.

### `Docs/reference/simulation/aoe-system.md`
- Spawn Pipeline (~line 140): replace "AOE expansion is currently simple because
  AOE multiplicity is mostly resolved before the event reaches ECS" with: the
  expansion fans `EchoCount` copies and scatters each within `ScatterRadius`
  (random disk, seeded by `JitterSeed`), computing per-copy bounds — the same
  event→command fan-out contract as projectiles.
- Entity Data / command description: note `AoeSpawnCommand` carries `EchoCount`
  and `ScatterRadius`.
- Known Gaps (~line 324): remove/annotate "AOE expansion is intentionally minimal
  … future scatter/pattern work should live in `AoeSpawnExpansionSystem`" — that
  gap is now closed by echo scatter.

## Acceptance criteria
- Docs describe echo count + scatter, list the new support and its tag, and no
  longer describe AOE count as same-center stacking.
- The projectile/AOE spawn-path symmetry is stated explicitly.

## Dependencies
Last; reflects 001–005 as landed.

## Scope
Small: documentation only.
