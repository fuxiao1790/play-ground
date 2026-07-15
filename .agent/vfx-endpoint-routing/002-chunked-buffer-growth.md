# 002 — Chunked buffer growth replaces the maxPerFrame drop

## Problem

`maxPerFrame` is not a visual budget. It is the fixed element count of the `GraphicsBuffer`
allocated once at `Register` and never resized (`CombatAoeVfxDispatcher.cs:112-121`):

```csharp
res.PositionBuffer = new GraphicsBuffer(Structured, maxPerFrame, sizeof(float) * 2);
res.Staging        = new NativeList<float2>(maxPerFrame, Allocator.Persistent);
```

with an overflow drop bolted on (`:171-174`):

```csharp
if (res.Staging.Length >= res.MaxPerFrame) return false;
```

The intent is correct — do not reallocate a GPU buffer every tick. The implementation turns that
into *one fixed chunk, then silently discard*. Requests past 2048 in a frame vanish for no reason
other than the buffer being the size it was born at.

`Docs/reference/simulation/vfx-system.md:181-183` documents the cap as intentional
budget-dropping. That framing is wrong and the doc must be corrected with this change.

## Key enabler

Staging happens on the **main thread** — `StageAoeSpawn` is called from the drain loop in
`CombatVfxRoot.DrainAndDispatch` (`CombatVfxRoot.cs:62`). A main-thread `NativeList.Add` grows
amortized on its own. There is no `ParallelWriter`, so no `AddNoResize` pre-sizing constraint
applies. Deleting the drop check is safe as-is.

## Change

1. Remove `MaxPerFrame` from the endpoint record; add `int BufferCapacity` (current allocated
   element count of the GPU buffers).
2. Add `const int GrowthChunk = 2048` (the old default; now realloc granularity, not a ceiling).
3. `Register`: allocate buffers at `GrowthChunk` and set `BufferCapacity = GrowthChunk`.
   Staging lists get `GrowthChunk` initial capacity. Drop the `maxPerFrame` parameter from
   `Register` on both `CombatAoeVfxDispatcher` and `CombatVfxRoot` (`CombatVfxRoot.cs:43`) — no
   call site passes a non-default value.
4. `StageAoeSpawn`: **delete the cap check**. Always `Add`. Still returns `bool` (now `false`
   only on an unrouted key from 001), so the accepted-count/stats path is unchanged.
5. `Dispatch()`, before `SetData` for an endpoint with `Staging.Length > BufferCapacity`:
   - compute `newCapacity = RoundUpToMultiple(Staging.Length, GrowthChunk)`;
   - allocate both replacement buffers into local variables while the old pair remains live;
   - if either allocation fails, release any partial replacement, keep the old pair bound and
     owned, log the allocation failure, discard this endpoint's staged batch, and continue. The
     endpoint remains usable on later frames within its previous capacity;
   - after both allocations succeed, swap and bind the pair as one transaction. If rebinding
     throws, restore the old pair/bindings and release both replacements;
   - only after successful rebinding, set `BufferCapacity = newCapacity` and `Release()` the old
     pair. No live handle is destroyed before its replacement is known-good;
   - **rebind**: the `VisualEffect` holds a reference to the old buffer, so
     `SetGraphicsBuffer(...)` must be called with the new instances. `Dispatch()` already calls
     `SetGraphicsBuffer` every frame (`:199`, `:201`), so rebinding is automatic — but this must
     be stated so a future "only bind once" optimization does not silently break realloc.
   - **never shrink**: growth is a high-water mark.

## Invariants respected

- **Hot path, no per-frame alloc** (`Docs/coding-standards.md:282`, `:288-290`): steady state
  allocates nothing. A realloc fires only on the frame that first crosses a chunk boundary at
  that level, then never again. This is strictly the same amortization the fixed buffer had, with
  the ceiling removed.
- **GPU handle ownership** (`Docs/coding-standards.md:292-313`): growth is a two-phase swap.
  Partial replacements are released on failure; the old pair remains valid until both replacements
  are allocated and bound; `Dispose` and the `Register` rollback still cover the current buffers.
- **No unsafe** (`:95`): still typed `SetData<T>`.

## What honestly remains a ceiling

After this change the only limits are real ones, and neither is ours to invent:

- the particle **Capacity** authored inside the `.vfx` asset (the graph clamps itself);
- GPU memory.

If a deliberate spawn budget is ever wanted, it becomes an explicit, named, logged decision —
not a silent side effect of a buffer size.

### Accepted temporary debt: unbounded high-water mark

Per user decision, this task intentionally ships without an application-level VFX event or byte
budget. The permanent high-water allocation for an endpoint is therefore determined by the
largest number of events routed to that endpoint in any one tick. A pathological tick can retain a
large staging capacity and GPU buffer pair for the rest of the endpoint's lifetime.

This is accepted for now so the accidental 2048-event drop can be removed. Follow-up work must add
an explicit event/byte budget, fallback policy, and dropped-event diagnostics before content stress
or release hardening. That later budget must be independent of growth chunk size. Allocation
failure remains an honest emergency fallback: keep the previous buffers and drop only the batch
that cannot fit.

## Acceptance criteria

- When replacement allocation succeeds, staging more than 2048 events for one endpoint in a frame
  drops **nothing**; all of them upload and spawn (subject only to the graph's authored Capacity).
- The buffer grows to the next multiple of 2048 and **stays** there; a later quiet frame does not
  shrink or realloc.
- A steady-state frame under the high-water mark performs **zero** GPU allocations — verify no
  per-frame `GraphicsBuffer` construction in the profiler.
- No leak: after growth, the previous `GraphicsBuffer` is released. Play-mode enter/exit cycles
  do not accumulate GPU buffers.
- Forced replacement-allocation failure leaves the old buffers usable and releases every partial
  replacement; it never leaves an endpoint bound to a released handle.
- The documented high-water mark matches the maximum per-endpoint event count observed in one
  tick and never shrinks during that endpoint's lifetime.
- `maxPerFrame` is gone from `CombatVfxRoot.Register` and `CombatAoeVfxDispatcher.Register`
  signatures; no call site breaks.
- `Docs/reference/simulation/vfx-system.md` (`:181-183`), `Docs/contracts/vfx-requests.md`
  (`:39-42` Guarantees), and `Docs/flows/vfx-dispatch.md` (`:45-48` Failure/Edge Cases) updated:
  events are no longer dropped at an artificial cap.

## Verification

Harness cannot run Unity. User-run play-mode verification:

1. Drive a scene that exceeds 2048 events/frame on one endpoint (dense AOE hit pulses).
2. Confirm particles no longer visibly truncate at the old cap.
3. Profile a steady busy frame: no `GraphicsBuffer` allocation should appear.
4. Watch the growth log (add a one-line `Debug.Log` on realloc, gated to development builds) to
   confirm it fires on crossing a boundary and then stops.

## Dependencies

Independent of 001 in principle, but both edit the same endpoint record. **Land 001 first** to
avoid a conflicting rewrite of the same class. Note the interaction: once endpoints are shared by
asset (001), the per-slot `maxPerFrame` becomes ambiguous — 002 deletes the field and dissolves
that ambiguity, which is why they are sequenced.

## Scope

Small. One managed class plus three doc updates. No ECS, no jobs, no producers.
