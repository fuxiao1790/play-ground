# 006 - Update Launch Aim Documentation

## Change

Update design and contract docs after behavior/tests stabilize:

- `Docs/reference/game-logic/skill-gameplay-system.md`: trigger-edge authoring,
  root/manual aim exclusion, relationship to homing and collision mode.
- `Docs/contracts/skill-runtime-snapshots.md`: copied launch-aim mode/range.
- `Docs/contracts/spawn-events-and-commands.md`: policy remains in projectile
  template/command; event stays slim; acquisition happens per wave.
- `Docs/reference/simulation/projectile-system.md`: nearest-hostile launch flow,
  common target acquisition, both lanes, no post-launch steering.
- `Docs/reference/simulation/spawn-template-registry.md`: fields participate in
  immutable command-shaped template and hash.
- `Docs/flows/spawn-event-to-entity.md`: acquisition before fan-out and lane split.

## Acceptance Criteria

- Docs state authored source of truth is trigger link.
- Docs state root/player casts use manual aim or existing aim assist only.
- Docs state both discrete and continuous triggered projectiles may launch-aim.
- Docs distinguish one-time launch aim from discrete-only homing.
- Docs state successful acquisition uses target direction as radial nova angular
  origin; shot `0` points at target and remaining shots use equal full-circle offsets.
- Docs state successful aimed nova ignores forward/side-spray spread and jitter,
  while disabled/failed acquisition preserves existing authored pattern.
- Docs state contact-gated source target is excluded.
- Docs state target spatial hash and common acquisition helper are reused with
  proper build/consumer synchronization.
- Docs state no entity component/archetype/pool changes.

## Dependencies

Depends on finalized behavior and tests from `001` through `005`.

## Estimated Scope

Small-medium documentation update across gameplay, simulation, contracts, and flow.
