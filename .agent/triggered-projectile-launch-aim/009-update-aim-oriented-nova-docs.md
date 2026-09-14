# 009 - Update Aim-Oriented Nova Docs

## Change

Correct completed task `006` documentation that currently describes shot-index-0
replacement and preserved normal pattern/RNG. Update:

- `Docs/reference/game-logic/skill-gameplay-system.md`
- `Docs/contracts/spawn-events-and-commands.md`
- `Docs/reference/simulation/projectile-system.md`
- `Docs/reference/simulation/spawn-template-registry.md`
- `Docs/flows/spawn-event-to-entity.md`

## Acceptance Criteria

- Docs define successful launch aim as full radial nova oriented from acquired
  direction.
- Formula and deterministic slot `0` direct-target behavior stated.
- Docs state remaining shots use equal `360/count` offsets.
- Docs state successful aimed nova bypasses stored forward/side-spray pattern and
  spread/jitter; failed/disabled acquisition preserves them.
- Both lanes, trigger-only authoring, manual root aim exclusion, homing independence,
  target-hash reuse, and unchanged archetypes remain documented.
- No stale statement says only shot `0` changes or later-shot RNG matches disabled
  policy.

## Dependencies

Depends on `007` behavior and `008` acceptance being stable.

## Estimated Scope

Small documentation correction.
