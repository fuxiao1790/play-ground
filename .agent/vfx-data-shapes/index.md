# ECS-Owned VFX Data Shapes (Basic + Timed, one system)

## Summary

Today every AOE VFX event flows through **one** fixed payload
`AoeVfxSpawnRequest {VfxId, Position, AreaSize}`, one `NativeQueue`, one bucketing
job, dispatched by `CombatVfxRoot` + `CombatAoeVfxDispatcher`. That single payload
cannot express a whole-duration effect, so lingering "pulse" visuals are faked by
`AoePulseVfxSystem` re-emitting an instantaneous request every tick.

This change makes **the data shape a first-class, ECS-owned, per-graph concept**
instead of a single hard-coded payload. Shapes are named by their **data**, not by
AOE use-case:

- ECS defines a small closed set of **data shapes**. Each shape is a concrete struct
  whose fields map 1:1 to the exposed buffers a VFX graph consumes. v1:
  - `Basic` — buffers `Positions(float2)`, `AreaSizes(float)`. No timing fields.
  - `Timed` — `Positions(float2)`, `AreaSizes(float)`, `Durations(float)`,
    `TickIntervals(float)`. The timing fields live only here.
  Every shape also requires the common `SpawnCount(int)` + `OnSpawn` event.
- Each registered graph is **bound to exactly one shape**, chosen by the author.
  `CombatVfxRoot.Register(asset, shape)` **validates on registration** that the graph
  exposes exactly that shape's buffers (correct names + types) and **hard-fails** on
  anything missing, mistyped, *or unexpectedly extra*. The author is never forced to
  carry fields a graph does not use — an arming graph can be `Basic` even on a
  lingering AOE.
- **One graph has exactly one shape, so the shape is encoded into the `VfxId`.**
  `Register` allocates ids as `EncodeId(shape, localIndex)` (shape in the high bits,
  a per-shape dense index in the low bits, `0` still the no-VFX sentinel). Any Burst
  job can recover a graph's shape from its id via `DecodeShape(id)` with no side
  table and no managed lookup — so `AoeVfxIds` stays 5 plain `int`s and no
  `vfxIds.*Id` read changes.
- Because the shape is per-graph, **impact and lingering AOEs are NOT split into
  separate systems.** One combined VFX system dispatches all shapes through the
  **same bridge and the same `CombatVfxRoot` GameObject**. A graph carries timing
  only if it is bound to `Timed`; such a graph is emitted **once** and self-drives
  its ticks over its `Duration`.

The old fixed lane idea is replaced by a shape registry. The pulse slot
(`PulseId`/`PulseEffect`) and `AoePulseVfxSystem` are **kept unchanged for now**; the
single-duration capability is delivered by binding a slot's graph to the `Timed`
shape (opt-in), not by removing the pulse path.

## Rationale for major architectural decisions

- **ECS owns the shapes; graphs bind to a shape.** User requirement: hard-coded
  data-shape structs matching exactly what a graph expects, configurable per graph,
  validated at registration. This puts the CPU/GPU contract in one place and lets the
  payload set grow without touching unrelated graphs ("the data type is limiting" is
  solved structurally).
- **Per-graph shape ⇒ one combined system.** Because any slot can independently be
  any shape, there is no need for a category split anywhere. Fewer systems, one
  dispatch pipeline, one `ProducerHandle`, one id space.
- **Separate typed queue per shape.** Structs differ in size/fields and the user
  wants exact structs, not a superset. One `NativeQueue<T>` per shape keeps the
  high-frequency `Basic` path lean (no unused `Duration`/`TickInterval` bytes) and
  keeps each producer building a concrete struct (Burst-friendly).
- **Duration/TickInterval are authored.** They come from
  `LingeringAoeDefinition.lifetimeSeconds` / `tickIntervalSeconds` (already
  authored), threaded to a `VfxTimingData` component, never derived from the runtime
  `CombatLifetimeComponent.Remaining`.
- **Keep the pulse path this iteration.** Per user "keep pulse slot for now." The new
  timing capability is opt-in via shape binding; nothing is removed, so there is no
  behavior regression and no double-emission (a graph is driven by the pulse system
  *or* used as a one-shot `Timed`-shaped field, chosen by which slot the author fills).

## Constraints & invariants the change must respect

1. **Producer→consumer concurrency** (code: `CombatAoeVfxDispatchSystem`, emitters).
   Producers write via `NativeQueue<...>.AsParallelWriter()` and combine into
   `singleton.ProducerHandle`; the dispatch system (`PresentationSystemGroup`) calls
   `ProducerHandle.Complete()` **once** before draining, then resets it. All shapes'
   producers combine into the **same single** `ProducerHandle`, completed before any
   shape's queue is read.
2. **Single-threaded drain/dispatch** (doc: vfx-system.md; code: `.Run()` +
   `DrainAndDispatch`). Bucketing is main-thread `IJob.Run()`; managed `VisualEffect`
   upload/`SendEvent` is main-thread via `CombatVfxRoot.Instance`. No managed VFX
   calls from jobs.
