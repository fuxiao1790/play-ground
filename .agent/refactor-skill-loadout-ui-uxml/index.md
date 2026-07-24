# Refactor SkillLoadoutUi to UXML + USS

## High-Level Summary

Replace the imperative `VisualElement` construction in
[SkillLoadoutUi.cs](../../Assets/Scripts/SkillUi/SkillLoadoutUi.cs) with a
declarative UI Toolkit setup:

- **Structure** moves to **UXML** assets.
- **Styling** (every current `element.style.*` assignment) moves to a **USS**
  stylesheet linked from the root UXML.
- The root UXML is assigned to the `UIDocument` component (chosen approach:
  *UIDocument source + USS on document*), so the panel tree is authored, not
  built in code.
- `SkillLoadoutUi.cs` keeps only what markup cannot express: **data binding**
  (labels, enabled states, opacity), **dynamic list cloning** (variable support
  buttons and picker choices), and **event wiring** (clicks, driver events,
  cooldown ticks, input gate).

This is a **layer-local refactor of the presentation only**. No change to
`SkillDriver`, `SkillUiCatalog`, `SkillSet`, or any data contract.

## Why This Change

The current file mixes three concerns in C#: element tree construction, inline
styling, and data/event logic. The first two are exactly what UXML/USS exist to
own. Moving them out removes ~120 lines of `new VisualElement` / `style.width =`
noise and leaves the C# doing one job: bind driver state to named elements.

## Chosen Approach (user decision)

**UIDocument source + USS on document.**

- Root UXML (`SkillLoadoutUi.uxml`) is assigned to `UIDocument.sourceAsset` in
  the Inspector. It contains the bar container and a `<Style>` reference to the
  USS. UI Toolkit clones it into `rootVisualElement` automatically on enable.
- The genuinely dynamic parts — node columns (count = `initialNodeCount`),
  variable support buttons per node, trigger buttons between nodes, and the
  picker modal with a variable choice list — are cloned at runtime from small
  UXML **templates** referenced by serialized `VisualTreeAsset` fields on
  `SkillLoadoutUi`. Cloning a template needs a `VisualTreeAsset` handle, and
  serialized fields are the type-safe, no-magic-string way to supply it. This is
  the "most Inspector-heavy" tradeoff the chosen option accepts.

## Constraints & Invariants The Change Must Respect

- **Presentation-only boundary** — the change stays inside the
  `PlayGround.SkillUi` assembly
  ([folder-structure.md](../../Docs/folder-structure.md) §Current Folders). It
  must not add fields to or change the API of `SkillDriver` or `SkillUiCatalog`.
  *Source: folder-structure.md, SkillDriver.cs public surface.*
- **Root Component Rule** — `SkillLoadoutUi` remains the single root
  MonoBehaviour owning the `UIDocument` reference, serialized refs, event
  wiring, and update order. Styling/structure logic is *removed*, not relocated
  into another gameplay class. *Source:
  [coding-standards.md](../../Docs/coding-standards.md) §Root Component Rule.*
- **Awake vs OnEnable** — cross-MonoBehaviour work (subscribing to
  `skillDriver` events) stays in `OnEnable`; self-owned setup
  (`GetComponent<UIDocument>`, array alloc) stays in `Awake`. Current code is
  already correct here and must not regress. *Source: coding-standards.md
  §Awake vs OnEnable Boundary.*
- **Fail-fast validation** — every required serialized reference (UIDocument,
  each template `VisualTreeAsset`, catalog, driver) is validated once at setup
  and throws a clear setup error if missing, instead of null-checking in the
  build path. *Source: coding-standards.md §Fail Fast Validation.*
- **Behavior parity** — the refactor must preserve, exactly:
  - bar layout: node columns with a supports row above a skill button, trigger
    buttons between adjacent nodes;
  - `−`/`+` support-cap buttons, enabled only within `[0, MaxSupportCount]`;
  - support/skill/trigger buttons open the picker for the right target;
  - triggered node's skill button shown at `opacity 0.45`;
  - per-node cooldown label text driven each `Update` by
    `GetCooldownProgressForNode`;
  - picker modal open/close, `Clear` + `Cancel` choices, and the
    `playerRoot.SetGameplayInputGate` gate on open/close;
  - full refresh on `LoadoutChanged`; picker close on rejected `EditResolved`.
- **Allocation** — UI rebuild happens only on edit/loadout change (not a hot
  path), so template cloning there is acceptable. `Update` must not get *worse*:
  keep it to the existing per-node cooldown-label text update. *Source:
  coding-standards.md §Allocation Rule.* (The pre-existing per-frame string
  interpolation in `Update` is out of scope; do not expand it.)
- **Editor steps are user steps** — `.uxml`/`.uss` are text source and are
  authored as part of this work. Assigning the root UXML to
  `UIDocument.sourceAsset`, setting `PanelSettings`, and dragging template
  assets into the serialized fields are **Inspector wiring = user editor
  steps**; they are written as instructions, never hand-edited into scene/prefab
  YAML or `.meta` files. *Source: memory `editor-steps-are-user-steps`.*

## Mechanisms Reused vs. Introduced

