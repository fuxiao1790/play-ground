# VFX Rework — Schema-Driven Per-Type Payloads

## Summary

Today every VFX event is one struct (`VfxPendingSpawn` = TypeId+Trigger+Position+AreaSize)
on one shared `NativeQueue`, and every registered graph (`VfxTypeResources`) hardcodes
the same two GPU buffers (`Positions` + `AreaSizes`) + `SpawnCount`, uploaded every frame.
This forces **all** VFX graphs to share one buffer contract: projectiles waste an
`AreaSizes` buffer they never read, and no graph can declare *different* GPU data.

This rework makes each effect's GPU payload **data**, not a hardcoded code path:

- Each payload shape is a plain unmanaged **payload struct** declaring exactly the GPU
  fields it needs, with a matching **schema** (`{ propertyName, byteOffset, stride }[]`).
- Producers pack their payload into **one generic** transient event
  (`VfxEvent { TypeId, VfxTrigger Trigger, DataType, <fixed payload blob> }`) and enqueue it
  into **one** MT container. No per-type queue. The trigger is a `VfxTrigger` enum
  (`Spawn/Hit/Expire/Pulse/Arming`) — the raw `0..4` trigger bytes are gone.
- The managed dispatcher is **one generic path**: for each drained event it routes by
  `(TypeId,Trigger)` to a graph, and stages/uploads that graph's buffers by looping its
  **schema** — no `switch` on data type, no class-per-effect.

Initial payload set (confirmed with user):

| DataType   | Payload fields                              | GPU buffers                                  | Emitted by |
|------------|---------------------------------------------|----------------------------------------------|------------|
| `Point`    | `float2 Position`                           | `Positions`                                  | projectiles |
| `Area`     | `+ float AreaSize`                          | `+ AreaSizes`                                | AOEs (spawn/hit/expire/arming) |
| `AreaTimed`| `+ float Duration, float TickRate`          | `+ Durations, TickRates`                     | lingering-AOE pulse (new) |

`AreaTimed` is the new type proving the framework end-to-end: pulse VFX already has the
tick interval (`AoePulseVfxComponent.Interval` → `TickRate`) and the field's remaining
lifetime (`CombatLifetimeComponent.Remaining` → `Duration`).

## Why this shape (the user's directive)

The user rejected the first plan because it created "a different archetype per vfx
effect" — a distinct managed class + queue + `switch(kind)` per type. This design has:

- **No per-effect ECS archetype** — VFX events stay transient native payloads, never
  added to entities (unchanged from today).
- **No per-effect managed class** — one generic `VfxGraphInstance` driven by a schema.
- **No `switch` on data type** — drain/upload loop over schema fields (data).
- **No per-effect container** — one `NativeQueue<VfxEvent>`.

Adding a new effect type = define a payload struct + author its schema entry + have the
relevant producer pack it + register the graph with that `DataType`. Data only.

## Constraints & invariants (source)

- **Threading** — producers write through `AsParallelWriter()`; a single accumulated
  `ProducerHandle` on the singleton is `Complete()`d in presentation before the main-thread
  drain. IJobEntity producers that grab the writer from a singleton, **and** the two
  `AoeSpawnExpansionSystem` plain `IJob` producers, must thread `ProducerHandle` as an
  **input** dependency — this stays exactly as today. (Source: `CombatVfxDispatchSystem`,
  memory `reference_shared_queue_producer_chaining`.)
- **No hot-path allocation** — packing is a stack struct fill + `Enqueue` (no alloc);
  managed per-field staging lists are `Persistent` and reused/cleared per frame.
  (Source: `Docs/performance.md` "no per-entity allocations".)
- **Sim/visual separation + capped visuals** — dispatch stays in `PresentationSystemGroup`;
  each graph keeps a `MaxPerFrame` cap; excess events dropped. (Source: `Docs/performance.md`.)
- **Lifecycle/ownership** — the queue is owned/created/disposed by `CombatVfxDispatchSystem`;
  graphs + GraphicsBuffers are owned by `CombatVfxRoot`/`CombatVfxDispatcher`, disposed on
  teardown/`ResetDispatcher`. (Source: current `CombatVfxDispatchSystem`, `CombatVfxRoot`.)
- **Single CombatRoot invariant** — `(TypeId,Trigger)` keys are globally unique, so one
  faction-agnostic dispatcher has no routing collision. (Source: memory
  `project_vfx_faction_scope_removal`.)
- **No strict-aliasing UB** — pack/unpack of the payload blob uses `UnsafeUtility.MemCpy` /
  `CopyStructureToPtr`, never pointer recasts between element types. (Source: `ecs-notes.md`
  "strict aliasing violations = UB".)

## Mechanisms reused vs introduced

- **Reused**: the persistent-`NativeQueue` + `ParallelWriter` + `ProducerHandle` producer/
  consumer pattern (unchanged, just one queue of a generic element); the
  `MaxPerFrame`-capped staging→`SetData`→`SendEvent` upload; `(TypeId,Trigger)` routing key;
  contract validation against `asset.GetExposedProperties()`.
