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
all managed. At high event counts this is main-thread work that could be Burst + parallel.

## Change

Keep the queue, the payload, and every producer **exactly as they are** (`NativeQueue` is the
documented lane-sink shape — `Docs/coding-standards.md:224-249`). Replace only the drain:

1. **Flat routing table on the singleton**: `NativeList<int> EndpointIdByKey` (`-1` = unrouted),
   indexed by the existing `KeyFor(typeId, trigger)`. Written on the main thread at registration;
   read from Burst. Carries an `// ECS Lifecycle:` comment per `Docs/coding-standards.md:251-265`.
2. **Burst counting sort** after `ProducerHandle.Complete()`:
   - drain the queue into a persistent `NativeList<AoeVfxSpawnRequest>` (single-threaded, Burst);
   - **count** pass: per event, resolve `endpointId` from the flat table, increment
     `counts[endpointId]`; skip `-1`;
   - **prefix sum** over endpoints (small N, single-threaded);
   - **scatter** pass (parallel): write each event to `out[offset[endpointId] + localIndex]`.
   Result: one contiguous `NativeArray` where every endpoint owns a range.
3. **Upload**: per endpoint with a non-empty range, `SetData(array, srcStart: offset, dstStart: 0,
   count)` straight from its range **into that endpoint's own buffers** — no dictionary lookup, no
   per-event branch, no staging copy. The per-endpoint staging `NativeList`s from 001/002
   disappear; the endpoint keeps only its buffers and capacity.

   **Buffers stay per-endpoint.** The contiguous scatter output is a *transient staging array*
   feeding each endpoint's own `SetData`; it is not a shared GPU buffer. Per the user directive
   (index Constraints), one buffer set corresponds to one graph and is never shared. Do not use
   the contiguous ranges as an excuse to collapse endpoints onto a single `GraphicsBuffer` with
   per-endpoint offsets — that scheme is rejected.

Ordering within an endpoint becomes scatter-order rather than dequeue-order. Both are already
non-deterministic and nothing may depend on either — explicitly guaranteed by
`Docs/coding-standards.md:224-232`.

## Why not the rejected alternative

Do **not** "solve" this by having producers write per-endpoint containers directly. That requires
either nested native containers (unsupported safely) or shared route components + per-endpoint job
scheduling — which reintroduces per-spawn structural changes, fragments chunks for unrelated
systems, and forces gameplay jobs to be scheduled per VFX endpoint. See the index rationale. One
Burst pass over a flat array is far cheaper than any of that.

## Invariants respected

- **No unsafe** (`Docs/coding-standards.md:95`): counting sort over typed `NativeArray<T>`;
  atomics via `Interlocked`/`NativeArray<int>` increments, no pointers.
- **No per-frame alloc** (`:282`): the drained list, counts, offsets, and output array are all
  persistent and grown to a high-water mark, matching 002's policy.
- **Lane singleton** (`:169-249`): the queue and `ProducerHandle` stay where they are; the new
  table lives on the same singleton. Nested singleton containers do **not** auto-chain
  dependencies — the new jobs must thread handles manually, exactly as the existing
  `ProducerHandle` does (`AoePulseVfxSystem.cs:40-44`).
- **No structural changes / no archetype growth**: routing stays a flat side table; no component
  is added to any combat entity.

## Acceptance criteria

- Main-thread time in `DrainAndDispatch` drops measurably versus the captured baseline. **If it
  does not, revert** — this task has no other justification.
- Visual output is identical to 003's: same events, same endpoints, same counts.
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
