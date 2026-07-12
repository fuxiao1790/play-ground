# 003 — Update tests whose finalized-result read-window collapses

## Why
`ReadFinalizedCombatResults()` reflects into `CombatApplyBridge.finalizedResults`. Today those
arrays are materialized in **simulation** (by Finalize), so three tests read them in the window
**between** `hitApply.Update()`/`TickSimulationOnly` and `presentationGroup.Update()`. After the
move, materialization happens **in the bridge** (presentation), so that window is empty — the
read must move to **after** `presentationGroup.Update()`. Step 002 keeps `finalizedResults`
alive across the frame so the post-presentation read succeeds.

## Tests to reorder (AoeSimulationTests.cs)
1. `EntityKeyedDamage_FinalizedEventUsesHitProxyAndDispatchResolvesCompanion` (~L695).
   Move `presentationGroup.Update()` **before** `ReadFinalizedCombatResults()`; the live
   `TargetHealth == finalized[0].Health` assert also moves after presentation.
2. `CombatApplyAggregatesEveryHitForOneTarget` (~L720). Move the read after
   `presentationGroup.Update()` (it already calls it at L723 — just read afterwards).
3. `EcsPushesLethalOverkillAndTargetOwnsHealthClamp` (~L767). Same reorder as #2.

Damage/crit tests that assert via `target.Hits` after `presentationGroup.Update()`
(e.g. `EcsCritRollsAreDeterministic…`) are unaffected — they never read the intermediate window.

## Note
`TickStatusPipelineOnly` runs only Status+Finalize (not the bridge). With deferral, results are
not materialized until the test calls `presentationGroup.Update()`. Multi-tick tests without a
presentation update rely on Finalize-start `DiscardPending` (001) to complete the stale apply
job before the next tick reads `HitQueue`.
