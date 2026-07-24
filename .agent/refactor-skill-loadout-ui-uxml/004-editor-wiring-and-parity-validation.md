# 004 — Editor Wiring + Play-Mode Parity Validation (USER STEPS)

## Goal

Wire the authored assets to the component in the Inspector and confirm the
refactored UI behaves identically to the pre-refactor imperative UI. These are
**user editor steps** (Inspector wiring and play-mode checks); the agent
provides instructions and cannot perform them.

## Editor Wiring Steps (User)

On the GameObject that holds `SkillLoadoutUi` + `UIDocument`:

1. Set the `UIDocument` **Source Asset** to `SkillLoadoutUi.uxml`.
2. Ensure a **PanelSettings** asset is assigned on the `UIDocument` (reuse the
   existing one if the scene already had it; UI Toolkit needs it to render).
3. Assign each serialized `VisualTreeAsset` template field on `SkillLoadoutUi`
   (node column, support button, trigger button, picker, picker choice — per the
   fields added in task 003).
4. Confirm the existing refs (`skillDriver`, `catalog`, `playerRoot`) are still
   assigned.
5. Check the Console: the UXML/USS assets import with no errors.

## Play-Mode Parity Checklist (User)

Enter play mode and verify against pre-refactor behavior:

- [ ] Bar appears bottom-center with `initialNodeCount` node columns and trigger
      buttons between them.
- [ ] Empty node shows `+` skill button and no cap buttons; picking a skill adds
      the `−`/support/`+` row.
- [ ] `+` cap adds a support slot up to `MaxSupportCount` then disables; `−` cap
      removes down to 0 then disables.
- [ ] Support / skill / trigger buttons open the picker for the correct target;
      `Clear` and `Cancel` work.
- [ ] A node fed by a trigger shows its skill button dimmed (opacity ~0.45).
- [ ] Cooldown numbers count down on the skill buttons while firing.
- [ ] Opening the picker gates gameplay input; closing restores it.
- [ ] Editing during a root skill's cooldown is rejected with the same message
      behavior (picker closes on rejected edit).
- [ ] Loadout save/restore still refreshes the bar (`LoadoutChanged`).

## Acceptance Criteria

- All wiring steps done; no import/console errors.
- Every parity checklist item passes.

## Dependencies

- **003** complete (component compiled with serialized template fields).

## Validation

- The checklist above is the validation. Record any parity gap as a defect
  against task 003, not a new design decision.

## Hard Boundaries

- If a checklist item fails due to missing structure/behavior, fix it in the
  task-001/002/003 assets, not by adding imperative styling back into C#.
