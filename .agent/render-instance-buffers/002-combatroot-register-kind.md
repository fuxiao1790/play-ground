# 002 — Pass render kind at `Register` call sites

**File:** `Assets/Scripts/System/Common/CombatRoot.cs`

## Changes

Thread the new `CombatRenderKind` argument into every
`CombatRenderResourceRegistry.Register(...)` call:

- Projectile registrations (approx. lines 131, 569, 596) → `CombatRenderKind.Projectile`.
- AOE registration in `TryBuildAoeRenderResource` (approx. line 617) →
  `CombatRenderKind.Aoe`.

Confirm the exact call sites with `grep -n "\.Register(" CombatRoot.cs` before
editing (line numbers may drift).

## Acceptance criteria

- All `Register` calls pass a kind; project compiles past this file.
- Projectile render ids report `Kind == Projectile`; AOE ids report `Kind == Aoe`.

## Dependencies

Depends on 001 (`Register` signature + `CombatRenderKind`).
