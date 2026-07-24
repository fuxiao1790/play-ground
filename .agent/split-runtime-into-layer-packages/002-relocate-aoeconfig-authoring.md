# 002 — Move `AoeConfig` to game-logic authoring

**Depends on:** 001 (removes CombatRoot's `AoeConfig` use, so the only remaining
`AoeConfig` referencers are game-logic). **Scope:** small.

## Objective
`AoeConfig` is authoring data misfiled in the sim namespace. Relocate it so it sits
with the other authoring types it depends on (`BasicAoePrefab` in `PlayGround.Skills`),
which removes the sim's last upward edge and dissolves the `Common ↔ sim` cycle.

## Changes
- Move `Assets/Scripts/System/Aoes/AoeConfig.cs` (+ `.meta`) to game-logic authoring
  (e.g. `Assets/Scripts/Skills/Authoring/AoeConfig.cs`). Keep the `.meta` GUID.
- Change its namespace `PlayGround.System.Combat.Aoes` → a game-logic authoring
  namespace (proposed `PlayGround.Skills`, matching `BasicAoePrefab`).
- It still references sim contracts (`AoeTypeDefinition`, `AoeSpawnGeometry`) — that
  is a legal game-logic→sim reference; keep those `using`s. Drop the now-unused
  sim `using`s while here (Application/Collision/Lifetime/Platform/Rendering/
  Spawning/Status/Targets are mostly dead imports — remove any not referenced).
- Update the two referencers for the new namespace:
  - `Assets/Scripts/Common/StatusEffects/StackingTriggerDef.cs`: replace
    `using PlayGround.System.Combat.Aoes;` with the new authoring namespace.
  - `Assets/Scripts/Skills/SkillDriver.cs`: adjust `using` if it named the old
    namespace for `AoeConfig` (it already lives in `PlayGround.Skills`, so likely
    no change).

## Acceptance criteria
- `grep -rn "using PlayGround\.\(Skills\|Player\|Mob\|Spawn\|Persistence\|Audio\|Game\|Level\|CameraSystem\)" Assets/Scripts/System/`
  returns **empty**.
- `grep -rn "using PlayGround\.System" Assets/Scripts/Common/` returns **empty**
  (the `Common → sim` edge is gone).
- `AoeConfig`'s `.asset` instances still resolve (GUID preserved) — no "missing
  script" in inspectors.

## Verification (user, Unity)
- Any `AoeConfig` ScriptableObject asset opens with fields intact.
