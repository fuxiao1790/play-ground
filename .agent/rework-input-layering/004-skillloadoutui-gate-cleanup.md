# 004 — `SkillLoadoutUi` uses modal-suspend; UI stays on the top layer

## Goal
Point the picker modal at the new single-flag suspend API and confirm the skill
bar's layering behavior. No pointer-over-UI bookkeeping remains.

## Changes to `Assets/Scripts/SkillUi/SkillLoadoutUi.cs`
- `OpenPicker`: replace `playerRoot?.SetGameplayInputGate(true, true);` with
  `playerRoot?.SetGameplayInputSuspended(true);`.
- `ClosePicker`: replace `playerRoot?.SetGameplayInputGate(false, false);` with
  `playerRoot?.SetGameplayInputSuspended(false);`.
- No other logic changes; `playerRoot` reference and its resolution stay as-is.

## Layering behavior (verify, likely no code change)
- `#bar` and its buttons are `picking-mode: Position` by default → they consume
  their own clicks; the world surface behind them never fires. This is the fix
  for "clicking a bar button also fires."
- The bar's dark background padding also consumes clicks (does not fire). That is
  acceptable/desired. If any region should be click-through to fire, set that
  element's `picking-mode: ignore` — that is the explicit per-element propagation
  control the design exposes.

## USS
- Ensure `.world-input-surface` exists in
  `Assets/Scripts/Ui/SkillLoadout/SkillLoadoutUi.uss` (added in task 002).
- No change needed to `.skill-bar` / `.picker-modal` for the core fix.

## Acceptance Criteria
- No remaining calls to `SetGameplayInputGate` anywhere (grep clean).
- Opening the picker freezes movement/dash; closing restores it.
- Clicking any bar button never fires the equipped skill.

## Dependencies
003 (new `SetGameplayInputSuspended` API).

## Scope
Trivial.
