# Implementation Log

## Status
Blocked

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-resource-regen-job.md | Complete | Replaced managed loops with chained Burst `IJobEntity` jobs. Static verification complete; Unity tests deferred. |
| 002-spawn-event-gather-job.md | Complete | Shared generic gather scheduled by all four lanes; static verification complete. Burst Inspector and Unity XML tests deferred. |
| 003-spawn-pool-topup-batch-enable.md | Complete | Installed Entities lacks NativeArray bulk overload; added scoped ColdSlotTag marker path across five pool archetypes. Static verification complete; Unity tests deferred. |
| 004-external-spawn-gate-job.md | Complete | Gate routing, mana debit, and targeted acquisition moved to scheduled Burst job with hash consumer/rejection producer handles. Static verification complete; Unity tests deferred. |
| 005-target-proxy-apply-jobs.md | Complete | Proxy updates scheduled in Burst; delete events collected in Burst then destroyed once as deduplicated batch. Static verification complete; Unity tests deferred. |
| 006-spatial-hash-gather-job.md | Blocked | Scheduled gather cannot feed required main-thread capacity calculation without a completion the task rejects. |
| 007-stats-counters.md | Pending | |
| 008-apply-bridge-redundant-pass.md | Pending | |

## Completed Tasks
- 001-resource-regen-job.md: `ResourceRegenSystem.cs` updated with separate Burst health and mana jobs; single-component entity behavior preserved.
- 002-spawn-event-gather-job.md: Added `GatherSpawnEventsJob<T>` and converted projectile, impact AOE, lingering AOE, and targeted expansion gathers to deferred native lists.
- 003-spawn-pool-topup-batch-enable.md: Replaced per-entity active toggles with a fresh-batch `ColdSlotTag` query path for five pool archetypes.
- 004-external-spawn-gate-job.md: Replaced managed gate loop with `ExternalSpawnGateJob`; retained fail-loud invalid-kind behavior through persistent native counter.
- 005-target-proxy-apply-jobs.md: Added scheduled proxy update job and batch proxy-delete collection/job path without ordering changes.

## Blockers
- 006-spatial-hash-gather-job.md: `GatherTargetsJob.Schedule()` makes snapshot lists unavailable to the required main-thread `CalculateAoeCapacity` until completed. Task both rejects that completion and requires capacity stay main-thread.

## Validation Summary
- 001: Static source verification complete: no `foreach` in `OnUpdate`; two `[BurstCompile] IJobEntity` structs chain through `state.Dependency`; original health and mana math retained. Unity tests deferred to user. XML results required.
- 002: Static source verification complete: four explicit generic registrations, all four callers schedule the shared gather and dispose events/chunks on normal and template-missing paths; managed pre-count and drain loops removed. Burst Inspector and Unity tests deferred to user. XML results required.
- 003: Verified installed Entities 6.4.0 lacks `SetComponentEnabled<T>(NativeArray<Entity>, bool)`. Static verification confirms marker is lifecycle-documented and exists on all five archetypes. Runtime follow-up split fresh-slot operations into an Active-enabled query and an Active-agnostic marker query so the marker is consumed after Active is disabled. Unity pool/spawn tests deferred to user. XML results required.
- 004: Static source verification confirms no `BuildHandle.Complete()` or request loop in `OnUpdate`; job publishes existing hash consumer and rejection producer handles, retains mana fallback/clamp and targeted fallback fields. Unity routing tests deferred to user. XML results required.
- 005: Static source verification confirms update `OnUpdate` has no completion or event loop and final dependency owns temporary chunks; delete has one guarded batch destroy after job completion. Unity proxy/collision tests deferred to user. XML results required.
- 006: Not implemented. Source inspection confirmed an unresolved scheduling/data-availability conflict; no later tasks executed.
