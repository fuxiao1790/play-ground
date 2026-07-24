# 001 — Author the USS Stylesheet

## Goal

Create one USS stylesheet that expresses every inline style currently set in
[SkillLoadoutUi.cs](../../Assets/Scripts/SkillUi/SkillLoadoutUi.cs) as reusable
classes and pseudo-states. After this task no styling values live in C#.

## Files To Create

- `Assets/Scripts/Ui/SkillLoadout/SkillLoadoutUi.uss`

## Files To Read

- `SkillLoadoutUi.cs` — source of truth for every current `style.*` value.

## Style Inventory To Capture

Map the current inline styles to classes (names illustrative — keep consistent
with task 002/003):

- `.skill-bar` — from `BuildBar`: absolute, `bottom: 24`, centered via
  `left: 50%` + `translate: -50% 0`, `flex-direction: row`,
  `align-items: flex-end`, background `rgba(0.03,0.04,0.08,0.88)`,
  padding `10 12`.
- `.node-column` — `flex-direction: column`, `align-items: center`.
- `.support-row` — `flex-direction: row`.
- `.cap-button` — `width: 28`, `height: 28` (the `−`/`+` buttons).
- `.support-button` — `width: 34`, `height: 28`, `font-size: 10`.
- `.skill-button` — `width: 112`, `height: 70`, `white-space: normal`.
- `.skill-button--triggered` — `opacity: 0.45` (modifier applied when a node has
  an incoming trigger).
- `.cooldown-label` — absolute, `right: 8`, `bottom: 4`, `color: white`.
- `.trigger-button` — `width: 112`, `height: 38`, `margin-bottom: 15`,
  `white-space: normal`, `font-size: 10`.
- `.picker-modal` — from `OpenPicker`: absolute, `top: 24`, centered
  (`left: 50%` + `translate: -50% 0`), `width: 560`, background
  `rgba(0.04,0.05,0.1,0.96)`, padding `12 16`.
- `.picker-choices` — `flex-direction: row`, `flex-wrap: wrap`.
- `.picker-choice` — `margin-right: 6`, `margin-bottom: 6`.

## Notes

- USS colors: convert the `Color(r,g,b,a)` 0–1 floats to `rgba()` (USS accepts
  `rgba(r,g,b,a)` with 0–255 channels, or `rgb(...)` — use whichever keeps the
  values readable; document the conversion inline).
- USS lengths use `px`/`%` units (e.g. `width: 28px`, `left: 50%`).
- `translate: -50% 0;` reproduces the centering transform.
- Do not invent new visual values; this task is a faithful transcription.

## Acceptance Criteria

- A single `.uss` file exists containing all classes above with values matching
  the current inline styles.
- No style value from `SkillLoadoutUi.cs` is missing from the stylesheet.
- File is valid USS (no C# leftovers, correct selector/property syntax).

## Validation

- Static review: diff the class list against the inline-style inventory above;
  every current `style.*` assignment has a class equivalent.
- Full visual confirmation happens in task 004 (play mode) after wiring.

## Hard Boundaries

- Do not modify `SkillLoadoutUi.cs` in this task.
- Do not add styles that have no counterpart in the current code.
