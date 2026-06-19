# Task 02 — Rewrite AoeCollisionJob (delete NativeHashSet) — PRIMARY FIX

**Depends on:** 01. **Blocks:** 04 (verification).

## Goal

Remove the per-entity `new NativeHashSet<int>(4, Allocator.Temp)` from
`AoeCollisionJob.Execute` ([AoeCollisionSystem.cs:175](../../Assets/Scripts/System/Aoe/AoeCollisionSystem.cs#L175)).
Replace `CollectCandidates` + the candidate `foreach` with an inline cell walk
that runs narrow phase directly, de-dups via the contact-gate buffer, and stops
at the hard-coded cap `CollisionConstants.MaxAoeTargetsPerTick` (32). This is
exactly the shape `ProjectileCollisionJob` already uses.

No component or query change: `hitGate` is still passed (`in AoeHitGateComponent`)
and used only for `RepeatHitCooldownSeconds`. There is **no** `MaxTargets` field.

## New Execute control flow

Replace lines ~165-234 (the body from `vfxPending` setup through the
`candidates.Dispose()` block) with:

```csharp
NativeStream.Writer vfxPending = VfxPending;
vfxPending.BeginForEachIndex(entityIndexInQuery);

if (identity.Faction == CombatFaction.None)
{
    Deactivate(active, collisionActive, renderActive);
    EndVfxStream(ref vfxPending);
    return;
}

// Bounded, allocation-free broadphase: walk cells inline, narrow-phase each
// candidate, de-dup via the contact gate, stop at the hard cap. When overlap
// exceeds the cap, survivors are first-N in cell-scan order (cy -> cx -> bucket),
// NOT nearest-N. Accepted: 32 is far above any real overlap count.
int remaining = CollisionConstants.MaxAoeTargetsPerTick;

bool hitVfxEmitted = false;
float cooldown = !lifetimeEnabled.ValueRO ? 0f : hitGate.RepeatHitCooldownSeconds;

int2 min = MinCell(collision.BoundsMin);
int2 max = MaxCell(collision.BoundsMax);
for (int cy = min.y; cy <= max.y && remaining > 0; cy++)
{
    for (int cx = min.x; cx <= max.x && remaining > 0; cx++)
    {
        long key = CellKey(identity.Faction, cx, cy);
        if (!OccupiedTargetCells.TryGetFirstValue(key, out int i,
                out NativeParallelMultiHashMapIterator<long> it))
        {
            continue;
        }

        do
        {
            Entity targetEntity = TargetEntities[i];
            int targetKey = TargetKey(targetEntity);

            // Gate doubles as per-tick de-dup: a target already hit this tick (or
            // still cooling down) is skipped, so the same target appearing in
            // multiple overlapping cells is processed at most once per tick.
            if (IndexOfGate(contactGates, targetKey) >= 0)
            {
                continue;
            }

            TargetPosition targetPosition = TargetPositions[i];
            TargetCollisionShape target = TargetShapes[i];

            if (!CombatCollisionMath.BoundsIntersect(
                    collision.BoundsMin, collision.BoundsMax,
                    target.BoundsMin, target.BoundsMax))
            {
                continue;
            }

            if (!CombatCollisionMath.Hit(
                    kinematics.Position, collision.Radius, collision.HalfExtents,
                    collision.RotationRadians, collision.ShapeType,
                    targetPosition.Value, target.Radius, target.HalfExtents,
                    target.RotationRadians, target.ShapeType))
            {
                continue;
            }

            contactGates.Add(new AoeContactGateElement
            {
                TargetId = targetKey,
                CooldownRemaining = cooldown
            });
            EmitHit(identity, kinematics, hitSpawn, area, targetEntity,
                targetPosition, targetKey, ref vfxPending, ref hitVfxEmitted);

            if (--remaining == 0)
            {
                break;
            }
        }
        while (OccupiedTargetCells.TryGetNextValue(out i, ref it));
    }
}

if (!lifetimeEnabled.ValueRO)
{
    Deactivate(active, collisionActive, renderActive);
}

EndVfxStream(ref vfxPending);
```

## Deletions

- Delete `CollectCandidates` ([lines 330-348](../../Assets/Scripts/System/Aoe/AoeCollisionSystem.cs#L330-L348)).
- Delete `ResolveHit` ([lines 236-261](../../Assets/Scripts/System/Aoe/AoeCollisionSystem.cs#L236-L261)) — its gate-check + add + emit are inlined above. `EmitHit` is kept and called directly.
- Add `using PlayGround.System.Common;` (for `CollisionConstants`) if not present.
- `NativeHashSet` usage disappears; leave the `Unity.Collections` using (other
  collections still used).

## Correctness notes

- `MinCell` / `MaxCell` / `CellKey` unchanged and still used.
- `OccupiedTargetCells` build in `OnUpdate` ([lines 61-83](../../Assets/Scripts/System/Aoe/AoeCollisionSystem.cs#L61-L83)) unchanged — targets still registered across every overlapped cell; the inline walk handles duplicates via the gate.
- Behavior parity for the common case (real overlap ≤ 32): identical hit set,
  identical single-VFX-per-AOE behavior (`hitVfxEmitted`).
- Difference vs old code: a target in multiple cells that **misses** narrow phase
  may be `Hit()`-tested more than once (no candidate de-dup). Pure redundant math,
  no allocation, accepted (rare at `SpatialHashCellSize = 64`).

## Acceptance

- No `Allocator.Temp` / `NativeHashSet` inside `AoeCollisionJob`.
- Project compiles; existing AOE collision tests pass.
- Manual: an AOE overlapping N ≤ 32 targets damages exactly those N (deduped),
  emits one VFX; an AOE overlapping > 32 targets damages 32 of them.
