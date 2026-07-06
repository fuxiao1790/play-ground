# 002 — Merge `CombatRenderActiveTag` into `Active` (conditional)

## Precondition
Only proceed if **001 confirms** render == Active for AOE + projectile **and** the user accepts
foreclosing telegraph-only (Active-but-not-rendered) states. Otherwise this task is cancelled.

## Goal
Delete `CombatRenderActiveTag`; make render and stats gate on `Active`. Removes one redundant
enableable bit from every AOE and projectile spawn/despawn path. Behavior-preserving (given the 001
invariant).

## Changes (cross-cutting — AOE + projectile + render + stats)
1. **Render/stats queries** → `WithAll<Active>`:
   - [CombatBatchedRenderSystem.cs:54](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L54)
   - [CombatRenderPrepareSystem.cs:28](../../Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs#L28) (+ handle at :35/:60)
   - [CombatStatsGatherSystem.cs:26,32](../../Assets/Scripts/System/Stats/CombatStatsGatherSystem.cs#L26)
   Confirm `Active` gives the same set these intended (live, visible entities). `Active` disabled ==
   dead slot == not rendered — matches.
2. **Remove all toggles/handles** of `CombatRenderActiveTag`:
   - AOE materialization ([AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs)):
     archetypes, `RenderActiveHandle`, reuse masks, ECB `SetComponentEnabled`. `SpawnState.Render` is
     dropped (it equals `SpawnState.Active`).
   - `AoeCollisionCore.Deactivate` / `RunCollision` — drop the `renderActive` EnabledRef (Active already
     disabled there).
   - `CombatLifetimeSystem` (AOE + projectile jobs) — drop `renderActive` writes.
   - Projectile: `ProjectileSpawnApplySystem`, `ProjectileCollisionSystem` — drop render toggles/handles.
3. **Delete the component** `CombatRenderActiveTag` ([CombatRenderComponents.cs:100](../../Assets/Scripts/System/Common/CombatRenderComponents.cs#L100))
   and all archetype entries once no references remain.
4. **Tests**: remove `CombatRenderActiveTag` assertions/archetype entries; the 001 render==Active
   characterization is retired (subsumed by "renders iff Active").

## Acceptance criteria
- No `CombatRenderActiveTag` references remain (grep clean).
- **User runs AOE + projectile PlayMode suites: green.** Visuals unchanged in-editor (render only live
  entities); active-visual stat unchanged.

## Scope / complexity
Medium–high. Mechanical but broad (render + stats + AOE + projectile). Behavior-preserving under the
001 invariant.

## Dependencies
001 (proof) + user decision. Best done after `../aoe-spawn-state-unify/` (so `SpawnState` is the single
place `Render` is dropped from).
