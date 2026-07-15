# VFX Endpoint Routing

## Summary

Today `CombatAoeVfxDispatcher` owns one `VisualEffect` GameObject + one `GraphicsBuffer` set
per **`(typeId, AoeVfxTrigger)`** pair (`CombatAoeVfxDispatcher.cs:181` `KeyFor`). Neither
`typeId` nor `trigger` is used anywhere in the actual upload — `StageAoeSpawn` consumes only
`Position` + `AreaSize` (`:163`), and `Dispatch()` always fires the same constant `"OnSpawn"`
event with the same property names (`:186`). The pair is used **only** as a dictionary key.

Consequence observed in play mode: one authored `VisualEffectAsset` assigned to several
trigger slots (or reused across AOE types) produces **one child GameObject per slot**, all
running the identical graph, each with its own duplicate buffers. `SkillDriver` registers up
to 5 slots per AOE type (`SkillDriver.cs:632-636`).

This plan makes the **`VisualEffectAsset` the identity of a VFX endpoint**. `(typeId, trigger)`
becomes pure *authoring-time routing* that resolves to a dense `endpointId`. Instance count
collapses to the true cardinality: **one per distinct graph actually in use**.

It also removes the `maxPerFrame` **drop**, which is not a budget — it is the fixed size of a
`GraphicsBuffer` allocated once at `Register` (`:112`) and never grown, with an overflow drop
bolted on (`:171`). The intent (do not reallocate a GPU buffer every tick) is preserved by
switching from *one fixed chunk then drop* to *grow by chunks to a high-water mark*.

Resulting path:

```text
producers (UNCHANGED)
  -> NativeQueue<AoeVfxSpawnRequest> on CombatAoeVfxDispatchSingleton (UNCHANGED)
  -> drain resolves (TypeId, Trigger) -> endpointId via managed route map
     (optional 004 replaces hot lookup with a synchronized native flat cache)
  -> endpoint record: staging list -> grown GraphicsBuffer -> VisualEffect.SendEvent
```

## Rationale for major architectural decisions

### Endpoint identity is a dense int + record, NOT an ECS entity

An endpoint needs an identity that survives its resources being swapped (list, buffer, graph
instance, capacity). It does **not** need to be an entity. An endpoint entity would only be
required if Burst jobs had to write per-endpoint containers directly — which this plan
explicitly rejects (below). Endpoints are presentation-side resource records addressed by a
dense `endpointId`, consistent with the standing directive that VFX kind must not become an
entity component or archetype (user directive; see Constraints).

### One endpoint owns one buffer set — buffers are never shared between graphs

**User directive.** A set of `GraphicsBuffer`s corresponds to exactly one graph. Endpoints must
not share buffers with each other, and no "one buffer per shape with per-endpoint offsets"
scheme may be introduced. This is a hard boundary on every task in this plan, including 004.

This follows the grain of the design rather than fighting it: the endpoint is the single owner of
its graph's list, buffer, capacity, and lifecycle. Shared buffers would split that ownership
across endpoints, reintroduce a cross-endpoint capacity coupling (the exact ambiguity 002
removes), and make an endpoint's resources un-swappable in isolation — breaking the stable-identity
property 003 depends on.

### Producers, the request struct, and the lane queue are NOT touched

An alternative design was considered and **rejected**: give producing entities a shared route
component per endpoint, and have each shape dispatcher schedule one endpoint-filtered producer
job per active endpoint, writing directly into a per-endpoint `NativeList` — so no event
carries a graph id and no bucketing pass exists.

It is rejected for concrete, codebase-specific reasons:

1. **It reintroduces per-spawn structural changes.** `ISharedComponentData` value changes are
   structural (chunk move), main-thread/ECB only, never writable from a Burst worker job. The
   spawn pools reuse entities across `typeId` within an archetype, so a reused AOE routinely
   comes back as a different `typeId` — a different endpoint — so the route would change on
   essentially every reuse. The warm-pool work exists specifically to reach **zero structural
   changes per tick**; this would undo it and force an ECB `SetSharedComponent` per spawn.
