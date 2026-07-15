# 004 — Burst bucketing of the drain (MEASUREMENT-GATED)

## Status: do not start without a profile capture

This task is **speculative optimization** and is sequenced last deliberately. Nothing observed in
play mode has implicated the drain. It is written down so the option is understood, not because it
is justified yet.

**Gate**: capture a profile of a dense combat frame and confirm
`CombatAoeVfxDispatchSystem.OnUpdate` / `DrainAndDispatch` is a meaningful main-thread cost. If it
is not, **close this task unimplemented**. The plan's value is delivered by 001–003.

## Problem (if the gate passes)

The drain is O(events) on the main thread (`CombatVfxRoot.cs:62-68`):

```csharp
while (queue.TryDequeue(out AoeVfxSpawnRequest p))
    if (dispatcher.StageAoeSpawn(p.TypeId, p.Trigger, p.Position, p.AreaSize))
        acceptedSpawnCount++;
```

Per event: a dequeue, a dictionary lookup, a bounds branch, two list appends — all main-thread,
all managed. At high event counts this is main-thread work that could move into Burst.

## Change

Keep the queue, the payload, and every producer **exactly as they are** (`NativeQueue` is the
documented lane-sink shape — `Docs/coding-standards.md:224-249`). Replace only the drain:

1. **Native flat routing cache on the singleton**: `NativeList<int> EndpointIdByKey`
   (`-1` = unrouted), indexed by the existing `KeyFor(typeId, trigger)`. The managed
   `endpointIdByKey` dictionary from 001 remains the authoritative route map through 003.
   Registration/removal increments a managed route revision. After `ProducerHandle.Complete()` and
   `ApplyPendingRemovals()`, the dispatch system asks `CombatVfxRoot` to refresh the native table
   only when that revision changed. Refresh grows as needed, fills holes with `-1`, and copies the
   current routes. The dispatch system owns and disposes the native cache; its singleton field gets
   an updated `// ECS Lifecycle:` comment per `Docs/coding-standards.md:251-265`.
2. **Race-free Burst counting sort** after route synchronization:
   - drain the queue into a persistent `NativeList<AoeVfxSpawnRequest>` in a single-thread Burst
     job;
   - clear persistent `counts`, `offsets`, and `writeCursors` arrays;
   - **count** pass: resolve each request through the native table, increment
     `counts[endpointId]`, and skip missing/out-of-range/tombstoned routes;
   - **prefix sum** over endpoints;
   - resize both typed output lists to the total accepted count without clearing their retained
     high-water capacities;
   - **scatter** in a single-thread Burst job using `writeCursors[endpointId]++`. Write payload
     fields into two persistent structure-of-arrays outputs:
     `NativeList<float2> PositionsByEndpoint` and `NativeList<float> AreaSizesByEndpoint`.
     Both arrays use the same endpoint offsets and counts.

   Count, prefix, and scatter remain single-threaded deliberately: this is race-free, needs no
   atomics or unsafe ref access, and still removes managed per-event work. Parallel scatter is a
   separate optimization only if a second profile shows these Burst jobs are themselves hot.
3. **Upload**: for each endpoint with a non-empty range, first grow that endpoint's own buffers
   using 002's transactional growth path, then upload the matching typed ranges:
   - `PositionBuffer.SetData(PositionsByEndpoint.AsArray(), offset, 0, count)`;
   - `AreaSizeBuffer.SetData(AreaSizesByEndpoint.AsArray(), offset, 0, count)`.

   This preserves the existing `float2` and `float` GPU strides. Never upload
   `AoeVfxSpawnRequest` directly into either buffer. The per-endpoint staging `NativeList`s from
   001/002 disappear; the endpoint keeps only its own buffers and capacity.

   **Buffers stay per-endpoint.** The two contiguous scatter outputs are transient CPU staging
   arrays feeding each endpoint's own `SetData`; they are not shared GPU buffers. Per the user directive
   (index Constraints), one buffer set corresponds to one graph and is never shared. Do not use
   the contiguous ranges as an excuse to collapse endpoints onto a single `GraphicsBuffer` with
   per-endpoint offsets — that scheme is rejected.

Single-thread scatter preserves the already-nondeterministic drained order within each endpoint.
Nothing may depend on that order — explicitly guaranteed by `Docs/coding-standards.md:224-232`.

## Why not the rejected alternative

Do **not** "solve" this by having producers write per-endpoint containers directly. That requires
either nested native containers (unsupported safely) or shared route components + per-endpoint job
scheduling — which reintroduces per-spawn structural changes, fragments chunks for unrelated
systems, and forces gameplay jobs to be scheduled per VFX endpoint. See the index rationale. One
Burst pass over a flat array is far cheaper than any of that.

## Invariants respected

- **No unsafe** (`Docs/coding-standards.md:95`): counting sort uses typed native containers and
  single-thread cursors, with no pointers or atomics.
- **No per-frame alloc** (`:282`): the drained list, counts, offsets, cursors, and both typed output
  arrays are persistent and grown to a high-water mark, matching 002's policy.
- **Lane singleton** (`:169-249`): the queue and `ProducerHandle` stay where they are; the new
  native route cache lives on the same singleton and is refreshed from the authoritative managed
  map only at the safe point above. Nested singleton containers do **not** auto-chain
  dependencies — the new jobs must thread handles manually, exactly as the existing
  `ProducerHandle` does (`AoePulseVfxSystem.cs:40-44`).
- **No structural changes / no archetype growth**: routing stays a flat side table; no component
  is added to any combat entity.

## Acceptance criteria

- Main-thread time in `DrainAndDispatch` drops measurably versus the captured baseline. **If it
  does not, revert** — this task has no other justification.
- Visual output is identical to 003's: same events, same endpoints, same counts.
- Known sentinel positions and area sizes arrive in their matching graph buffers without stride or
  field corruption.
- Unrouted keys (`-1`) are skipped and counted as dropped exactly as before.
- Zero per-frame allocation in the new jobs at steady state.
- No job-safety errors with the safety system enabled; handles thread correctly from producers
  through bucketing to upload.

## Verification

Harness cannot run Unity. User-run verification:

1. **Before**: capture a profile of a dense frame; record `DrainAndDispatch` main-thread ms.
2. Implement; capture the same scene again; compare. A non-improvement means close/revert.
3. Confirm visuals are unchanged side-by-side with the 003 build.
4. Run with the job safety system on; confirm no dependency errors.

## Dependencies

**Depends on 001** (needs `endpointId` and the routing map). Best after 002 and 003 so the
endpoint record is settled before its staging lists are removed.

## Scope

Medium–large, and the only task touching ECS jobs. It is also the task with the weakest
justification — the gate at the top is real. If the drain does not profile hot, 001–003 already
deliver everything this plan set out to fix, and this task should be closed unimplemented.
