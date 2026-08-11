# 003 — Gameplay Input Block Reasons

## Goal

Block gameplay input while paused, without breaking the skill picker's existing
suspension, and without leaving a held pointer armed across the pause.

## Why The Bool Has To Go

`gameplayInputSuspended` (`PlayerRoot.cs:65`) is a single bool toggled by
`SkillLoadoutUi.cs:313` on picker open and `:355` on picker close. The loadout
stays usable while paused, so both suspenders are live at once: opening and
closing the picker during pause would clear the suspension while the game is
still frozen. A second parallel `paused` bool would have the same collision one
level up. The type is wrong, so fix the type (D5).

## Scope

### New: `Assets/Scripts/Player/GameplayInputBlock.cs`

```csharp
[Flags]
public enum GameplayInputBlock
{
    None = 0,
    Paused = 1 << 0,
    SkillPicker = 1 << 1,
}
```

Namespace `PlayGround.Player`, assembly `PlayGround.GameLogic`. `PlayGround.Ui`
may reference it (I1).

### `Assets/Scripts/Player/PlayerRoot.cs`

- Replace the `gameplayInputSuspended` bool with a `GameplayInputBlock` mask
  field plus `bool GameplayInputBlocked => blocks != GameplayInputBlock.None;`.
- Replace `SetGameplayInputSuspended(bool)` (`:103-104`) with
  `SetGameplayInputBlocked(GameplayInputBlock reason, bool blocked)`, which
  sets or clears that reason's bit. Idempotent per reason.
- Update the three existing read sites to test `GameplayInputBlocked`:
  `ReadMoveInput` (`:436`), `ReadDashPressedThisFrame` (`:439`), and the
  `fireHeld` line (`:213`).
- Also gate `facing.AimAt(aimWorldPosition)` (`:212`) on `GameplayInputBlocked`.
  It is currently unguarded, so during pause the player sprite would keep
  rotating to follow the mouse — a visible tell that the game is not actually
  frozen. Aim snaps to the current pointer on resume, which is correct.
- Subscribe to `PauseController.PausedChanged` in `OnEnable` and unsubscribe in
  `OnDisable` (I7), mapping it to
  `SetGameplayInputBlocked(GameplayInputBlock.Paused, paused)`. Add a serialized
  `PauseController` field, validated in `Awake` (I7).

### `Assets/Scripts/Ui/Hud/SkillLoadout/SkillLoadoutUi.cs`

Migrate the two call sites to
`SetGameplayInputBlocked(GameplayInputBlock.SkillPicker, true/false)`. No
behaviour change on its own; it just stops the picker from clearing pause's
block.

### `Assets/Scripts/Ui/Hud/GameplayInputSurface.cs`

Add a serialized `PauseController`, subscribe in `OnEnable`, unsubscribe in
`OnDisable` (its existing `OnDisable` already clears `fireHeld`, so follow that
shape). On pause:

- set `surface.pickingMode = PickingMode.Ignore` so pointer events cannot reach
  the world surface at all, and restore `PickingMode.Position` on resume;
- clear `fireHeld` and release any captured pointer.

This is the documented gate — `Docs/ui.md:102-108` states that UI Toolkit
picking decides whether a click reaches the world surface (I5). Flipping the
surface's own picking mode keeps that decision inside the component that owns
it, and means the overlay does not have to win a sibling-order race to be safe.

Clearing `fireHeld` also fixes the resume case: a pointer held down across the
pause would otherwise leave `FireHeld` true and fire the instant the game
resumes.

## Interaction Check

While paused, `PlayerRoot.Update` still runs and still calls
`skillDriver.Tick(false, ...)`. That is intentional and required: `Tick` is the
only path that resolves queued loadout edits (I4), which task 002 keeps above
its dt guard. Fire is blocked twice over — by the mask here and by the dt guard
in 002 — and those cover different things. The mask stops input reaching the
driver; the dt guard stops mobs, which have no input mask at all.

## Acceptance Criteria

- Paused: mouse held on the world surface produces no cast, and `FireHeld` reads
  false.
- Paused: WASD and dash produce no movement input; the player sprite does not
  rotate with the mouse.
- Open picker → pause → close picker: input stays blocked. Unpause: input works.
- Pause → unpause with the mouse button held down the whole time: no cast fires
  on the resume frame.
- Picker-only behaviour at `timeScale = 1` is unchanged from today.

## Dependencies

001 (`PauseController` and its `PausedChanged` event).

## Scope Estimate

Medium. One new enum, edits in four files, all mechanical except the aim gate
(a deliberate behaviour change) and the surface picking flip.
