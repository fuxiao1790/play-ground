# 002 — Consume in CombatApplyBridge (the moved blocking-wait)

## New pending state (fields on the bridge)
```
private JobHandle pendingApplyHandle;
private NativeList<CombatTickResult> pendingResults;
private NativeList<StatusStackSnapshot> pendingStatus;
private NativeReference<int> pendingEvictions;
private bool hasPending;
```

## SetPendingCombat (called by Finalize, 001)
Dispose/complete any prior un-consumed pending first (defensive), then store the new handle +
containers and set `hasPending = true`.

## DiscardPending (called by Finalize at its OnUpdate start, 001)
If `hasPending`: `pendingApplyHandle.Complete()`, dispose the three containers, clear the flag.
Also `DisposeFinalizedCombat()` (last frame's materialized arrays). This replaces the current
`DisposeFinalizedCombat()` reach-in.

## OnUpdate (presentation)
1. `DisposeFinalizedCombat()` — free last frame's materialized arrays (kept readable through
   last frame for tests; see 003). **Do not** also dispose here at the end anymore.
2. If `!hasPending` return.
3. `pendingApplyHandle.Complete();` ← **the blocking wait, now here.** Because Finalize returned
   without completing, the expansion IJobs scheduled after it were able to run on workers
   alongside this apply job.
4. Materialize: read `pendingEvictions.Value` → update the eviction counter; 
   `finalizedResults = pendingResults.ToArray(Allocator.Persistent)`; same for status.
5. Dispose `pendingResults`/`pendingStatus`/`pendingEvictions`; `hasPending = false`.
6. Existing `ReplayCombat(finalizedResults, finalizedStatusSnapshots, EntityManager)`.
7. **Keep** `finalizedResults`/`finalizedStatusSnapshots` (disposed at next OnUpdate step 1 /
   OnDestroy) so a test can read them after `presentationGroup.Update()`.

## Eviction counter move
Move `EntryEvictionCounter` (ProfilerCounterValue) and the `entryEvictions` accumulator from
Finalize to the bridge; update them in step 4. (Pure telemetry; no behavior change.)

## OnDestroy
Complete `pendingApplyHandle`, dispose any pending containers, then `DisposeFinalizedCombat()`.
