# 002 — Projectile collision: unified hash + same-faction skip

## Goal
Replace the faction-keyed projectile target hash with a **cell-only** hash and skip
candidates whose faction equals the projectile's faction.

## Background
[ProjectileCollisionSystem.cs](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs)
builds `targetCells` keyed by `CellKey(targetFactions[i].Value, cell)` (L85) and the job
looks up `CellKey(identity.Faction, cx, cy)` (L226). Under the firing-faction model both
sides keyed `Player`, so it matched. With own-faction `TargetFaction` (001) that keying
is wrong; switch to a unified hash + narrow-phase faction test.

## Changes
1. **Hash build (main thread):** key by cell only —
   `targetCells.Add(CellKey(cell.x, cell.y), i);` (L82-86). `targetFactions` is still
   queried/disposed; now it is also fed into the job.
2. **`CellKey`** (L474-484): drop the `CombatFaction faction` parameter (cell-only FNV).
3. **Job:** add `[ReadOnly] public NativeArray<TargetFaction> TargetFactions;` and set
   it from the captured array (the dispose-after-`collisionHandle` already covers it).
4. **Cell lookup (L226):** `long key = CellKey(cx, cy);`.
5. **Narrow phase (after fetching `targetIdx`, before/with the gate check, L235-243):**
   `if (TargetFactions[targetIdx].Value == identity.Faction) continue;` — skip
   same-faction. Keep the existing `identity.Faction == None` deactivate path (L185).

## Acceptance Criteria
- A `Player` projectile hits only non-`Player` targets; a `Mob` projectile hits only
  non-`Mob` targets — verified against a mixed-faction target set.
- No `CombatFaction` appears in any `CellKey` call in this file.
- Job dependency/dispose graph unchanged except the added read-only array.
- Existing pierce/gate/on-hit-spawn/VFX behavior unchanged for opposing-faction hits.

## Dependencies
001 (own-faction `TargetFaction`).

## Scope
Small–medium, single file.

## Note (deferred)
This makes a projectile scan every faction's targets in a cell and skip its own. The
faction- (or faction×region-) granular key is the deferred optimization (index I10).
