# 004 — Add `PlayGround.Sim.asmdef` at `System/`; move shared `Common` files under it

**Depends on:** 001, 002, 003, 008 (sim file set is now free of game-logic *and*
Debugging references). **Scope:** small — one asmdef + three file moves.

## Objective
Carve the ECS-simulation assembly out of the single `PlayGround.Runtime` by adding a
nested asmdef, without relocating the bulk of the code (it already lives in
`Assets/Scripts/System/`).

## Changes
1. Create `Assets/Scripts/System/PlayGround.Sim.asmdef`:
   - `name: PlayGround.Sim`.
   - `references`: the Unity ECS stack the sim names (Entities, Collections,
     Mathematics, Burst, + Unity modules used) — computed from the `System/**`
     `using`s. **No** `PlayGround.*` reference.
   - This nested asmdef automatically removes `System/**` from the root
     `PlayGround.Runtime` assembly's scope.
2. Move (with `.meta`, GUIDs preserved) the three shared primitives under the Sim
   asmdef folder — `Assets/Scripts/System/Shared/`:
   - `DamageSnapshot.cs`, `GameplayTags.cs`, `GameplayLayers.cs` (from `Common/`).
   - Namespace may stay `PlayGround.Common` (independent of folder/assembly).
3. Delete the stale `Assets/Scripts/System/Application/CombatApplyFinalizeSystem.cs.delete`.

## Acceptance criteria
- `PlayGround.Sim.asmdef` lists zero `PlayGround.*` references.
- The three shared files compile inside `PlayGround.Sim` and are referenced by
  game-logic downward only.
- No `.asset`/scene/prefab shows a missing MonoScript (moves preserved `.meta`).

## Verification (user, Unity)
- Unity recompiles; `PlayGround.Sim` appears as its own assembly and builds with no
  `PlayGround.*` reference.
