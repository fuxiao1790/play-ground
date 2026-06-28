# 003 — AOE collision: unified hash + same-faction skip

## Goal
Same structural change as 002 for both AOE collision paths (lingering/pulse and impact),
which share `AoeCollisionCore`.

## Background
- [AoeCollisionCore.cs](../../Assets/Scripts/System/Aoe/AoeCollisionCore.cs) walks cells
  via `CellKey(identity.Faction, cx, cy)` (L113) and has its own `CellKey(faction,…)`
  (L299-309).
- Both wrappers build `occupiedTargetCells` keyed by `CellKey(targetFactions[i].Value,…)`
  ([LingeringAoeCollisionSystem.cs:80](../../Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs#L80),
  [ImpactAoeCollisionSystem.cs:80](../../Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs#L80))
  and already query `TargetFaction` but never pass it into `RunCollision`.

## Changes
1. **`AoeCollisionCore.CellKey`** (L299-309): drop the `CombatFaction` parameter
   (cell-only).
2. **`AoeCollisionCore.RunCollision<TGate>`**: add a
   `NativeArray<TargetFaction> targetFactions` parameter. In the cell walk (L120-130),
   after resolving `i`, add `if (targetFactions[i].Value == identity.Faction) continue;`
   before the gate/bounds/hit checks. Cell key becomes `CellKey(cx, cy)` (L113).
   Keep the `identity.Faction == None` deactivate path (L94).
3. **Both wrappers:**
   - Build `occupiedTargetCells.Add(AoeCollisionCore.CellKey(x, y), i);` (cell-only).
   - Add `[ReadOnly] public NativeArray<TargetFaction> TargetFactions;` to each job,
     set from the already-captured `targetFactions` (still disposed after
     `collisionHandle`).
   - Pass `TargetFactions` into the `RunCollision(...)` call.

## Acceptance Criteria
- Lingering and impact AOEs damage only opposing-faction targets.
- No `CombatFaction` argument remains in any `AoeCollisionCore.CellKey` call.
- `MaxAoeTargetsPerTick` cap, contact-gate dedupe, and on-hit-spawn behavior unchanged
  for opposing-faction targets.

## Dependencies
001. Independent of 002/004.

## Scope
Medium: one shared core + two near-identical wrappers.
