# 003 — Rewrite SkillLoadoutUi.cs (Query + Clone + Bind)

## Goal

Replace the imperative element construction and inline styling in
[SkillLoadoutUi.cs](../../Assets/Scripts/SkillUi/SkillLoadoutUi.cs) with:
query the authored tree, clone the task-002 templates for dynamic lists, bind
driver state, and wire events. No `style.*` assignment and no ad-hoc
`new VisualElement()`/`new Button()` for structure remain.

## Files To Modify

- `Assets/Scripts/SkillUi/SkillLoadoutUi.cs`

## Files To Read

- `SkillLoadoutUi.cs` (current) — behavior to preserve.
- `SkillDriver.cs` — bound API: `RuntimeNodes`, `Revision`, `TryQueueEdit`,
  `GetCooldownProgressForNode`, `ConfigureInitialRuntimeNodeCount`,
  `LoadoutChanged`, `EditResolved`.
- Task 002 templates — element `name`s and classes to query/clone.

## Serialized Fields To Add

Add `[SerializeField] private VisualTreeAsset` fields for each runtime-cloned
template from task 002 (e.g. `nodeColumnTemplate`, `supportButtonTemplate`,
`triggerButtonTemplate`, `pickerTemplate`, `pickerChoiceTemplate` — collapse per
the 002 consolidation choice). Do **not** add a field for the root UXML; it
comes from the `UIDocument` source.

## Behavior To Preserve (exact parity)

- Node columns cloned `initialNodeCount` times into `#bar`; trigger buttons
  cloned between adjacent nodes.
- Support row: `−` cap button (enabled when `SupportSlotCount > 0`), then one
  support button per `SupportSlotCount` (label = `SupportLabel`), then `+` cap
  button (enabled when `SupportSlotCount < MaxSupportCount`). Cap buttons only
  shown when the node has a `SkillSet` (matches current `skillSet != null`
  guards).
- Skill button label = `SkillLabel`; `skill-button--triggered` class added when
  the previous node has a `TriggerToNext` (replaces `style.opacity = 0.45`).
- `#cooldown` label under each skill button, refreshed in `Update` from
  `GetCooldownProgressForNode` using the current formatting.
- Clicks open the picker for the right `PickerTarget`; cap buttons call
  `QueueCapEdit`.
- `OpenPicker` clones `SkillPicker`, fills `#title` = `Select {Kind}`, adds the
  `Clear` choice then one choice per catalog entry (Skills/Supports/Triggers by
  kind), wires `#cancel` to `ClosePicker`, and calls
  `playerRoot.SetGameplayInputGate(true, true)`.
- `ClosePicker` removes the modal and calls `SetGameplayInputGate(false,false)`.
- `OnLoadoutChanged` refreshes the bar; `OnEditResolved` closes the picker when
  `!result.Accepted`.

## Behavior To Change

- Do not `root.Clear()` the authored root. On enable and on `LoadoutChanged`,
  clear and repopulate only the cloned children under `#bar` (and close any open
  modal). The authored root/`#bar`/`<Style>` stay intact.
- Replace `SkillLabel`/`SupportLabel`/`TriggerDisplayName` *consumers* to set
  cloned elements' text; the label-string helpers themselves can stay as-is.
- Apply classes via `AddToClassList`/`EnableInClassList` instead of `style.*`.

## Setup / Validation

- In `Awake`/setup, validate required refs (UIDocument, catalog, driver, every
  template `VisualTreeAsset`) and throw a clear setup error if missing
  (coding-standards §Fail Fast). Keep `FindAnyObjectByType` fallbacks only where
  they already exist.
- Keep event subscription in `OnEnable` and unsubscription in `OnDisable`
  (coding-standards §Awake vs OnEnable). Keep the `cooldownLabels` array indexed
  by node.

## Acceptance Criteria

- No `style.*` assignments and no structural `new VisualElement()`/`new Button()`
  remain in the file (leaf elements come from cloned templates).
- `BuildBar`, `AddNodeColumn`, `AddTriggerButton` bodies are replaced by
  query/clone/bind logic; `OpenPicker`/`AddChoice`/`AddClear` construct via the
  picker templates.
- The file compiles within `PlayGround.SkillUi`.
- Every parity item above is implemented.

## Dependencies

- **001** and **002** complete (USS classes and template names exist).

## Validation

- Compile check: `PlayGround.SkillUi` builds with no errors.
- Static review against the parity list — each item maps to code.
- Grep the file for `\.style\.` and `new VisualElement`/`new Button` used for
  structure; expect none (cloned templates instead).
- Runtime parity is validated in task 004.

## Hard Boundaries

- Do not change `SkillDriver`, `SkillUiCatalog`, `SkillSet`, or any data type.
- Do not add new public API to `SkillLoadoutUi` beyond the serialized template
  fields.
- Do not alter the picker/edit command flow or the input-gate semantics.
