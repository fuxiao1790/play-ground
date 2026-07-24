# 002 — Author UXML Root + Templates

## Goal

Author the declarative structure: a root UXML for the `UIDocument` plus small
templates cloned at runtime for the dynamic parts. All structure and class
assignment lives in markup; C# (task 003) only queries names and clones.

## Files To Create

Under `Assets/Scripts/Ui/SkillLoadout/`:

- `SkillLoadoutUi.uxml` — **root**, assigned to `UIDocument.sourceAsset`.
  - `<Style src="SkillLoadoutUi.uss" />` (relative reference).
  - One container `#bar` with class `skill-bar`. Left empty — C# fills it with
    cloned node columns and trigger buttons so the count stays driven by
    `initialNodeCount`.
- `SkillNodeColumn.uxml` — one node column, class `node-column`, containing:
  - `#supports` container, class `support-row` (empty; C# clones support
    buttons and the `−`/`+` cap buttons into it, or the cap buttons are named
    `#decrease`/`#increase` here if kept static — see Notes).
  - `#skill` `Button`, class `skill-button`, containing a `#cooldown` `Label`,
    class `cooldown-label`.
- `SkillSupportButton.uxml` — one `Button`, class `support-button` (cloned per
  support slot).
- `SkillTriggerButton.uxml` — one `Button`, class `trigger-button` (cloned
  between adjacent nodes).
- `SkillPicker.uxml` — the modal, class `picker-modal`, containing:
  - `#title` `Label`.
  - `#choices` container, class `picker-choices`.
  - `#cancel` `Button`.
- `SkillPickerChoice.uxml` — one `Button`, class `picker-choice` (cloned per
  catalog choice + the `Clear` entry).

## Notes

- Element `name` attributes are the contract task 003 queries against
  (`root.Q<Button>("skill")`, etc.). Keep names stable and documented here.
- **Cap buttons (`−`/`+`)**: they are a fixed pair per column, so author them
  statically inside `SkillNodeColumn.uxml` as `#decrease`/`#increase` with class
  `cap-button`, and let C# only toggle their enabled state and register
  callbacks. This avoids extra clone calls. (If the current visual order
  — decrease, support buttons, increase — is easier to preserve by having C#
  place them around the cloned support buttons, document that ordering choice.)
- **Template consolidation option**: `SkillSupportButton`, `SkillTriggerButton`,
  and `SkillPickerChoice` are each a single labeled `Button` differing only by
  class. Collapsing them into one `SkillButton.uxml` reused with a class set in
  C# is acceptable; if done, record the class-application contract here.
- No inline `style` attributes in the UXML — all visuals come from USS classes
  authored in task 001.

## Acceptance Criteria

- Root UXML exists, links the USS, and exposes a named `#bar` container.
- Each dynamic unit has a template with the named parts listed above and the
  correct USS classes applied.
- The template tree shape mirrors the current runtime tree one-to-one (column →
  supports row + skill button with cooldown label; trigger buttons between
  nodes; modal with title + choices + cancel).
- No inline styles; all class names exist in the task-001 USS.

## Dependencies

- Depends on **001** for the USS class names referenced by `<Style>` and the
  `class` attributes.

## Validation

- Static review: every `name` and `class` used here is (a) consumed by task 003
  and (b) defined in the USS. Cross-check the three lists.
- Import validation (user, in editor): assets import without UXML/USS errors in
  the Console. Note this as a user editor step if the agent cannot import.

## Hard Boundaries

- Do not modify `SkillLoadoutUi.cs` in this task.
- Do not wire assets onto the `UIDocument` or serialized fields here — that is
  the user Inspector step in task 004.
- Do not introduce structure the current UI does not have.
