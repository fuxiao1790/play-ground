# 005 — Editor Wiring (User Steps)

## Goal

Scene and inspector setup for the pause feature in
`Assets/Scenes/BenchmarkLarge.unity`.

Editor wiring is a user operation. Nothing in this task may be done by editing
scene, prefab, asset, or `.meta` YAML by hand (`Docs/ui.md:152-153`, I6). These
are instructions to follow in the editor.

## Steps

1. **Verify the Esc binding.** Open `Assets/InputSystem_Actions.inputactions`,
   select the `UI` map, and confirm the `Cancel` action has a keyboard Escape
   binding. It should already, from the Unity input template. No new action is
   needed — task 001 reads `UI/Cancel` directly.

2. **Add `PauseController`.** On the `GameRoot` object in
   `BenchmarkLarge.unity`, add the `PauseController` component and assign
   `InputSystem_Actions` to its `Input Actions` field.

3. **Wire `PlayerRoot`.** Select the player object and assign the `GameRoot`
   object's `PauseController` to the new `Pause Controller` field.

4. **Wire `GameplayInputSurface`.** On the `GameUI` object, assign the same
   `PauseController` to its new `Pause Controller` field.

5. **Add `PauseMenuUi`.** On the same `GameUI` object, add the `PauseMenuUi`
   component and assign: its `UIDocument`, `PauseMenuUi.uxml` as the template
   asset, `PauseMenuUi.uss` as the stylesheet, and the `PauseController`.

6. **Verify the UI event setup.** Runtime pointer delivery needs an
   `EventSystem` with `InputSystemUIInputModule` in the scene
   (`Docs/ui.md:114-115`). It is already required by the existing HUD, so this
   is a confirmation step.

7. **Playtest.** Enter play mode and check, in order:
   - Esc freezes everything — projectiles, AOEs, mobs, player, animation, VFX
     particles mid-flight.
   - The overlay appears and Resume dismisses it.
   - Clicking over the world while paused fires nothing, including a click that
     lands where the Resume button is not.
   - Holding the mouse button through a pause/resume cycle does not fire on the
     resume frame.
   - The skill bar and loadout picker still work while paused, and an edit made
     while paused takes effect.
   - Esc again resumes and the game continues from exactly where it froze.

## Acceptance Criteria

- Every step above completes with no missing-reference exceptions on entering
  play mode.
- Each playtest check passes.
- No scene or asset file was edited outside the Unity editor.

## Dependencies

001, 003, 004.

## Scope Estimate

Small, but blocking: nothing downstream can be verified in a real scene until
this is done.
