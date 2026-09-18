# 006 — AOE hits on catalysts

## Goal

Friendly impact and lingering AOEs trigger catalysts they overlap, on the same
cadence they hit units, without spending the unit hit budget.

## Changes

`Assets/Scripts/System/Catalysts/CatalystHitEmission.cs`
- `ScanAoe(...)` — walks `CatalystAoeOccupiedCells` over the AOE's bounds cell
  range, narrow-phases with the same two-stage test, filters
  `entry.Faction == identity.Faction` (friendly only), and calls the shared
  `EnqueueTriggerSpawn`. No `HitWriter` write. AOEs have no pierce, so nothing
  is consumed.
- Its own per-tick budget constant, separate from
  `CollisionConstants.MaxAoeTargetsPerTick`. A crowded unit pass must not
  starve catalyst triggers, and catalyst overlaps must not consume the unit
  budget. Add `CollisionConstants.MaxAoeCatalystsPerTick` with the same
  "safety bound, not a gameplay knob" comment the existing cap carries.
- Reuse the caller's seen-key scratch discipline for dedup within one pass, or
  a separate small scratch range — do not share one counter with the unit pass.

`Assets/Scripts/System/Aoes/AoeCollisionCore.cs`
- `RunCollision` takes the catalyst lane plus `CatalystCount` and, after the
  unit cell walk, runs `ScanAoe` when `CatalystCount > 0`. Both AOE lanes get
  the behavior from this one edit.
- Hit VFX stays unit-driven: a catalyst overlap must not emit the AOE's hit VFX,
  or a ring standing in a lingering field would strobe. Leave
  `hitVfxEmitted` untouched by the catalyst pass.

`Assets/Scripts/System/Aoes/ImpactAoeCollisionSystem.cs`,
`Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs`
- Pass the catalyst lane through to `RunCollision`. Impact AOEs fire catalyst
  triggers once, on their single contact pass; lingering AOEs fire them once per
  `AoeHitGateComponent` tick, which is the existing retrigger bound and the
  reason no catalyst-side cooldown exists.

## Acceptance criteria

- An impact AOE covering three catalysts fires three triggers, once.
- A lingering AOE covering a catalyst fires one trigger per tick interval, not
  per frame.
- An enemy-faction AOE covering a player catalyst fires nothing.
- A catalyst overlap produces no `CombatHitEvent` and no AOE hit VFX.
- 32 units plus catalysts in one AOE: units still get their full budget and
  catalyst triggers still fire.
- Zero catalysts leaves the AOE path at its current cost.

## Tests to run (PlayMode, `CatalystAoeHitTests`)

- `ImpactAoe_FiresEachOverlappedCatalystOnce`
- `LingeringAoe_FiresOncePerTickInterval`
- `EnemyAoe_IgnoresFriendlyCatalyst`
- `CatalystOverlap_EmitsNoHitEventOrHitVfx`
- `UnitBudget_AndCatalystBudget_AreIndependent`

## Dependencies

004, and 005 for the shared `CatalystHitEmission` helper and aim resolution.

## Scope

Medium. One helper method plus threading the lane through two call sites.