- **Introduced**: `VfxEvent` generic container + payload structs; a managed `VfxBufferSchema`
  table keyed by `DataType`; one generic `VfxGraphInstance` (replaces `VfxTypeResources`).
  Justification: these *remove* the hardcoded 2-buffer contract and collapse the would-be
  per-type code paths into one data-driven path.
- **Type ownership** (user directive): `VfxDataType`, `VfxTrigger`, `VfxGraphKey`, and the
  payload structs are **ECS/data-layer owned** (in `VfxEcsComponents.cs`) and are the single
  canonical types across producers, the singleton, **and** the managed `Register`/dispatcher —
  no managed mirror enum, no `byte`/`int` trigger args, no boundary conversion. The managed
  `VfxBufferSchema` (property names/strides) is the one managed-only piece, keyed by the ECS
  `VfxDataType`.

## Minimal/additive vs refactor comparison

- **Additive** (rejected — the first plan):
  - data flow: 3 typed queues → 3 drain branches → 3 managed graph classes.
  - new concepts: `VfxEventKind` enum + `switch`, per-type queue fields, class hierarchy.
  - copies: drain de-interleave.
  - long-term cost: adding a type edits 5 sites (struct + queue field + class + switch +
    registration); parallel per-type code paths to keep in sync — the "archetype per
    effect" the user rejected.
- **Refactor** (chosen):
  - data flow: 1 queue → 1 drain loop → 1 generic graph, all schema-driven.
  - types changed/removed: `VfxPendingSpawn` → generic `VfxEvent` + payload structs;
    `VfxTypeResources` → schema-driven `VfxGraphInstance`; `requireAreaSizeContract` bool →
    `DataType`.
  - copies: producer pack (1 `MemCpy`) + drain de-interleave (same as additive).
  - long-term benefit: one source of truth for "a vfx event"; adding a type is data only.
- **Decision: refactor.** Fewer runtime data paths, one source of truth, and it is the only
  option that satisfies the user's no-switch / no-per-effect-archetype directive. The extra
  producer-side pack is one `MemCpy` on the capped, low-volume presentation path.

## Design validation vs invariants

- *Threading*: one `NativeQueue<VfxEvent>` + one `ProducerHandle`; every producer still
  combines its handle and the drain still `Complete()`s first → no change to job safety.
  The two `IJob` AOE producers keep `ProducerHandle` as input (pressure-tested against
  `reference_shared_queue_producer_chaining`).
- *Allocation*: pack is stackonly; staging persistent → no new hot-path allocs.
- *Aliasing*: blob pack/unpack via `MemCpy`/`CopyStructureToPtr`, no recast → no UB.
- *Capped visuals*: `MaxPerFrame` retained per graph; overflow dropped as today.
- *No new archetype*: events never touch entities → zero structural change.

## Open decisions / risks

1. **Container granularity** — chose ONE generic `NativeQueue<VfxEvent>` (user's later
   steer toward "no per-type / no switch") over the message-1 phrasing "one container per
   event type." If the user still wants one typed queue per type, the only change is the
   singleton fields + a typed drain per queue; the schema-driven graph side is unaffected.
2. **Payload blob size** — `VfxEvent` carries a fixed 32-byte payload blob (covers the
   4-field `AreaTimed` = 20B with headroom). A future larger payload bumps this constant.
   Trivial waste on a capped presentation path.
3. **`GraphicsBuffer.SetData` byte semantics** (impl risk) — per-field staging is
   `NativeList<byte>`; buffers are `Structured(MaxPerFrame, field.Stride)`. Verify the
   byte-wise `SetData` start-index/count units land contiguously per field during
   implementation; fall back to reinterpreting staging to the field element type if needed.

## Tasks

- `001-event-data-model.md` — generic `VfxEvent`, payload structs, `VfxDataType`, `Pack` helper.
- `002-singleton-single-queue.md` — collapse dispatch singleton to one `NativeQueue<VfxEvent>`.
- `003-schema-driven-dispatcher.md` — schema table + generic `VfxGraphInstance` + dispatcher + root.
- `004-producers-pack-events.md` — repoint all emit sites to pack `VfxEvent`; `CombatDeathUtility`.
- `005-registration-and-areatimed.md` — `SkillDriver` `DataType` registration + pulse `AreaTimed` wiring.

## Verification (user runs PlayMode — harness cannot build Unity)

1. `AoeSimulationTests` + `ProjectileCollisionSimulationTests` stay green (they only add
   `AoePulseVfxSystem` / assert `AoePulseVfxComponent`; they do not touch the queue).
2. Live scene: projectile spawn/hit/expire, AOE spawn/hit/expire/arming, and lingering
   pulse VFX all still fire.
3. Register a projectile graph exposing **only** `Positions` + `SpawnCount` (no `AreaSizes`):
   registers without error; no `AreaSizes` buffer allocated for it.
4. A pulse graph reading `Durations`/`TickRates` visibly responds (e.g. particle lifetime
   scaled by `Durations[id]`).
5. Profiler: no per-frame `AreaSizes` upload on projectile graphs; managed allocs/frame flat.
