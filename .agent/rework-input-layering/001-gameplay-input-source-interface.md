# 001 — Add `IGameplayInputSource` (Game Logic)

## Goal
Define the seam that lets the UI layer supply the player's fire state without
Game Logic referencing the UI layer.

## Change
New file `Assets/Scripts/Player/IGameplayInputSource.cs`, namespace
`PlayGround.Player`:

```csharp
namespace PlayGround.Player
{
    // Supplies per-frame gameplay pointer intent to PlayerRoot.
    // Implemented by the UI layer (world click surface) and registered into
    // PlayerRoot; PlayerRoot never reads the raw pointer button itself.
    public interface IGameplayInputSource
    {
        // True while the player is holding fire over the world (not over UI).
        bool FireHeld { get; }
    }
}
```

## Rationale / Constraints
- Lives in `PlayGround.GameLogic` (the `Assets/Scripts/` root asmdef) so the
  lower layer owns the contract; the UI layer implements it. Respects the
  one-way reference rule (layer-rules.md "Package Boundary").
- Kept to a single bool now (mouse-only fire). Aim position is intentionally
  *not* part of this interface — `PlayerRoot` already derives aim from the
  `UI/Point` action, which is independent of the fire button.

## Acceptance Criteria
- File compiles in `PlayGround.GameLogic`.
- No reference to any UI type.

## Dependencies
None.

## Scope
Trivial.
