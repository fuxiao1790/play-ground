# VFX Rework — Per-Type Payloads, Producer Switch, Schema-Driven Buffers

## Summary

Today every VFX event is one struct (`VfxPendingSpawn` = TypeId+Trigger+Position+AreaSize)
on one shared `NativeQueue`, and every registered graph (`VfxTypeResources`) hardcodes
the same two GPU buffers (`Positions` + `AreaSizes`) + `SpawnCount`, uploaded every frame.
This forces **all** VFX graphs to share one buffer contract: projectiles waste an
`AreaSizes` buffer they never read, and no graph can declare *different* GPU data.

This rework makes each effect's GPU buffer set **data**, not a hardcoded code path, and makes
the VFX pipeline agnostic to its trigger source (projectile vs AOE):

- **One payload struct + one queue per `VfxDataType`** — `VfxPointPayload` /`VfxAreaPayload`
  /`VfxAreaTimedPayload`, each embedding a `VfxGraphKey` (`TypeId` + `VfxTrigger` enum
  `Spawn/Hit/Expire/Arming` — the raw trigger bytes and the old per-tick `Pulse` are gone) and
  exactly its GPU fields. The singleton holds one `NativeQueue` per type.
- **Data type authored per effect** (option B, type safety) — each effect carries an explicit
  ECS-owned `VfxDataType`, **never** derived from projectile/AOE. Registration publishes it into
  a compact `NativeList<VfxDataType> DataTypeByKey` (indexed by `VfxKey.FlatIndex(typeId,trigger)`,
  default slot `None`).
- **Producer switches on the data type** — every producer reads what it has, looks up
  `dataType = DataTypeByKey[flatIndex]`, and `switch`es to build the matching payload and enqueue
  it into that type's queue. One shared `Emit` helper; no projectile/AOE branch anywhere.
- **Generic managed dispatch** — three typed drains route each payload by `Key` to its graph and
  stage it via that type's **schema** (`{ propertyName, payloadOffset, stride }[]`). One generic
  `VfxGraphInstance` owns exactly its schema's buffers — no class-per-effect, no runtime data-type
  `switch` on the managed side. Authored type is exact-match validated against the graph at
  registration.

Initial data-type set (confirmed with user):

| VfxDataType | Payload fields (after `Key`)        | GPU buffers                      |
|-------------|-------------------------------------|----------------------------------|
| `Point`     | `Position`                          | `Positions`                      |
| `Area`      | `+ AreaSize`                        | `+ AreaSizes`                    |
| `AreaTimed` | `+ Duration, TickRate`              | `+ Durations, TickRates`         |

Any effect on any source may author any type; the table is the buffer contract per type, not a
projectile/AOE rule. `AreaTimed` is the new type proving the framework **and** replaces the old
per-tick pulse: there is no `Pulse` trigger — a **lingering AOE authors its `Spawn` effect as
`AreaTimed`**, carrying `Duration` (active lifetime) + `TickRate`, and the graph self-pulses on the
GPU (valid because lingering AOEs are stationary, so one spawn event ≡ one event per tick).
`AoePulseVfxSystem` + `AoePulseVfxComponent` are deleted; **arming is unchanged** (task 006).

## Why this shape (the user's directives)

