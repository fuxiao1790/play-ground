# 001 — Shared Acquisition Helper

**Depends on:** none. **Scope:** small, pure refactor, no behaviour change.

## Change

Extract the nearest-hostile query out of
`TargetedResolveSystem.TargetedResolveJob.TrySelectNthNearest`
([TargetedResolveSystem.cs:252-331](../../Assets/Scripts/System/Targeted/TargetedResolveSystem.cs#L252-L331))
into a new Burst-compatible static in the targeted namespace:

```
Assets/Scripts/System/Targeted/TargetedAcquisition.cs
```

Surface:

- `TrySelectNthNearest(in Snapshot snapshot, float2 from, float radius, int rank, CombatFaction faction, int excludeKey, out Entity entity, out float2 position)`
- `TryNearestHostile(...)` — `rank = 0`, `excludeKey = 0` convenience used by the gate (002).
- `Snapshot` — a small readonly struct holding the four `[ReadOnly] NativeArray`s plus
  `NativeParallelMultiHashMap<long,int> OccupiedCells`, so both a job and main-thread code can
  pass the hash snapshot without either owning it.
- `TargetKey(Entity)` moves here too; the resolve job and `TargetSpatialHashSystem` currently
  carry byte-identical copies.

`InsertNearest` and `Candidate` move with it, unchanged.

## Constraints

- Must stay Burst-compatible and allocation-free: `FixedList512Bytes<Candidate>` on the stack,
  no managed types, no `ref` fields that outlive the call (C9).
- Faction and shape tests must be copied verbatim — this task changes nothing observable (N1).
- `MaxChainCount` stays the resolve system's constant; the helper takes the bound as an argument
  so it does not own a gameplay cap.

## Acceptance criteria

- `TargetedResolveSystem` contains no selection logic; it calls the helper.
- Every existing `TargetedResolveEditModeTests` test passes unchanged, including the four faction
  tests and `ForkRank_DedupesMultiCellTargetBeforeSelectingSecondNearest` — that test is the
  regression guard for the dedupe/rank behaviour moving intact.
- No new allocation in the resolve job (verify by inspection; the type is still a struct job).