2. **It fragments chunks for unrelated systems.** Distinct shared values fragment chunks for
   *every* query over those entities — collision, lifetime, render prepare — not just VFX. With
   multiple route roles (impact/expiry/trail) chunk count becomes the cartesian product of route
   values.
3. **It inverts the layer dependency.** `CombatLifetimeSystem` and the collision systems emit
   VFX as a *side effect* of jobs doing gameplay work. Per-endpoint filtering forces those
   gameplay jobs to be scheduled once per VFX endpoint, or forces a second full pass over the
   same entities. Presentation would dictate simulation scheduling, violating the layer
   boundary (`Docs/flows/vfx-dispatch.md:35-38`).
4. **It reintroduces a hard cap — the exact defect this plan removes.**
   `NativeList<T>.ParallelWriter.AddNoResize` cannot grow while jobs run, so capacity must be
   pre-sized and overflow throws. `NativeQueue<T>.ParallelWriter` grows by claiming per-thread
   blocks with no pre-sizing — which is precisely why the lane uses one, and is documented as a
   deliberate choice (`Docs/coding-standards.md:224-249`).

The good ideas from that design are kept: **stable endpoint identity with swappable resources**,
**shape is a property of the graph**, and **runtime endpoint creation/removal**. Only the
shared-route + per-endpoint-scheduling mechanism is dropped.

### Staging stays main-thread, so there is no capacity constraint at all

`StageAoeSpawn` is called from the main-thread drain (`CombatVfxRoot.cs:62`). A main-thread
`NativeList.Add` grows amortized on its own. The `AddNoResize` pre-sizing problem simply does
not exist on this path — deleting the drop check is sufficient.

## Constraints & invariants (source)

- **No `unsafe` in gameplay or shared runtime** — `Docs/coding-standards.md:95`. All staging
  stays typed `NativeList<T>` + `GraphicsBuffer.SetData<T>`.
- **Lane singleton, no cross-system field reach-in** — `Docs/coding-standards.md:169-249`. The
  queue and `ProducerHandle` stay on `CombatAoeVfxDispatchSingleton`; producers combine handles;
  the dispatch system completes before draining. Unchanged by this plan.
- **`NativeQueue` is the deliberate lane-sink shape** — `Docs/coding-standards.md:224-249`.
  Many disjoint producers append to one shared container, drained once by the owner. Do not
  replace with `NativeList`/`NativeStream`. Ordering is explicitly meaningless; sinks must treat
  contents as an unordered set.
- **VFX dispatch is a named hot path** — `Docs/coding-standards.md:282`. No per-frame alloc,
  LINQ, or closures in drain/dispatch. Buffer growth must be amortized, not per-tick.
- **Native/GPU handle ownership** — `Docs/coding-standards.md:292-313`. Every `GraphicsBuffer`
  released on every teardown path, including the partial-alloc rollback in `Register`
  (`CombatAoeVfxDispatcher.cs:126-137`). Growth allocates and binds a complete replacement pair
  before releasing the old pair; partial replacements roll back without damaging the endpoint.
- **VFX is a visual-only lane** — `Docs/coding-standards.md:150-167`. `AoeVfxSpawnRequest`
  carries no gameplay authority. Dropping is allowed but must not be *accidental*.
- **No per-vfx-kind entity component / archetype** — user directive. One entity emits many vfx
  kinds across triggers; a component-per-kind would fork archetypes into many tiny chunks. The
  `(TypeId, Trigger) -> ...` mapping lives in a presentation-side route map, not on entities.
  Tasks 001-003 use a managed dictionary; optional 004 adds a synchronized native flat cache.
- **One buffer set per graph; never shared between endpoints** — user directive. No shared
  buffer + per-endpoint offset scheme, in any task.
- **ECS lifecycle comments** — `Docs/coding-standards.md:251-265`. Any changed singleton field
  carries an updated `// ECS Lifecycle:` comment.