3. **VfxId identity & ownership** (doc: "VFX Identity"; code: `Register`).
   `CombatVfxRoot` is the sole id allocator; `0` = no-VFX; same asset → same id; first
   registration owns the contract options (incl. the bound shape). The id now encodes
   `(shape, per-shape localIndex)` so shape is derivable from id; resources are held
   in a per-shape `owners` list keyed by that local index.
4. **Exposed input properties are Initialize-only — a graph-authoring invariant**
   (docs: "Spawn Payload Buffer Lifetime" and `vfx-shared-graph-area-size-corruption.md`).
   This is the real root cause and it lives in *how the graph is authored*, not in the
   C# dispatch. One `VisualEffect` instance per graph asset reuses its exposed buffers
   across every batch, overwriting them on the next non-empty dispatch, and
   `spawnIndex` is a batch-local index, not a stable instance id. Therefore **every
   exposed request-buffer input property — `Positions`, `AreaSizes`, `Durations`,
   `TickIntervals`, and any future shape buffer — may only be wired into the
   `Initialize Particle` context**, where each particle copies its value into a
   persistent particle attribute. Wiring *any* of them into `Update`/`Output` corrupts
   alive particles. This rule is identical for `Basic` and `Timed`; **the data-shape
   split does NOT isolate two skill sets that share one graph asset**, and no C#
   change here can enforce or substitute for the authoring rule (see the validation
   gap, constraint 8 / task 002).
5. **Grow-only native scratch & GPU buffers** (code: singleton comment,
   `EnsureBufferCapacity`). Each shape's scratch lists and each graph's buffers grow
   by doubling, never shrink.
6. **Performance budget** (doc: "Performance Notes"). Dispatch scales with graph
   count + staged upload, one `SendEvent` per graph per frame. `Basic` is the hot
   path and stays exactly as lean as today; `Timed` is low-frequency and only its
   graphs upload the two extra buffers.
7. **Burst constraints** (all `IJobEntity` emitters). Shape selection inside jobs is a
   plain `enum` switch over concrete structs + `bool` writer flags — no
   interfaces/virtuals.
8. **Registration validation is exact** (user requirement). A graph bound to a shape
   must expose *exactly* that shape's buffers plus `SpawnCount`/`OnSpawn`; anything
   missing, wrong-typed, or an unexpected extra `GraphicsBuffer` is a hard failure →
   logged error, id `0`.

## Mechanisms reused vs. introduced

**Reused (conform to, do not fork):**
- producer→`ProducerHandle`→`Complete()`→bucket→`DrainAndDispatch` pipeline.
- `CombatVfxRoot` id allocation, `owners` list, `AoeVfxTypeResources` growth,
  `CombatAoeVfxDispatcher` upload+`SendEvent` core, counting-sort bucketing shape.
- authored `lifetimeSeconds`/`tickIntervalSeconds` on `LingeringAoeDefinition`; the
  prefab→definition→runtime→SkillDriver registration chain.
- the existing collision-system split stays (it is a *gameplay* split, not a VFX one);
  its VFX emit just calls the shared shape-aware helper.

**Introduced (justified):**
- `VfxDataShape` enum + per-shape request structs (`VfxSpawnRequest`,
  `TimedVfxSpawnRequest`) + a `VfxDataShapeTable` descriptor registry (single source
  of truth for validation + buffer set). `VfxSpawnRequest` is the rename of today's
  `AoeVfxSpawnRequest`.
- One typed `NativeQueue<T>` + scratch per shape on the singleton.
- Per-shape buffer allocation on `AoeVfxTypeResources` + a per-shape upload path in
  the dispatcher.
- `VfxId` encoding helpers (`EncodeId`/`DecodeShape`/`DecodeLocalIndex`) so the shape
  rides inside the id — `AoeVfxIds` stays 5 plain `int`s.
- `VfxTimingData {float Duration; float TickInterval;}` for authored timing values.
- A shared Burst emit helper `VfxEmit.Enqueue(...)` used by every emit site, which
  picks the queue/struct via `DecodeShape(id)`.

## Minimal/additive vs. refactor comparison

**Additive (rejected): one superset payload.** Widen the single struct to include
`Duration`/`TickInterval`; keep one queue; timing-free events zero them.
- data flow: one queue/one pass, every graph uploads all four buffers.
- copies added: hot-path `Basic` events carry+sort 8 unused bytes; `Basic` graphs
  upload two unused buffers each dispatch (violates constraint 6).
- long-term: recreates "the data type is limiting" the moment a shape needs a field
  another does not; author is forced into unused fields (explicitly rejected by user).

**Refactor (chosen): ECS-owned per-graph data shapes.**
- data flow: N typed queues → N bucketing passes over the shared id space → N
  dispatch calls into the same root; a graph uploads exactly its shape's buffers.
- concepts changed: `AoeVfxSpawnRequest` becomes the `Basic` shape `VfxSpawnRequest`
  (unchanged fields); registration takes a shape + validates and encodes it into the
  id; `AoeVfxIds` stays 5 ints.
- copies removed/avoided: `Basic` hot path is byte-identical to today; no per-tick
  re-emission needed for the new timed-field capability.
