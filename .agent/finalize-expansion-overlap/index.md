# Finalize → Expansion Overlap (defer the apply blocking-wait to the presentation consumer)

## Goal
Let the AOE/projectile **spawn-expansion** jobs run on worker threads **while the combat
apply job is still in flight**, filling the worker-idle bubble that Finalize's synchronous
`Dependency.Complete()` currently creates.

## Why it's now possible
After the Status→Finalize swap, the producer of expansion's input (StatusProcessSystem)
runs **before** Finalize (apply). Expansion's input no longer depends on apply, and apply's
job (`TargetHealth`/`TargetStackEntry`) is data-disjoint from expansion's job
(`AoeSpawnCommand`/spawn queues). See [[project_finalize_expansion_overlap]].

## The one true blocker
System `OnUpdate`s run sequentially on the main thread. `CombatApplyFinalizeSingleSystem`
calls `Dependency.Complete()` and then reads the job outputs on the main thread
(`evictionRef.Value`, `resultsList.ToArray()`, `bridge.SetFinalizedCombat`). That blocking
wait parks the main thread, so the expansion systems' `OnUpdate` are not even invoked until
apply is finished. **Move the wait + the output materialization to the consumer** — the
`CombatApplyBridge` in `PresentationSystemGroup`, which already replays results to GameObjects.

## Result ordering (unchanged group order, new timing)
- SimulationSystemGroup: … Status → **Finalize (schedule apply, DO NOT complete)** →
  ImpactAoe/LingeringAoe/Projectile SpawnExpansion (schedule disjoint jobs) → SpawnApply …
- PresentationSystemGroup: **CombatApplyBridge** completes the apply handle (the moved wait),
  materializes results, replays to GameObjects.

Apply's single-threaded IJob now overlaps the expansion IJobs on the worker pool.

## Subtasks
- 001 — defer apply in Finalize; hand un-completed pending to the consumer
- 002 — consume (complete + materialize + replay) in CombatApplyBridge; move eviction counter
- 003 — update tests whose read-window between hitApply.Update() and presentationGroup.Update() collapses

## Open decision for the user (pending-state lifecycle)
Where the pending handoff lives — see 001. Recommendation: **bridge-held pending** with a
Finalize-start discard, because it keeps the existing Finalize→bridge managed handoff and
adds no singleton schema. Alternative: relocate pending into `CombatHitDispatchSingleton`
(removes the reach-in, per [[project_combat_event_singletons]], but more churn).

## Verification (harness cannot run Unity)
User runs PlayMode `AoeSimulationTests`. Watch: finalized-result tests (§003), overkill/crit
damage tests, and profile that expansion now overlaps apply (worker timeline).