- **Reused**: UI Toolkit `UIDocument`/`PanelSettings` already drives this UI;
  the driver event model (`LoadoutChanged`, `EditResolved`, `Revision`,
  `TryQueueEdit`) is unchanged; serialized-reference dependency injection is the
  standard here (coding-standards §Root Component). The C# label/enabled/opacity
  reads reuse the exact same `SkillDriver`/`SkillSet`/`catalog` accessors the
  current code already calls.
- **Introduced**: UXML + USS assets (new file type for this project — no
  existing `.uxml`/`.uss`), and a small set of serialized `VisualTreeAsset`
  template fields. Justification: these are the canonical UI Toolkit mechanism
  for the exact separation being requested; there is no existing markup path to
  conform to.

## Design Validation Against Invariants

- *Presentation-only*: task set touches only `Assets/Scripts/SkillUi/*` and new
  UXML/USS assets. Confirmed by the driver's public surface being sufficient —
  no new accessor is required.
- *Root Component*: `SkillLoadoutUi` keeps ownership; the removed code is pure
  view construction, not gameplay logic, so nothing needs a new home.
- *Fail-fast*: validation list grows by the new template fields; handled in the
  same `Awake`/setup validation the standard prescribes.
- *Behavior parity*: each parity item maps to a concrete bind/clone step in task
  003; the UXML mirrors the current tree shape one-to-one so no visual/behavior
  drift is expected. Validated by the play-mode parity checklist in task 004.
- *Allocation*: cloning is confined to edit-time refresh; `Update` path
  unchanged.

## Minimal/Additive vs. Refactor Comparison

- **Minimal/additive approach** (keep imperative builder, wrap it):
  - resulting data flow: unchanged, but styling/structure still hardcoded in C#.
  - new concepts/types introduced: none, or a thin helper that still emits
    `VisualElement`s.
  - copies/translations added: none.
  - long-term cost: the exact problem the user raised persists — UI definition
    stays in code; every visual tweak is a recompile; no designer/UI-Builder
    path. Does not satisfy the request.
- **Refactor approach** (this plan — declarative UXML/USS):
  - resulting data flow: `UIDocument` clones authored UXML; C# binds driver
    state to named elements and clones templates for variable lists.
  - existing concepts/types changed or removed: `BuildBar`, `AddNodeColumn`,
    `AddTriggerButton`, and the inline-style bodies of `OpenPicker`/`AddChoice`
    are removed; replaced by query + clone + bind.
  - copies/translations removed or avoided: eliminates the hand-built element
    tree and every inline `style.*` assignment.
  - long-term benefit: structure and look are editable in UXML/USS (and UI
    Builder) without recompiling; C# shrinks to binding logic; matches the
    directly requested model.
- **Decision**: **choose refactor.** It is the user's explicit request, keeps a
  single source of truth for layout (the UXML) instead of layout-in-code, and
  removes duplication rather than hiding it.

## Default Decision Rule Check

No second data representation of a domain concept is introduced — the driver
state stays the single source of truth; UXML/USS describe only presentation.
Passes.

## Task List

1. [001-author-uss-stylesheet.md](001-author-uss-stylesheet.md) — Author the USS
   capturing every current inline style as reusable classes.
2. [002-author-uxml-templates.md](002-author-uxml-templates.md) — Author the
   root UXML and the runtime-cloned templates (node column, support button,
   trigger button, picker, picker choice); link the USS.
3. [003-refactor-skillloadoutui-cs.md](003-refactor-skillloadoutui-cs.md) —
   Rewrite `SkillLoadoutUi.cs` to query named elements, clone templates, bind
   state, and wire events; delete the imperative builders and inline styling.
4. [004-editor-wiring-and-parity-validation.md](004-editor-wiring-and-parity-validation.md)
   — **User editor steps**: wire UIDocument source, PanelSettings, and template
   fields in the Inspector, then run the play-mode parity checklist.

## Task Order & Dependencies

- 001 → 002 (UXML `<Style>` references the USS class names).
- 002 → 003 (C# queries element names and clones templates defined in 002).
- 003 → 004 (wiring/validation needs the compiled component with its serialized
  fields).

## Open Questions / Considerations

- **Asset location** (decided): `.uxml`/`.uss` live under
  `Assets/Scripts/Ui/SkillLoadout/`. This is a new `Assets/Scripts/Ui/` asset
  root (no asmdef needed — UXML/USS are not compiled; the C# stays in
  `Assets/Scripts/SkillUi/` under `PlayGround.SkillUi`).
- **Template consolidation**: support button, trigger button, and picker choice
  are all "one labeled button". They can be three tiny templates or one generic
  button template reused with different USS classes. Task 002 authors them
  explicitly for clarity; collapsing to one generic button template is an
  acceptable simplification if preferred.
- **Node-column count**: node columns are cloned in C# `initialNodeCount` times
  (not hardcoded in UXML) so the existing configurable count and
  `ConfigureInitialRuntimeNodeCount` contract are preserved.
- **Refresh strategy on `LoadoutChanged`**: the current code does
  `root.Clear(); BuildBar()`. With the authored root on the UIDocument, task 003
  should refresh by rebuilding only the cloned children under `#bar`, not by
  clearing the authored root. Parity target is identical visible result.
