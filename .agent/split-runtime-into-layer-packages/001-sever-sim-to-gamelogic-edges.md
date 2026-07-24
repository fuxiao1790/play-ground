# 001 — Delete dead sim→game-logic code in CombatRoot

**Depends on:** none. **Scope:** small. Still inside the single `PlayGround.Runtime`
assembly, so it compiles independently and can land before any package move.

## Objective
Remove the sim's two dead references into game logic so `Assets/Scripts/System/`
has no game-logic dependency except `AoeConfig` (handled in 002).

## Changes
`Assets/Scripts/System/Core/CombatRoot.cs`:
- Delete `using PlayGround.Skills;` (line 16) — no `Skills` type is used in the file.
- Delete `public int RegisterConfig(AoeConfig config)` (lines ~320–337) and the
  `private readonly Dictionary<AoeConfig, int> configTypeIds` field (line ~73).
  Confirmed **no callers** across `Assets/` or `Packages/`; the live path is
  `SkillDriver` → `RegisterType(AoeTypeDefinition)`.

## Acceptance criteria
- `CombatRoot.cs` no longer names any `PlayGround.Skills` type or `AoeConfig`.
- `grep -rn "RegisterConfig\|configTypeIds" Assets Packages` returns nothing.
- Project still compiles (single assembly unchanged otherwise).

## Verification (user, Unity)
- Unity recompiles with no errors; existing AOE registration still works
  (AOEs spawn in `BenchmarkLarge`).
