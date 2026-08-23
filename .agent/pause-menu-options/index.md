# Pause Menu Options

## Summary

Center the existing pause menu, add an Options button and empty modal options
popup, and route `UI/Cancel` through one universal active-popup stack so Escape
closes the frontmost popup before it can change pause state.

## Architectural Decisions

- `PauseController` remains the only reader of `UI/Cancel`. It offers a generic
  registration contract for low-count modal UI handlers, asks the most recently
  opened popup first, and toggles pause only when no handler consumes request.
- Popup owners register only while their popup is active. `PauseMenuUi` owns
  options-popup presentation state because the popup is
  authored UI, not gameplay state. It registers during `OnEnable` and unregisters
  during `OnDisable`.
- Stable menu and popup hierarchy remains authored in `PauseMenuUi.uxml`; layout
  and visibility remain class-driven in `PauseMenuUi.uss`; C# only wires intent
  and toggles the visibility class.
- Popup remains under existing `#pause-menu-layer`, above the pause menu panel,
  and uses position picking while visible to block controls underneath.

## Constraints And Invariants

- UI may depend on Game Logic, but Game Logic must not reference UI types.
  Source: `Docs/ui.md` (Ownership) and
  `Docs/architecture/layer-rules.md` (Package Boundary).
- UI owns presentation/input routing, while pause runtime state remains owned by
  `PauseController`. Source: `Docs/ui.md` (Ownership, State And Commands).
- `#pause-menu-layer` stays the frontmost authored HUD layer and actual controls
  opt into picking. Source: `Docs/ui.md` (Input Layering) and
  `Assets/Scripts/Ui/Hud/SkillLoadoutUi.uxml`.
- Stable visual hierarchy belongs in UXML, visual values in USS, and C# owns
  event wiring/small state updates. Source: `Docs/ui.md` (UXML, USS, And C#
  Rules).
- Cross-component setup must not occur in `Awake`, and cleanup must not depend on
  `OnDestroy`. Popup-handler registration follows real popup open/close state,
  with `OnDisable` as cleanup fallback. Source: `Docs/coding-standards.md`
  (Awake vs OnEnable Boundary, OnDestroy Boundary).
- UI work stays fixed-size and cannot scale with projectile/AOE/entity count.
  Source: `Docs/ui.md` (Lifecycle And Performance).
- No ECS data, jobs, structural changes, or high-count simulation paths are
  touched. Source: `Docs/reference/simulation/ecs-notes.md` and inspected code.
- Agents do not run Unity tests; user exports XML under `Logs/`, and agent must
  review that XML before claiming pass. Source: `Docs/project-overview.md` and
  `Docs/testing.md`.

## Mechanisms Reused Vs Introduced

Reused:

- Existing `UI/Cancel` action and `PauseController` pause-state authority.
- Existing `#pause-menu-layer` and `#pause-background` authored layer stack.
- Existing pause-menu UXML-template clone, USS visibility class, and cached
  button callback pattern.
- Existing UI lifecycle registration and fail-fast setup validation.

Introduced:

- `IUiCancelHandler`, a narrow Game Logic contract that contains no concrete UI
  type. It permits every modal presentation owner to consume cancel while
  preserving one input source and one pause-state owner.
- One authored `#options-popup` element plus its hidden class. No options data
  model or settings state is introduced while popup is empty.

## Design Validation

- Dependency direction holds: `PauseMenuUi` implements a Game Logic interface;
  `PauseController` never imports or resolves `PlayGround.Ui`.
- Ownership holds: popup visibility exists only in the visual element class
  state; pause truth remains `PauseController.IsPaused`.
- Input routing holds: Escape reaches `PauseController` once, newest active
  popup is offered request first, and a consumed request does not toggle pause.
- Lifecycle holds: button/event wiring is paired in `OnEnable`/`OnDisable`;
  popup-handler registration is paired at open/close and also cleared by
  `OnDisable`; no teardown work is placed in `OnDestroy`.
- Layering holds: popup is later sibling of menu panel within frontmost pause
  root and position-picks across the screen while visible.
- Performance holds: handler scan and UI state are fixed, low-count work and do
  not touch any ECS loop.

## Minimal/Additive Vs Refactor Comparison

### Minimal/Additive Approach

- Resulting data flow: `PauseMenuUi` reads `UI/Cancel` separately from
  `PauseController`, or repairs pause state after `PauseController` toggles it.
- New concepts/types introduced: second input action reader or suppression flag.
- Copies/translations added: duplicate per-frame action state and popup-to-pause
  synchronization.
- Long-term cost: input order dependence, transient unpause/audio changes, and
  every future modal needing another parallel workaround.

### Refactor Approach

- Resulting data flow: `UI/Cancel` -> `PauseController` -> newest registered
  cancel handler -> fallback pause toggle.
- Existing concepts/types changed or removed: direct unconditional toggle path
  becomes consume-or-toggle routing; no existing pause API is removed.
- Copies/translations removed or avoided: no second input reader, no duplicate
  pause state, no frame-order repair.
- Long-term benefit: one deterministic source for cancel and reusable modal
  priority without Game Logic knowing UI implementations.

### Decision

- Choose refactor.
- Reason: it keeps one input and pause-state data path. Additive alternatives
  create duplicated input ownership or visible time/audio state churn.

Default decision rule: when two representations or data paths describe the
same domain concept, refactor toward one source of truth unless compatibility
or migration requires otherwise.

## Tasks

1. [001-centralize-cancel-routing.md](001-centralize-cancel-routing.md)
2. [002-author-options-popup.md](002-author-options-popup.md)
3. [003-tests-and-docs.md](003-tests-and-docs.md)
4. [004-universal-popup-cancel.md](004-universal-popup-cancel.md)

## Open Questions And Dependencies

- None. "Empty" means popup has a visible blank panel and no settings controls;
  Escape is its close action.
- Existing scene references remain valid because current template and controller
  assets are modified in place; no Inspector or scene YAML changes are needed.
