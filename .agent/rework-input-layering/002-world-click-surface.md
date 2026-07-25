# 002 — Add `GameplayInputSurface` (UI layer, bottom layer)

## Goal
The bottom input layer: a full-screen picking element behind `#bar` that turns
world pointer presses into a `FireHeld` bool, and registers itself as the
player's `IGameplayInputSource`.

## Change
New file `Assets/Scripts/SkillUi/GameplayInputSurface.cs`, namespace
`PlayGround.Skills` (matches `SkillLoadoutUi.cs`, which is UI-layer code in the
same asmdef but uses that namespace). Attributes mirror `SkillLoadoutUi`:

```csharp
[DefaultExecutionOrder(1000)]              // run after UIDocument populates root
[RequireComponent(typeof(UIDocument))]
public sealed class GameplayInputSurface : MonoBehaviour, IGameplayInputSource
```

Behavior:
- `[SerializeField] private PlayerRoot playerRoot;` — resolved in `Awake`, with a
  single `FindAnyObjectByType<PlayerRoot>()` fallback (same pattern as
  `SkillLoadoutUi.Awake`). Validate non-null; throw a clear setup error if
  missing.
- `Awake`: `document = GetComponent<UIDocument>();` resolve `playerRoot`.
- `OnEnable`:
  - `root = document.rootVisualElement;`
  - Create the surface element once (cache it), style it full-screen and
    transparent, `pickingMode = PickingMode.Position`, add USS class
    `world-input-surface`.
  - `root.Insert(0, surface);` — index 0 keeps it behind `#bar` (later siblings
    render on top and are picked first). Idempotent: if already parented, move to
    index 0 rather than duplicate.
  - Register pointer callbacks (see below).
  - `playerRoot.SetFireInput(this);`
- `OnDisable`:
  - `playerRoot?.ClearFireInput(this);`
  - `fireHeld = false;`
  - `surface?.RemoveFromHierarchy();`

Pointer capture / hold tracking (callbacks registered once on the surface):
- `PointerDownEvent`: `surface.CapturePointer(evt.pointerId); fireHeld = true;`
- `PointerUpEvent`: `if (surface.HasPointerCapture(evt.pointerId))
  surface.ReleasePointer(evt.pointerId); fireHeld = false;`
- `PointerCaptureOutEvent`: `fireHeld = false;` (capture lost, e.g. focus change).

`public bool FireHeld => fireHeld;`

USS class (add to `Assets/Scripts/Ui/SkillLoadout/SkillLoadoutUi.uss`, since the
same panel loads it):
```css
.world-input-surface {
    position: absolute;
    left: 0; top: 0; right: 0; bottom: 0;
    /* transparent; picking-mode set in C# to Position */
}
```

## Why capture the pointer
Once fire starts on the surface, capture keeps `PointerMove`/`Up` flowing to the
surface even if the cursor passes over `#bar` mid-hold. So holding fire and
grazing the UI does not stop firing, and release is detected reliably. Matches
hold-to-fire semantics of the old `attackAction.IsPressed()`.

## How this delivers "UI decides propagation"
- Skill buttons (`Position`) consume their own `PointerDown`; the surface, a
  sibling and not their ancestor, never receives it → no fire. Structural.
- Any element set to `picking-mode: ignore` lets the pick fall through to the
  surface behind it → click fires. This is the per-element propagation control.

## Rationale / Constraints
- Element created and callbacks registered once (allocation-light; no per-frame
  work). `FireHeld` is a bool.
- Cross-MonoBehaviour registration in `OnEnable`, own-ref validation in `Awake`
  (Awake/OnEnable boundary).
- Uses only `UnityEngine.UIElements`; no Input System dependency needed for the
  fire path (pointer events come from UI Toolkit).

## Acceptance Criteria
- Compiles in `PlayGround.SkillUi`.
- With the component on the `GameUI` object: clicking empty world sets
  `FireHeld` true for the hold; clicking a skill button does not.
- No per-frame allocations (callbacks registered in `OnEnable`, not `Update`).

## Dependencies
001 (interface), and 003 provides `PlayerRoot.SetFireInput/ClearFireInput`.
Author 002 and 003 together so both sides compile.

## Scope
Small–medium.