- **Zero structural changes per tick in the warm-pool spawn path** — established by the
  spawn-pool top-up work. This plan adds no components to combat entities at all.

## Mechanisms reused vs. introduced

Reused (conform, do not fork):

- `CombatAoeVfxDispatchSingleton` + `ProducerHandle` threading — untouched.
- `NativeQueue<AoeVfxSpawnRequest>` lane sink and `while (TryDequeue)` drain shape — untouched.
- `AoeVfxSpawnRequest` struct — **unchanged**, still carries `TypeId` + `Trigger`.
- All producer systems — **unchanged**.
- Flat key `typeId * 256 + trigger` (`KeyFor`) — retained as the routing key.
- `Register` validate + try/catch partial-alloc rollback shape.
- `Dispatch()` SetData/SetGraphicsBuffer/SetInt/SendEvent flow.

Introduced (justified):

- **`endpointId` (dense int) + endpoint record** — replaces the `(typeId, trigger)` dictionary
  key with graph identity. Removes duplicate instances; required by the observed bug.
- **`endpointIdByKey` managed route map** (`key -> endpointId`, absent = no visual) — the
  authoritative mapping for 001-003 under the no-archetype rule. Measurement-gated 004 may mirror
  it into an owned native flat cache (`-1` = no visual) for Burst lookup.
- **Chunked growth state on the endpoint record** (capacity high-water mark) — replaces the
  fixed `maxPerFrame` field.

## Minimal/additive vs. refactor comparison

- **Minimal/additive** (keep `(typeId, trigger)` keying, dedupe by comparing assets at
  `Register` and aliasing duplicate keys to a shared record):
  - resulting data flow: two representations of "which instance" — the key dictionary *and* an
    alias table pointing into shared records.
  - new concepts/types: an alias/indirection layer over the existing dictionary.
  - copies/translations added: one extra hop per stage call; `maxPerFrame` still per key but now
    ambiguously shared.
  - long-term cost: **structural warning** — duplicate ownership of instance identity, unclear
    source of truth for capacity, adapter code existing only to avoid re-keying.
- **Refactor** (re-key the pool by asset; `(typeId, trigger)` becomes routing only):
  - resulting data flow: one dictionary keyed by graph identity; one authoritative route map.
  - existing concepts/types changed: `KeyFor` demoted from identity to routing key;
    `AoeVfxTypeResources` becomes an endpoint record; `maxPerFrame` replaced by growth state.
  - copies/translations removed: the duplicate instance/buffer sets; the ambiguous per-key cap.
  - long-term benefit: instance count equals true graph cardinality; capacity is a per-graph GPU
    property with one owner; runtime endpoint creation becomes natural.
- **Decision**: **choose refactor**. Reason: the additive path creates duplicate ownership of
  identity and an unclear source of truth for capacity — exactly the structural warning the
  workflow says to treat as disqualifying. The refactor target is clear and touches one managed
  class plus one main-thread table; producers, queue, and payload are untouched, so simulation
  risk is nil.

## Default decision rule note

`endpointId` becomes the single source of truth for "which live VFX instance does this request
reach". `(typeId, trigger)` is authoring-time routing only and must not be re-derived downstream.
The endpoint record is the single owner of that graph's list, buffer, capacity, and lifecycle —
and that ownership is exclusive, which is why buffers are never shared between endpoints.

**Future multi-contract note**: data shape is a property of the **graph** (one graph = one
contract), so when a second VFX data contract is wanted, the endpoint record owns its own shape.
No `key -> dataType` side table is needed — `endpointIdByKey` already resolves to the endpoint,
and the endpoint knows its contract. Any future contract work should build on that rather than
reintroducing a parallel mapping.

## Design validation vs. invariants

- **No unsafe**: staging remains `NativeList<float2>/<float>` + `SetData<T>`. PASS.
- **Lane singleton / handle threading**: not touched; producers and queue unchanged. PASS.
- **`NativeQueue` sink shape preserved**: still one shared queue, many producers, one drain,
  unordered. PASS.
