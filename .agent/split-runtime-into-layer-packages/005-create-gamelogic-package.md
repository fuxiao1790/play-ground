# 005 — Rename `PlayGround.Runtime.asmdef` → `PlayGround.GameLogic`

**Depends on:** 004 (Sim asmdef exists, so the root assembly no longer owns
`System/**`). **Scope:** small.

## Objective
Turn the leftover root assembly into the game-logic layer. After task 004 carved out
`System/`, the root asmdef at `Assets/Scripts/` already covers exactly the
game-logic folders (everything not claimed by a nested asmdef).

## Changes
- Rename `Assets/Scripts/PlayGround.Runtime.asmdef` →
  `Assets/Scripts/PlayGround.GameLogic.asmdef` (keep the `.meta`/GUID so GUID-based
  references survive), and set its `name` to `PlayGround.GameLogic`.
- Set its `references` to `PlayGround.Sim` + the Unity packages it names
  (`Unity.Entities`, `Unity.InputSystem`, etc.). Keep minimal.
- Update any asmdef that references the old assembly **by string name**
  `"PlayGround.Runtime"` (GUID-based references need no change) — handled fully in 006.

## Acceptance criteria
- Root asmdef is named `PlayGround.GameLogic` and references `PlayGround.Sim`.
- Game-logic folders (`Skills/`, `Mob/`, … `Common/Stats,StatusEffects`) compile in
  this assembly; `System/`, `Debugging/`, `SkillUi/` are excluded (own asmdefs).

## Verification (user, Unity)
- `PlayGround.GameLogic` compiles against `PlayGround.Sim`.
