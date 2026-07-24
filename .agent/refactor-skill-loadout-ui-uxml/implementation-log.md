# Implementation Log

## Status
001–003 complete (agent). 004 pending (user editor steps).

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-author-uss-stylesheet.md | Complete | `SkillLoadoutUi.uss` created |
| 002-author-uxml-templates.md | Complete | Root + 5 templates created |
| 003-refactor-skillloadoutui-cs.md | Complete | `SkillLoadoutUi.cs` rewritten |
| 004-editor-wiring-and-parity-validation.md | Pending | User Inspector wiring + play test |

## Completed Tasks

### 001 — USS
- Created `Assets/Scripts/Ui/SkillLoadout/SkillLoadoutUi.uss`.
- Every inline style from the old `SkillLoadoutUi.cs` transcribed to a class
  (`.skill-bar`, `.node-column`, `.support-row`, `.cap-button`,
  `.support-button`, `.skill-button`, `.skill-button--triggered`,
  `.cooldown-label`, `.trigger-button`, `.picker-modal`, `.picker-choices`,
  `.picker-choice`).
- Added `.hidden { display: none; }` utility (parity mechanism for caps being
  absent when a node has no skill set).
- Colors converted: `Color(0.03,0.04,0.08,0.88)→rgba(8,10,20,0.88)`,
  `Color(0.04,0.05,0.10,0.96)→rgba(10,13,26,0.96)`.

### 002 — UXML
- Created under `Assets/Scripts/Ui/SkillLoadout/`: `SkillLoadoutUi.uxml` (root,
  links USS, holds `#bar`), `SkillNodeColumn.uxml`, `SkillSupportButton.uxml`,
  `SkillTriggerButton.uxml`, `SkillPicker.uxml`, `SkillPickerChoice.uxml`.
- Cap buttons `#decrease`/`#increase` are authored static in the node column
  with baked text (`−`/`+`) and `cap-button` class; C# only toggles enabled /
  `.hidden` and wires clicks.

### 003 — C#
- Rewrote `Assets/Scripts/SkillUi/SkillLoadoutUi.cs`.
- Removed all `style.*` assignments and all structural `new VisualElement`/
  `new Button`/`new Label` (grep confirms only the `new Label[]` cooldown array
  remains).
- Added serialized `VisualTreeAsset` fields: `nodeColumnTemplate`,
  `supportButtonTemplate`, `triggerButtonTemplate`, `pickerTemplate`,
  `pickerChoiceTemplate`.
- `OnEnable` now queries authored `#bar` (throws a clear setup error if the
  UIDocument source is unassigned) instead of building the tree.
- Node columns / trigger buttons / support buttons / picker / choices are cloned
  from templates via `Instantiate().Q(...)`; state bound via `text`,
  `SetEnabled`, `AddToClassList`.
- Added `[DefaultExecutionOrder(1000)]` so this component's `OnEnable` runs after
  `UIDocument` has populated `rootVisualElement`. (Awake→Start ordering with
  `ConfigureInitialRuntimeNodeCount` is unaffected — Awake fully precedes Start
  regardless of execution order.)

## Deviations

- **Fail-fast validation scope**: task 003 listed `catalog` among refs to
  validate-and-throw. Kept `catalog` **optional** (existing `if (catalog != null)`
  guards) to preserve current behavior where a scene without a catalog runs with
  empty pickers. Validated the truly-required refs instead: `UIDocument`,
  `SkillDriver`, and all five template assets. Driver/playerRoot keep their
  existing `FindAnyObjectByType` fallbacks.
- **Cap buttons static, not cloned**: task 002 offered "static in NodeColumn" vs
  "cloned" — chose static (baked `−`/`+` text + class in UXML), which removes the
  cap text/label assignment from C# entirely.

## Blockers
- (none)

## Validation Summary
- 001/002: static review — every USS class is referenced by a UXML `class`/C#
  query, and every element `name` C# queries exists in a template.
- 003: grep confirms no `\.style\.` and no structural element construction
  remain; API surface unchanged besides the added serialized fields.
- **Not run**: Unity compile + play-mode parity. The agent cannot run the Unity
  compiler or import assets headless in this environment. These are covered by
  task 004 (user, in editor). `.meta` files are intentionally not created — Unity
  generates them on import.
