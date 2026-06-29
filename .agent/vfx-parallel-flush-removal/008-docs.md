# 008 — Update VFX docs

**Files:**
`Docs/reference/simulation/vfx-system.md`,
`Docs/flows/vfx-dispatch.md`,
`Docs/contracts/vfx-requests.md`
**Depends on:** 001–006
**Scope:** small

## Changes

### vfx-system.md
- Rewrite the **Data Flow** block: simulation jobs `Enqueue` `VfxPendingSpawn`
  into one persistent `NativeQueue<VfxPendingSpawn>` owned by
  `CombatVfxDispatchSystem` (via `AsParallelWriter()` + `ProducerHandle`);
  presentation completes `ProducerHandle`, drains the queue on the main thread
  (`CombatVfxRoot.DrainAndDispatch`), and dispatches. Remove the
  `VfxPendingSpawn → DynamicBuffer<VfxSpawnRequestElement> → VfxFlushJob` hops.
- **Key Classes:** delete `VfxFlushJob`/`VfxStreamFlushJob` entries; update
  `CombatVfxDispatchSystem` to "owns the persistent `NativeQueue<VfxPendingSpawn>`,
  exposes `AsParallelWriter()`/`ProducerHandle`, drains it each frame". Remove the
  `VfxSpawnRequestElement` / `VfxSingleton`-buffer descriptions.
- **Trigger Values / Authoring:** correct the stale note — trigger 0 *is* emitted
  by `AoeSpawnExpansionSystem` for expansion-spawned AOEs. Update the table's
  "Current emitters" for value 0 accordingly.

### vfx-dispatch.md
- Replace step 2 ("Flush jobs append requests to
  `DynamicBuffer<VfxSpawnRequestElement>`") with: producer jobs enqueue into the
  shared `NativeQueue<VfxPendingSpawn>`; presentation completes producers and
  drains the queue. Update Producers/Consumers lists (drop flush jobs; add
  `AoeSpawnExpansionSystem` as a trigger-0 producer).

### vfx-requests.md
- **Fields/Shape:** `VfxPendingSpawn` is the single request type; remove
  `VfxSpawnRequestElement`.
- **Lifetime:** requests live in the persistent shared queue until presentation
  drains them; drop the "flushed to the VFX singleton buffer" wording.
- **Trigger values:** update the `0` line to note it is emitted by
  `AoeSpawnExpansionSystem`.

## Acceptance criteria

- No doc references `VfxFlushJob`, `VfxStreamFlushJob`, `VfxSpawnRequestElement`,
  or the `VfxSingleton` buffer as live mechanisms.
- Data flow reads producer-jobs → shared `NativeQueue` → main-thread drain →
  dispatch.
- Trigger-0 emission by `AoeSpawnExpansionSystem` is documented.