- long-term: the shape set is the single extension point; adding a visual that needs
  new per-instance data = add a shape + queue, zero impact on other graphs; one
  combined system.

**Decision: refactor.** It is the user's stated model, avoids any category split,
keeps the hot path lean, and gives one source of truth for the CPU↔graph contract.
The only additive cost — one typed queue + scratch per shape — is the explicit price
of exact structs the user asked for, and is bounded by the (small, closed) shape set.

## Default decision rule (applied)

Presentation stays single-source: one root, one dispatcher, one id space, one
`ProducerHandle` — not duplicated per shape. The *shape contract* is centralized in
`VfxDataShapeTable` (validation + buffer set defined once, consumed by registration,
allocation, and dispatch) so there is no second description of a shape to drift.

## Design validation against invariants

- **(1)** all shapes' producers combine into the one `ProducerHandle`; completed once
  before draining every queue. ✔
- **(2)** per-shape bucketing `IJob.Run()` then main-thread dispatch; no new managed
  calls in jobs. ✔
- **(3)** id encodes its shape; a request's id only ever addresses a graph of the
  decoded shape (author-bound + validated), so a shape's queue only ever holds ids of
  that shape and each bucketing pass is self-contained. ✔
- **(4)** the Initialize-only authoring rule is documented as general to all exposed
  input properties (incl. `Durations`/`TickIntervals`); the refactor adds no path that
  reads request buffers outside `Initialize Particles`. ✔
- **(5)** per-shape scratch + per-graph buffers grow by doubling. ✔
- **(6)** `Basic` payload/upload byte-identical to today; extra buffers only for
  `Timed` graphs; still one `SendEvent`/graph/frame. ✔
- **(7)** emit helper is an enum switch over concrete structs + bool writer flags. ✔
- **(8)** `Register` validates the asset against `VfxDataShapeTable[shape]` exactly,
  hard-failing on missing/mistyped/extra buffers before encoding the id. ✔

## Task list

- [001-data-shape-core.md](001-data-shape-core.md) — `VfxDataShape` enum, per-shape
  request structs, `VfxDataShapeTable`, `VfxId` encode/decode helpers, `VfxTimingData`,
  per-shape queues + scratch on the singleton (`AoeVfxIds` stays 5 ints).
- [002-root-dispatcher-shapes.md](002-root-dispatcher-shapes.md) — shape-aware
  `Register` + exact validation, per-shape buffers on `AoeVfxTypeResources`, per-shape
  `Dispatch`, per-shape `DrainAndDispatch`.
- [003-dispatch-system-per-shape.md](003-dispatch-system-per-shape.md) — per-shape
  bucketing + drain in the one `CombatAoeVfxDispatchSystem`, one `ProducerHandle`.
- [004-shared-emit-helper.md](004-shared-emit-helper.md) — `VfxEmit.Enqueue` helper
  (decodes shape from id); update every emit site to use it; populate `VfxTimingData`
  at spawn.
- [005-authoring-per-graph-shape.md](005-authoring-per-graph-shape.md) — prefab
  per-slot shape selectors; carry shapes through definition→runtime→SkillDriver; pass
  shape to `Register` (which encodes it into the id). Keep pulse slot + system.
- [006-validation-and-docs.md](006-validation-and-docs.md) — exact-match validation
  polish + `vfx-system.md` rewrite for the data-shape model.

## Resolved decisions

- **Generic shape names.** Shapes are named by data content — `Basic` (no timing) and
  `Timed` (carries `Duration`+`TickInterval`) — not by AOE use-case. Only `Timed`
  carries the timing fields.
- **Exact-match strictness = hard fail.** Missing, wrong-typed, *and* unexpected extra
  `GraphicsBuffer` properties all fail registration (id `0`). No warn-only path.
- **Shape encoded in the id; `AoeVfxIds` stays 5 ints.** One graph = one shape, so the
  shape rides in the `VfxId` bits; jobs decode it with `DecodeShape(id)`. No slot
  struct, no parallel shapes component, no `vfxIds.*Id` churn.

## Open questions / considerations

- **Pulse vs timed-field overlap.** With the pulse system kept, `VfxTimingData`
  (authored duration/tick) and `AoePulseVfxComponent` (pulse interval) both exist.
  Intentional this iteration; a later pass can consolidate once the per-tick pulse is
  retired.
- **Same asset, two shapes.** If one asset is bound to different shapes by two AOEs,
  first registration wins; task 002 logs a conflict (mirrors the existing
  contract-conflict log).
- **Shared-graph corruption is orthogonal, not solved here.** Per
  `vfx-shared-graph-area-size-corruption.md`, one asset shared by multiple skill sets
  with different `AreaSize` (or now different `Duration`/`TickInterval`) corrupts alive
  particles if the graph samples request buffers after `Initialize Particles`. Neither
  the shape split nor duplicating/splitting graph assets fixes the broken authoring
  pattern — it only hides it. This plan preserves the correct pattern and adds the
  buffers to its regression coverage (task 006); it does not claim to fix the aliasing.
