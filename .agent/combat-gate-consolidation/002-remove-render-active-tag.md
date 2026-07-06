# 002 — Remove `CombatRenderActiveTag`; derive visibility from `Active`

## Goal

Delete the `CombatRenderActiveTag` enableable component. Render prepare, batched
submit, and stats-count systems select the renderable archetype and read the
`Active` enabled mask instead. Behavior-identical.

## Why safe

The render-active bit is written to the same value as `Active` at every site:
- projectile spawn: both `true`;
- impact AOE spawn: both `collisionEnabled`;
- lingering AOE spawn: both `true`;
- every death path (lifetime expiry, projectile collision, AOE `Deactivate`)
  disables both.

So `render == Active` today; substituting the `Active` mask reproduces the exact
degenerate/visible split. Render systems use
`EntityQueryOptions.IgnoreComponentEnabledState` and drive visibility purely from
the mask, so the query's selecting tag only needs to be a component present on the
renderable archetype — `Active` qualifies.

## Changes

### Component definition
- Delete `CombatRenderActiveTag` from
  [CombatRenderComponents.cs](../../Assets/Scripts/System/Common/CombatRenderComponents.cs).

### Render systems (selecting tag + mask -> `Active`)
- [CombatRenderPrepareSystem.cs](../../Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs):
  `renderPrepareQuery` `WithAll<CombatRenderActiveTag>` -> `WithAll<Active>`; the
  `renderActiveHandle` / `RenderActive` job field -> `ComponentTypeHandle<Active>`;
  `GetEnabledMask(ref RenderActive)` -> `GetEnabledMask(ref Active)`. Keep
  `IgnoreComponentEnabledState` and the degenerate-instance branch unchanged.
- [CombatBatchedRenderSystem.cs](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs):
  `renderQuery` `WithAll<CombatRenderActiveTag>` -> `WithAll<Active>` (keep
  `WithAny<ProjectileTag, AoeTag>` + `IgnoreComponentEnabledState`).

### Stats
- [CombatStatsGatherSystem.cs](../../Assets/Scripts/System/Stats/CombatStatsGatherSystem.cs):
  both `activeProjectileRenderQuery` / `activeAoeRenderQuery`
  `WithAll<CombatRenderActiveTag>` -> `WithAll<Active>`. Counts are identical since
  render==Active.
- [CombatStatsComponents.cs](../../Assets/Scripts/System/Stats/CombatStatsComponents.cs):
  update the doc comment that says counts come from `WithAll<CombatRenderActiveTag>`.

### Spawn apply (drop the redundant enable writes + archetype entries)
- [ProjectileSpawnApplySystem.cs](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs):
  remove `CombatRenderActiveTag` from the archetype, the `RenderActiveHandle`, the
  reuse-job `renderActiveMask` write, and the cold-create
  `SetComponentEnabled<CombatRenderActiveTag>` in `RecordCommonProjectileReset`.
- [AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs):
  remove from both archetypes, both `RenderActiveHandle`s, both reuse-job
  `renderActiveMask` writes, both `RecordImpactReset` / `RecordLingeringReset`
  render-enable calls. Drop `SpawnState.Render` (and its use), since render is no
  longer a separately materialized bit.

### Death paths (drop render disable)
- [CombatLifetimeSystem.cs](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs):
  remove `EnabledRefRW<CombatRenderActiveTag> renderActive` + its `= false` write
  from both `ProjectileLifetimeJob` and `AoeLifetimeJob`.
- [ProjectileCollisionSystem.cs](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs):
  remove the render-active disable on despawn.
- [AoeCollisionCore.cs](../../Assets/Scripts/System/Aoe/AoeCollisionCore.cs):
  drop `EnabledRefRW<CombatRenderActiveTag> renderActive` from `RunCollision` /
  `Deactivate` (and the callers
  [ImpactAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs),
  [LingeringAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs)).

### Tests (drop/replace render-active assertions)
- [ProjectileSpawnPipelineTests.cs](../../Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs)
- [CombatPoolCleanupSystemTests.cs](../../Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs)
- [ProjectileCollisionSimulationTests.cs](../../Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs)
- [AoeSimulationTests.cs](../../Assets/Tests/PlayMode/AoeSimulationTests.cs)
- [BareMinimumPrototypePlayModeTests.cs](../../Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs)
  — assertions on `CombatRenderActiveTag` become assertions on `Active` (or are
  dropped where they duplicate an `Active` check).

## Acceptance criteria

- No reference to `CombatRenderActiveTag` remains (grep clean).
- `CombatRenderComponent` size assert (32 bytes) still passes.
- Existing PlayMode suite passes unchanged (user-run): sprites still render for
  live entities, dead slots still degenerate/invisible, on-screen counts
  (`ActiveProjectiles` / `ActiveAoes`) unchanged.

## Scope

Medium. Touches render + stats + every spawn/death site, but each edit is a
deletion or a one-symbol swap; no logic change.

## Note for the arming follow-on

Removing this bit now is what lets arming stay net-flat later: arming's
"sprite off while winding up" becomes `Active && !ArmingTag`, added as a
`WithDisabled<ArmingTag>` clause on the render/stats queries — no render bit is
resurrected. Tracked in `../common-combat-lifecycle/`.
