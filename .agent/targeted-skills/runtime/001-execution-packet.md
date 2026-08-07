# Task Execution Packet

## Task

001-damage-scale-hit-contract.md

## Goal

Add optional per-hit `DamageScale` to `CombatHitEvent`; finalizer treats `<= 0` as `1`, scales direct base damage before existing crit calculation.

## Files Allowed To Modify

- `Assets/Scripts/System/Application/CombatHitEvent.cs`
- `Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs`
- Existing or new directly relevant EditMode combat tests under `Assets/Tests/EditMode/`

## Files Allowed To Create

- One focused EditMode test file only if no suitable existing test file exists.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- Both allowed application files and existing CombatApply EditMode tests.

## Behavior To Preserve

- Every current producer leaves `DamageScale` unset and still deals full damage.
- Existing crit seed, ordering, and aggregation behavior stay unchanged.

## Behavior To Change

- A positive `DamageScale` multiplies `payload.DamageAmount` before crit multiplier.

## Relevant Global Context

- Damage fields are permitted on damage event contracts; no spawn-routing data.
- Combat paths must stay allocation-light; this is pure scalar job work.
- Preserve ECS lifecycle comment on event.

## Dependencies Confirmed

- None required. `CombatHitEvent` and `FinalizeCombatSingleJob` exist.

## Step-By-Step Instructions

1. Add `public float DamageScale;` to `CombatHitEvent`.
2. In direct-damage branch, calculate `scale = hit.DamageScale <= 0f ? 1f : hit.DamageScale`; then `baseAmount = math.max(0f, payload.DamageAmount * scale)` before crit roll.
3. Do not update current producers.
4. Add EditMode coverage for 1.0 + 0.5 aggregation, default/unset full damage, and scale-before-crit.

## Acceptance Criteria

- Existing combat tests unaffected.
- New test covers 1.5x total noncrit, unset = full, and 0.5 scale x 2 crit.

## Validation Required

- Run relevant EditMode tests or documented equivalent project test command.
- Run a compile/build check when available.

## Hard Boundaries

- No changes outside allowed files except imports required by task.
- Do not implement targeted-domain work.
- Stop on architectural ambiguity.
