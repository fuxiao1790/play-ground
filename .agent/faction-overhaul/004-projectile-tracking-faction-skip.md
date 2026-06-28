# 004 — Projectile target acquisition: unified hash + same-faction skip

## Goal
Homing projectiles must acquire/refresh only opposing-faction targets, via the same
unified-hash + skip pattern.

## Background
[ProjectileTrackingSystem.cs](../../Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs)
builds two faction-keyed maps: `targetIndicesById` keyed by `TargetIdKey(faction, id)`
(L57) and `targetCells` keyed by `CellKey(faction, cell)` (L59). The acquisition job
looks up both by `identity.Faction` (L153, L207). `TargetKey` is unique per entity
regardless of faction, so faction in these keys is removable.

## Changes
1. **Hash build (L52-60):** key cells by `CellKey(cell.x, cell.y)` and ids by
   `TargetIdKey(TargetKey(target))` (drop faction from both). Pass `targetFactions`
   into the acquisition job (it is already captured and disposed after
   `acquisitionHandle`).
2. **`CellKey`/`TargetIdKey` (L403-434):** drop the `CombatFaction` parameter.
3. **Acquisition job:** add `[ReadOnly] public NativeArray<TargetFaction> TargetFactions;`.
   - `TryRefreshTrackedTarget` (L133-168): look up `TargetIdKey(tracking.TrackedTargetId)`;
     after resolving the index, verify `TargetFactions[index].Value != identity.Faction`
     (drop a now-stale same-faction target).
   - `TrySelectRandomTargetInCell` (L224-264): pass through the faction array (or the
     projectile faction) and skip a candidate when
     `TargetFactions[targetIndex].Value == identity.Faction`.
   - Cell lookups use `CellKey(cell.x, cell.y)`.
   Keep the `identity.Faction == None` early-out (L110).

## Acceptance Criteria
- A homing `Player` projectile only acquires/steers toward non-`Player` targets (and
  vice versa).
- No `CombatFaction` argument remains in `CellKey`/`TargetIdKey` calls in this file.
- Refresh correctly drops a cached target that no longer qualifies.

## Dependencies
001. Independent of 002/003.

## Scope
Medium, single file (two nested helpers threaded with the faction array).
