# 001 — Move `AudioManager` from GameLogic into Sim

## Why

`PlayGround.Sim.asmdef` references no PlayGround assembly
(`Docs/architecture/layer-rules.md` §*Package Boundary*: `Ui -> GameLogic -> Sim`,
one-way). The sound bridge is an ECS presentation system and must live in Sim,
where layer-rules.md places bridges. A bridge in Sim cannot call an
`AudioManager` in GameLogic.

`CombatVfxRoot` is the precedent: a `MonoBehaviour` inside Sim at
`Assets/Scripts/System/Vfx/CombatVfxRoot.cs`, referenced downward by
`SkillDriver`.

## Change

Pure move. **No behavior change, no logic edit.**

- `Assets/Scripts/Audio/AudioManager.cs` → `Assets/Scripts/System/Audio/AudioManager.cs`
- Namespace `PlayGround.Audio` → `PlayGround.System.Combat.Audio`, matching the
  sibling `PlayGround.System.Combat.Vfx`.
- Delete the now-empty `Assets/Scripts/Audio/` folder and its `.meta`.
- `SkillDriver.cs:3` — update `using PlayGround.Audio;` to the new namespace.

Move the `.meta` file with the script so the GUID is preserved and existing
scene references survive.

## Verify

`AudioManager.cs` imports only `System.Collections.Generic` and `UnityEngine` —
no GameLogic dependency exists, so the move is mechanical. Confirm this still
holds before moving; if any GameLogic reference has appeared, stop and re-plan
rather than adding a reference to `PlayGround.Sim.asmdef`.

## Acceptance Criteria

- `AudioManager` compiles inside `PlayGround.Sim`.
- `PlayGround.Sim.asmdef` is **unchanged** — no PlayGround reference added.
  This is the single most important check in the task.
- `SkillDriver` still compiles (GameLogic → Sim is an allowed direction).
- The `AudioManager` GameObject in `Assets/Scenes/BenchmarkLarge.unity` still
  resolves its script (GUID preserved via the `.meta` move).
- No other file changes.

## Dependencies

None. Must land before every other task.

## Scope

Small — file move, one namespace line, one `using`.

## Editor Work (User)

If Unity reports a missing script on the `AudioManager` object in
`BenchmarkLarge.unity` after the move, the `.meta` GUID was not preserved;
reassign the script component. Expected outcome is that nothing breaks.
