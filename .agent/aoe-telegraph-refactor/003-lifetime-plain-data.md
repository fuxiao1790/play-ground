# 003 — Make `CombatLifetimeComponent` plain (non-enableable) data

## Goal
Drop `IEnableableComponent` from `CombatLifetimeComponent` so its only remaining meaning is the
`Remaining` timer. This closes the "disable lifetime to signal something" trap (the attempt-1
failure mode) permanently. Behavior-identical, because the enable bit is always `true` and is no
longer read after 002.

## Changes

1. **[CombatEcsComponents.cs:214-219](../../Assets/Scripts/System/Common/CombatEcsComponents.cs#L214)**
   - Change `public struct CombatLifetimeComponent : IComponentData, IEnableableComponent` to
     `: IComponentData`.
   - Update the doc-comment: remove "enableable" and the "Presence is the impact-vs-lingering
     discriminator" line (that role moved to `LingeringAoeTag` in 001). New intent: "plain
     lifetime timer; present on projectiles and lingering AOEs; absent on impact AOEs;
     `CombatLifetimeSystem` counts `Remaining` down and disables `Active` on expiry."

2. **AOE apply — [AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs)**
   - `LingeringAoeSpawnJob.Execute`: remove `EnabledMask lifetimeMask = ...` ([:391](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L391))
     and the `lifetimeMask[i] = true;` write ([:442](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L442)).
     Keep `lifetimes[i] = new CombatLifetimeComponent { Remaining = cfg.Lifetime };`.
   - `RecordLingeringReset`: remove `ecb.SetComponentEnabled<CombatLifetimeComponent>(entity, true);`
     ([:502](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L502)). Keep the `SetComponent`
     with `Remaining`.
   - The `LifetimeHandle` type handle stays (still used to write `Remaining`); only the enabled-mask
     usage is removed.

3. **Projectile apply — [ProjectileSpawnApplySystem.cs](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs)**
   - Remove `ecb.SetComponentEnabled<CombatLifetimeComponent>(entity, true);` ([:199](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L199)).
   - In the reuse job remove `EnabledMask lifetimeMask = ...` ([:322](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L322))
     and `lifetimeMask[i] = true;` ([:379](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L379)).
     Keep the `lifetimes[i] = new CombatLifetimeComponent { Remaining = cfg.Lifetime };` write.

4. **Verify presence-only consumers still compile unchanged** (no edits expected):
   - `CombatLifetimeSystem` `AoeLifetimeJob`/`ProjectileLifetimeJob` `WithAll<CombatLifetimeComponent>`
     — still valid as presence.
   - `TimedSpawnSystem` `WithAll<CombatLifetimeComponent>` + `in CombatLifetimeComponent` — valid.
   - `ProjectileCollisionSystem` `ref CombatLifetimeComponent` — valid.

5. **Tests** — remove now-invalid enable API calls/asserts:
   - `SetComponentEnabled<CombatLifetimeComponent>(...)` in
     [ProjectileTrackingSimulationTests.cs:335](../../Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs#L335),
     [ProjectileSpawnPipelineTests.cs:475](../../Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs#L475)/[:519](../../Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs#L519),
     [AoeSimulationTests.cs:1498](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L1498) — delete
     (the component is created enabled-by-default semantics no longer apply; presence is enough).
   - `IsComponentEnabled<CombatLifetimeComponent>` asserts in
     [AoeSimulationTests.cs:516](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L516)/[:583](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L583)/[:592](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L592)
     — delete or replace with `HasComponent<CombatLifetimeComponent>` where the test intent was
     "is a live lingering AOE" (prefer the `LingeringAoeTag` assertion from 001).
   - Any manual archetype builders in tests that list `typeof(CombatLifetimeComponent)` are fine
     (presence still valid); only the enable calls change.

## Acceptance criteria
- Project compiles; no `SetComponentEnabled<CombatLifetimeComponent>` /
  `IsComponentEnabled<CombatLifetimeComponent>` / `EnabledRef*<CombatLifetimeComponent>` /
  `WithPresent<CombatLifetimeComponent>` anywhere in the repo.
- Full projectile + AOE simulation suites pass with identical results.

## Scope / complexity
Medium. Highest surface of the three (projectiles + AOE + several tests) but purely mechanical
and behavior-identical.

## Dependencies
Depends on 002 (all `EnabledRef` reads must be gone before the component can lose
`IEnableableComponent`).
