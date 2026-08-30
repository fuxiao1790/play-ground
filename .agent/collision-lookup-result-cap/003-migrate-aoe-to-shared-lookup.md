---
name: migrate-aoe-to-shared-lookup
description: Switch AoeCollisionCore.RunCollision from its inline cell-walk to driving TargetSpatialHashLookup. Behavior-preserving.
---

# 003 - Migrate AOE to the Shared Lookup

## Depends On

[002-introduce-spatial-hash-lookup.md](002-introduce-spatial-hash-lookup.md).

## Changes

### [AoeCollisionCore.cs](../../Assets/Scripts/System/Aoes/AoeCollisionCore.cs)

In `RunCollision` (the double `for (cy...) { for (cx...) { ... } }` block,
currently [lines 71-149](../../Assets/Scripts/System/Aoes/AoeCollisionCore.cs#L71-L149)):

1. Compute `cellMin`/`cellMax` exactly as today (`CombatSpatialHash.MinCell`/`MaxCell`
   on `collision.BoundsMin`/`BoundsMax`).
2. Construct `var lookup = new TargetSpatialHashLookup(occupiedTargetCells, targetFactions, identity.Faction, cellMin, cellMax);`.
3. Replace the nested `for`/`do-while` with:
   ```csharp
   while (remaining > 0 && lookup.MoveNext(out int i))
   {
       // existing per-candidate body: seen-key dedup check, TargetPosition/TargetCollisionShape
       // lookup, BoundsIntersect, exact Hit test, seenTargetKeys insert, EmitHit, remaining--
   }
   ```
4. Remove the now-dead inline cell/bucket-walking code: the `min`/`max`
   locals' loop usage, the `key`/`occupiedTargetCells.TryGetFirstValue`/`it`
   variables, and the per-candidate `targetFactions[i].Value == identity.Faction`
   check (now handled inside the lookup).
5. Everything from `Entity targetEntity = targetEntities[i];` onward in the
   current loop body (dedup check, position/shape fetch, `BoundsIntersect`,
   `Hit`, `seenTargetKeys` insert, `EmitHit`, `if (--remaining == 0) break;`)
   stays exactly as-is, just re-indented into the new `while` body. The
   `if (--remaining == 0) break;` becomes redundant with the `while` loop's
   own `remaining > 0` condition — keep it as `if (--remaining == 0) break;`
   or simplify to rely solely on the loop condition; either is acceptable as
   long as the accepted-hit cap behavior (decrement after `EmitHit`, stop at
   zero) is unchanged.

No changes to `EmitHit`, `Deactivate`, `ContainsSeenTargetKey`, or any other
function in this file. No changes to `ImpactAoeCollisionSystem.cs` or
`LingeringAoeCollisionSystem.cs` — they call `RunCollision` the same way.

## Acceptance Criteria

- All existing AOE PlayMode tests in
  [AoeSimulationTests.cs](../../Assets/Tests/PlayMode/AoeSimulationTests.cs)
  pass unmodified, including:
  - `OverlappingTargetRegisteredInMultipleCellsHitsOnce` (dedup still works
    across cells).
  - `PulseHitsAtMostMaxAoeTargetsPerTick` (cap still stops at `MaxHitsPerTick`,
    per task 001's rename).
  - `PulseOverflowHitsFirstTargetsInCellScanOrder` (ordering guarantee —
    this is the test that would catch a subtle enumeration-order regression
    from the new lookup type).
- No change to `AoeCollisionCore`'s public signature (`RunCollision`'s
  parameter list is unchanged from task 001's state — this task only changes
  the function body).
- `ImpactAoeCollisionJob` and `LingeringAoeCollisionJob` remain Burst-compiled
  with no new allocations.