- **No archetype growth / no structural changes**: no component added to any combat entity;
  routing lives in a managed side map read on the main thread. PASS — and strictly better than the
  rejected shared-route design.
- **Hot-path alloc**: staging lists persistent and cleared per frame; buffer growth only on
  crossing a chunk boundary, never shrinks, so steady state allocates nothing. PASS.
- **Ownership**: growth transactionally allocates and binds both replacements before releasing the
  old pair; allocation/rebind failures roll back partial replacements. `Dispose` and `Register`
  rollback cover every endpoint buffer. PASS.
- **Visual-only**: payload unchanged. PASS.
- **Accidental drops removed**: capacity chases the maximum per-endpoint event count seen in one
  tick. Unbounded retained high-water memory is accepted temporary debt; explicit budget/fallback
  work is deferred and recorded below. Allocation failure safely drops only the batch that cannot
  fit while preserving the old endpoint buffers. PASS WITH ACCEPTED DEBT.

## Tasks

- `001-endpoint-identity.md` — re-key the dispatcher pool by `VisualEffectAsset`; add the
  authoritative managed route map; `Register` reuses an existing endpoint when the asset already
  has one. Fixes the duplicate GameObjects. **Behavior-identical otherwise.**
- `002-chunked-buffer-growth.md` — delete the `maxPerFrame` drop; grow `GraphicsBuffer` by chunks
  to a high-water mark and rebind after realloc. Removes the artificial cap.
- `003-runtime-endpoint-lifecycle.md` — allow endpoint creation/removal at runtime rather than
  only at `SkillDriver` compile; staged removal; resources swappable behind a stable `endpointId`.
- `004-burst-bucketing.md` — **measurement-gated.** Move the O(events) managed drain into
  race-free single-thread Burst passes using a synchronized native route cache and typed position/
  area-size ranges. Only if profiling shows the drain matters.
- `005-explicit-vfx-budget.md` — **deferred accepted debt.** After 002 ships unbounded growth,
  define an intentional event/byte budget, fallback policy, and diagnostics without coupling the
  budget to buffer chunk size.

001 and 002 are independent and each independently shippable. 003 depends on 001. 004 depends on
001 and should not start without a profile capture justifying it. Deferred 005 depends on 002 and
representative memory/event profile captures.

## Open questions / considerations

- **Superseded plan.** `.agent/vfx-new-contract-path/` was deleted in favour of this plan (user
  decision). It is recoverable from git at `f24bf1f8` if any of it is wanted back. Note what went
  with it: that plan's actual *feature* was a second **Area-Timed** contract
  (`Positions + AreaSizes + Durations + TickIntervals`) letting a stationary lingering AOE
  **self-pulse on the GPU** instead of being driven per-tick by `AoePulseVfxSystem`. **This plan
  does not deliver that** — it is identity/ownership/capacity work only. If GPU self-pulse is
  still wanted, it should be re-planned on top of endpoints, where it is simpler: the endpoint
  owns its contract, so it becomes "an endpoint of a different shape" with no side table.
- **Chunk size.** 2048 is the current fixed size and is proposed as the growth chunk. Arbitrary
  but harmless — it only sets realloc granularity now, not a cap.
- **Accepted unbounded-growth debt.** Until deferred 005, each endpoint permanently retains buffer
  and staging capacity sized for its largest event count in any one tick. This is an explicit user
  decision for the current implementation, not a claim that GPU memory is a sufficient long-term
  budget or fallback.
- **Doc drift**: `Docs/coding-standards.md:162` still references `AoeVfxSpawnRequestElement`
  (the removed `DynamicBuffer` element from the flush-removal work). Fix opportunistically.
- **Naming debt**: the `AoeVfx*` prefix is already inaccurate — dispatch is source-agnostic and
  nothing in the upload is AOE-specific. Renaming is out of scope here; recorded as debt.
