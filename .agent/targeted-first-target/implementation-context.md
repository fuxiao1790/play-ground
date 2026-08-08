# Implementation Context

## Architectural Decisions
- Gate gets first target only for root cast targeted requests.
- Gate stamps target position into `AcquireAnchor`, not target entity.
- Resolve keeps fork rank and hop selection behavior.
- One `TargetedAcquisition` helper is source of truth for nearest hostile selection.

## Global Invariants
- `chainDistance` is only authored acquisition/reach radius.
- Same-faction proxies are never selected.
- Spatial hash read needs `BuildHandle.Complete()` first.
- Selection uses bounded stack `FixedList512Bytes<Candidate>`; no managed allocation.
- ECS lifecycle comments use `ECS Lifecycle:` when lifecycle changes.

## Ownership / Data Flow
- External gate reads request, template registry, and target hash; it appends intent only.
- Expansion reads registry and fans events into commands.
- Apply materializes command into pooled targeted entity.
- Resolve walks links and emits hit/VFX results.

## ECS / Threading
- Helper must work in Burst jobs and on the main thread.
- Gate schedules no job and publishes no consumer handle.
- Hash snapshot is readonly arrays plus occupied-cell map.

## Reused / Introduced
- Reuse `TargetSpatialHashSingleton`, `TargetedSpawnTemplate`, `AcquireAnchor`, and arming.
- Add `TargetedAcquisition`, `HasAcquiredTarget` on targeted event/command.

## Validation
- Use static/search review only here; do not run Unity tests.
- User must run EditMode and PlayMode suites with XML result files.

## Files / Systems
- `TargetedAcquisition.cs`
- `TargetedResolveSystem.cs`
- `ExternalSpawnGateSystem.cs`
- `TargetedSpawnPipeline.cs`
- `TargetedSpawnExpansionSystem.cs`
- `TargetedSpawnApplySystem.cs`
- `CombatRoot.cs`
- targeted EditMode tests and targeted system/contract docs
