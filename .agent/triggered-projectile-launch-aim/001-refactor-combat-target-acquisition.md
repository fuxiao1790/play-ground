# 001 - Refactor Common Target Acquisition

## Change

Move/rename `Assets/Scripts/System/Targeted/TargetedAcquisition.cs` to common
combat-target ownership as `CombatTargetAcquisition`. Keep its Burst-compatible
`Snapshot`, nearest/Nth-nearest selection, collision-shape range test,
deduplication, faction filter, deterministic ordering, and excluded-target support.

Update existing callers in `ExternalSpawnGateSystem`, `TargetedResolveSystem`, and
targeted tests. Do not alter targeted gameplay behavior.

## Acceptance Criteria

- One production implementation owns nearest-hostile spatial-hash selection.
- Helper name/namespace no longer implies targeted-archetype ownership.
- Existing targeted root acquisition and chain selection use renamed helper.
- Existing deterministic ordering, target-shape intersection, rank behavior, and
  exclusion behavior unchanged.
- No managed references or allocations enter helper.
- No projectile/targeted entity archetype changes.

## Dependencies

None.

## Estimated Scope

Small refactor: one file move/rename plus caller/test references.
