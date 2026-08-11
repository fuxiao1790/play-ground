# 004 — Pause Menu Overlay UI

## Goal

Visible paused state with a Resume button, in the existing HUD panel, without
blocking the skill loadout UI.

## Design

The overlay is a template cloned into the existing `GameUI` root at runtime,
following the pattern the skill UI already uses — `Docs/ui.md:52-60` requires
the root UXML to stay small and dynamic pieces to be separate templates (I11).
Do not add the overlay to `SkillLoadoutUi.uxml`.

It is **not** an input blocker. World click-through is handled in task 003 by
the world surface's own picking mode, which is the documented gate (I5). Making
the overlay a full-screen picking element instead would also cover the skill
bar, and the loadout must stay usable while paused. So the overlay is laid out
to leave the skill bar reachable, and any of its own non-interactive decoration
uses `picking-mode: ignore`.

## Scope

New folder `Assets/Scripts/Ui/Hud/PauseMenu/`, mirroring
`Assets/Scripts/Ui/Hud/SkillLoadout/`:

- `PauseMenuUi.uxml` — stable structure: a container, a title label, a
  `#resume` button. Names queried from C#, values from USS.
- `PauseMenuUi.uss` — all layout, colour, spacing, sizing, opacity.
- `PauseMenuUi.cs` — namespace under `PlayGround.Ui`, assembly
  `PlayGround.Ui`.

Controller behaviour:

- Serialized `UIDocument`, `VisualTreeAsset` for the template,
  `StyleSheet`, and `PauseController`. Validate in `Awake`, throw on missing
  (I7) — match the message style in `ResourceBarUi.cs:38-41`.
- `OnEnable`: resolve `rootVisualElement`, clone the template once, cache
  `#resume`, register its clicked callback, subscribe to
  `PauseController.PausedChanged`, and set initial visibility from
  `PauseController.IsPaused`.
- `OnDisable`: unsubscribe, unregister the callback, remove the cloned element
  it owns (`Docs/ui.md:117-126`).
- Show/hide by toggling a USS class, not by rebuilding the tree, and not in
  `Update` — there is no per-frame work in this controller at all.
- The Resume button calls `pauseController.SetPaused(false)`. It stores no
  pause state of its own (I10).

## Acceptance Criteria

- Pausing shows the overlay; resuming hides it; the state survives repeated
  toggling with no duplicate cloned elements.
- The Resume button unpauses, and the overlay hides in response to
  `PausedChanged`, not by direct self-mutation on click.
- With the overlay visible, skill bar buttons are still clickable and the
  loadout picker still opens and resolves edits.
- With the overlay visible, clicking anywhere over the world produces no cast.
- No `Update` method on the controller.
- A missing serialized reference throws a clear setup error naming the field.

## Dependencies

001 (`PauseController`), and 003 for the world click-through guarantee the
fourth criterion depends on.

## Scope Estimate

Medium. Three new files, no changes to existing UI files.
