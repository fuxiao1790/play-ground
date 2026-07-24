# 009 — Add `PlayGround.Debugging.asmdef` at `Debugging/` (exempt leaf)

**Depends on:** 005 (GameLogic), 004 (Sim). Task 008 (cycle break) is already done,
so the sim no longer references `PerformanceText`. **Scope:** small — one asmdef.

## Objective
Give the debug overlays their own **exempt** assembly under `Assets/Scripts/`: it may
reference sim + game-logic + UI freely; the only requirement is that no core
assembly references it (already true post-008).

## Changes
- Create `Assets/Scripts/Debugging/PlayGround.Debugging.asmdef`:
  - `name: PlayGround.Debugging`.
  - `references`: `PlayGround.Sim` + `PlayGround.GameLogic` (+ `PlayGround.SkillUi`
    only if debug code names UI types) + Unity packages used (`Unity.Entities`,
    `UnityEngine.UI`).
  - This nested asmdef removes `Debugging/**` from the root GameLogic assembly.
- No file moves — `Debugging/` already sits under `Assets/Scripts/`. `PerformanceText`
  stays in the global namespace (fine).

## Acceptance criteria
- No asmdef in `PlayGround.Sim` / `PlayGround.GameLogic` / `PlayGround.SkillUi`
  references `PlayGround.Debugging` (true leaf).
- Deleting the Debugging asmdef would still leave the three core assemblies compiling.

## Verification (user, Unity)
- Overlay + VFX tester still function in `BenchmarkLarge`.
