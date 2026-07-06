# 008 - Docs And Cleanup

## Goal

Update design docs and remove stale assumptions after lifecycle refactor lands.

## Scope

- Update render contract to describe sprite gate, not generic render/active gate.
- Update spawn-events-and-commands contract to state:
  commands may carry lifecycle inputs, but ECS apply derives lifecycle state.
- Update ECS implementation notes:
  - `Active` means live pool slot.
  - `Arming` and `Armed` are active.
  - `Dead` is disabled `Active`.
  - lifecycle tick computes armed delta.
  - death commit owns reusable-slot transition.
- Update VFX docs with arming trigger/duration behavior.
- Update stats docs if active visual counts become sprite-visible counts.
- Mark old AOE-only `.agent` plans as superseded by common lifecycle plan.

## Acceptance Criteria

- Docs match implemented lifecycle semantics.
- No doc says `CombatRenderActiveTag` means active entity rendering if the type
  has been renamed.
- No doc says future windup is AOE-only.

## Dependencies

Final cleanup after code tasks.

## Estimated Scope

Low to medium.