User corrections shaped this: (1) no managed **class-per-effect** ("different archetype per vfx
effect"); (2) the **producer** switches on the data type to build the payload and add it to that
type's list; (3) "vfx is vfx — no projectile/AOE difference"; (4) `VfxDataType` authored per
effect (type safety). Result:

- **No per-effect ECS archetype** — VFX payloads stay transient native, never on entities.
- **No per-effect managed class** — one generic `VfxGraphInstance` driven by a schema.
- **Per-type payload + queue** — matches "one native container per event type"; the switch that
  routes into them lives at the **producer**, on `VfxDataType` (never on source).
- **No managed data-type `switch`** — three typed drains + one generic graph.
- **No source distinction** — the sole per-effect variation is the authored `VfxDataType`.

Adding a new **data type**: `VfxDataType` value + payload struct + queue + schema entry + a
producer `switch` case + author it on the effect. Adding a **field** to an existing type: the
field + its schema entry + producers filling it.

## Constraints & invariants (source)

- **Threading** — producers write through `AsParallelWriter()`; a single accumulated
  `ProducerHandle` on the singleton is `Complete()`d in presentation before the main-thread
  drain. IJobEntity producers that grab the writer from a singleton, **and** the two
  `AoeSpawnExpansionSystem` plain `IJob` producers, must thread `ProducerHandle` as an
  **input** dependency — this stays exactly as today. (Source: `CombatVfxDispatchSystem`,
  memory `reference_shared_queue_producer_chaining`.)
- **No hot-path allocation** — building the event is a stack struct fill + `Enqueue` (no alloc);
  managed per-field staging lists are `Persistent` and reused/cleared per frame.
  (Source: `Docs/performance.md` "no per-entity allocations".)
- **Sim/visual separation + capped visuals** — dispatch stays in `PresentationSystemGroup`;
  each graph keeps a `MaxPerFrame` cap; excess events dropped. (Source: `Docs/performance.md`.)
- **Lifecycle/ownership** — the queue is owned/created/disposed by `CombatVfxDispatchSystem`;
  graphs + GraphicsBuffers are owned by `CombatVfxRoot`/`CombatVfxDispatcher`, disposed on
  teardown/`ResetDispatcher`. (Source: current `CombatVfxDispatchSystem`, `CombatVfxRoot`.)
- **Single CombatRoot invariant** — `VfxGraphKey (TypeId,Trigger)` is globally unique, so one
  faction-agnostic dispatcher has no routing collision. (Source: memory
  `project_vfx_faction_scope_removal`.)
- **Lookup write/read discipline** — `DataTypeByKey` is written only at registration (main
  thread, no jobs in flight) and read-only in producer jobs → no job-safety conflict. (Source:
  ECS job model.)
- **No strict-aliasing UB** — schema staging copies fields out of a payload struct by offset via
  `UnsafeUtility.MemCpy`, never pointer recasts between element types. (Source: `ecs-notes.md`
  "strict aliasing violations = UB".)

## Mechanisms reused vs introduced

- **Reused**: the persistent-`NativeQueue` + `ParallelWriter` + `ProducerHandle` producer/
  consumer pattern (now three typed queues); the `MaxPerFrame`-capped
  staging→`SetData`→`SendEvent` upload; `(TypeId,Trigger)` routing; contract validation against
  `asset.GetExposedProperties()`.
- **Introduced**: three per-type payload structs + queues; a `DataTypeByKey` lookup; a managed
  `VfxBufferSchema` table keyed by `VfxDataType`; one generic `VfxGraphInstance` (replaces
  `VfxTypeResources`); a serializable `VfxEffectBinding` `(asset, VfxDataType)` authoring pair.
  Justification: per-type payloads/queues keep each event minimal (message-1 "one container per
  event type"); the generic schema-driven graph avoids a class-per-effect.
- **Type ownership** (user directive): `VfxDataType`, `VfxTrigger`, `VfxGraphKey`, `VfxKey`, and
  the payload structs are **ECS/data-layer owned** (in `VfxEcsComponents.cs`) and are the single
  canonical types across producers, the singleton, the managed `Register`/dispatcher, **and** the
  authoring bindings — no managed mirror enum, no `byte`/`int` trigger args, no conversion. The
  managed `VfxBufferSchema` (property names/strides) is the one managed-only piece, keyed by the
  ECS `VfxDataType`.

## Minimal/additive vs refactor comparison

- **Additive** (rejected — the first plan):
  - data flow: 3 typed queues → 3 drain branches → 3 managed graph classes.
  - new concepts: `VfxEventKind` enum + `switch`, per-type queue fields, class hierarchy.
  - copies: drain de-interleave.
  - long-term cost: adding a type edits 5 sites (struct + queue field + class + switch +
    registration); parallel per-type code paths to keep in sync — the "archetype per
    effect" the user rejected.
- **Refactor** (chosen):
  - data flow: per-type payload/queue (minimal per event) → three typed drains → one generic
    schema-driven graph. Producer switches on the authored `VfxDataType` (looked up once).
  - types changed/removed: `VfxPendingSpawn` → three payload structs; `VfxTypeResources` →
    schema-driven `VfxGraphInstance`; `requireAreaSizeContract` bool → authored `VfxDataType`
    (`VfxEffectBinding`) + `DataTypeByKey` lookup.
  - copies: schema staging memcpy only (producers assign named fields — no pack copy).
  - long-term benefit: minimal per-event payloads; VFX ignores its trigger source; one generic
    graph class; adding a type is mostly data + one producer `switch` case.
- **Decision: refactor.** Per-type payloads/queues (message-1 model) + a generic schema-driven
  graph (no class-per-effect) + a producer switch on the authored type (no source distinction).
  Cost: three queues + a `DataTypeByKey` lookup read per event (cheap, capped path); authoring
  gains an explicit `VfxDataType` per effect (the deliberate type-safety trade). No job-splitting
  or `ComponentLookup` needed for the current type set (verified — see Open decisions #5).

## Design validation vs invariants

- *Threading*: three `NativeQueue`s share one `ProducerHandle`; every producer combines its
  handle and the drain `Complete()`s first → no change to job safety. Writers grabbed from the
  singleton don't auto-chain, so producers (incl. the two `IJob` AOE ones) keep threading
  `ProducerHandle` (pressure-tested against `reference_shared_queue_producer_chaining`).
- *Lookup*: `DataTypeByKey` written only at registration, read-only in jobs → safe.
- *Allocation*: `Emit` is stack-only + `Enqueue`; staging persistent → no new hot-path allocs.
- *Aliasing*: schema staging copies out of a payload struct by offset via `MemCpy`, no recast → no UB.
- *Capped visuals*: `MaxPerFrame` retained per graph; overflow dropped as today.
- *No new archetype*: payloads never touch entities → zero structural change.

## Open decisions / risks

1. **Per-type payloads/queues + producer switch (chosen)** — matches message-1 "one container
   per event type"; each event is minimal. The producer switches on the authored `VfxDataType`
   (option A lookup) to build+route. Managed side stays generic (no class-per-effect).
2. **Data-type lookup = flat `NativeList<VfxDataType>`** (option A) indexed by
   `VfxKey.FlatIndex`, grown at registration, `None` default → unregistered keys skip. O(1) read,
   no hashing, no per-entity storage.
3. **Authored `VfxDataType` (option B)** — explicit per effect, exact-match validated against the
   graph. Cost: a `VfxDataType` field per effect (`VfxEffectBinding`) + existing prefabs must set
   non-`Point` types; validation makes the migration self-guiding (task 005).
4. **`GraphicsBuffer.SetData` byte semantics** (impl risk) — per-field staging is
   `NativeList<byte>`; buffers are `Structured(MaxPerFrame, field.Stride)`. Verify byte-wise
   `SetData` index/count units land contiguously; else reinterpret staging to the field element type.
5. **Producer field availability** — ordinary emit sites pass `0` timing and read no timing
   component. `AreaTimed` rides only on the lingering `Spawn` event (task 006): the non-arming
   site reads Duration/TickRate from the `AoeSpawnCommand` (`IJob` over commands → no spanning); the
   arm-complete `AoeArmingJob` (unchanged/unsplit — arming stays as today) reads the lingering-only
   `CombatLifetimeComponent` + `AoeHitGateComponent` **optionally via `ComponentLookup`** (impact → 0,
   and its Spawn type is `Area`). No job splitting.

## Tasks

- `001-event-data-model.md` — per-type payload structs + ECS-owned `VfxDataType`/`VfxTrigger`/`VfxGraphKey`/`VfxKey`.
- `002-singleton-single-queue.md` — three typed queues + `DataTypeByKey` lookup in the singleton.
- `003-schema-driven-dispatcher.md` — schema table + generic `VfxGraphInstance` + three typed drains + root.
- `004-producers-pack-events.md` — producers look up the authored type, `switch`, enqueue via `Emit`; `CombatDeathUtility`.
- `005-registration-and-areatimed.md` — authored `VfxDataType` (`VfxEffectBinding`) + registration writes graph + lookup; pulse authored `AreaTimed`.
- `006-pulse-duration-not-per-tick.md` — delete per-tick `AoePulseVfxSystem`/`AoePulseVfxComponent` + `VfxTrigger.Pulse`; fold pulse into the lingering `Spawn` event (`Duration`+`TickRate`, `ComponentLookup` at arm-complete); arming unchanged; test updates.

## Verification (user runs PlayMode — harness cannot build Unity)

1. `AoeSimulationTests` + `ProjectileCollisionSimulationTests` compile after their
   `AoePulseVfxSystem`/`AoePulseVfxComponent` references are removed (task 006) and stay green;
   they don't touch the queue.
2. Live scene: projectile spawn/hit/expire, AOE spawn/hit/expire/arming still fire; a lingering
   AOE's single `Spawn` event drives a graph that self-pulses for its whole duration (not one burst
   per tick); the `Arming` telegraph is unchanged.
3. Register a projectile graph exposing **only** `Positions` + `SpawnCount` (no `AreaSizes`):
   registers without error; no `AreaSizes` buffer allocated for it.
4. The lingering `Spawn` graph receives non-zero `Durations`/`TickRates` and pulses over
   `Duration[id]` at `TickRate[id]`; an arming lingering AOE begins pulsing at arm-complete (its
   deferred Spawn), not during arming.
5. Profiler: no per-frame `AreaSizes` upload on projectile graphs; per-tick pulse emission gone;
   managed allocs/frame flat.
