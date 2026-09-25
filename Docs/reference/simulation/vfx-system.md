# VFX System

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be checked against code before implementation work.

This is the detailed VFX ECS dispatch doc. Use [index.md](./index.md) for the
simulation overview and aspect map.

The recurring cross-skill area-size corruption, its confirmed reproduction, and
the mandatory mixed-size graph test are documented separately in
[Shared VFX Graph Area-Size Corruption](./vfx-shared-graph-area-size-corruption.md).

## Summary

Combat VFX dispatch is ECS-owned and data-shape based. Simulation jobs emit
plain native requests into typed queues. `CombatAoeVfxDispatchSystem` completes
the shared producer handle once in `PresentationSystemGroup`, buckets each
shape by graph id, and dispatches through the single scene `CombatVfxRoot` and
its `CombatAoeVfxDispatcher`.

There is no impact-vs-lingering category split in presentation. A graph is bound
to one `VfxDataShape` at registration, and the returned `VfxId` encodes that
shape plus a dense per-shape local index. Emitters only carry the five `int`
slots in `AoeVfxIds`; jobs recover the shape from the id with
`VfxDataShapeTable.DecodeShape`.

The runtime treats VFX graphs as fire-and-forget renderers. CPU-side request
buffers are spawn-event payloads only: presentation uploads the current batch,
sends `OnSpawn`, and the graph must copy any per-instance values it needs into
particle attributes during `Initialize Particles`. After spawn, the GPU owns
lifetime, animation, and rendering for those particles.

## Data Flow

```text
Simulation producer job
  -> VfxEmit.Enqueue(...) or VfxEmit.EnqueueLineSegment(...)
  -> request enters the queue for its registered data shape
  -> producer job handle combines into CombatAoeVfxDispatchSingleton.ProducerHandle
  -> CombatAoeVfxDispatchSystem in PresentationSystemGroup
  -> ProducerHandle.Complete() once
  -> per-shape counting-sort bucketing by DecodeLocalIndex(VfxId)
  -> CombatVfxRoot drains and dispatches the matching data shape
  -> CombatAoeVfxDispatcher uploads shape buffers
  -> VisualEffect.SetGraphicsBuffer / SetInt / SendEvent("OnSpawn")
  -> graph Initialize Particles copies spawn payload into particle attributes
  -> graph Update/Output animates alive particles from particle attributes
```

`CombatAoeVfxDispatchSingleton` owns one persistent `NativeQueue<T>` per shape
and one shared `ProducerHandle`. Producers write through `AsParallelWriter()`;
the presentation consumer completes the handle before reading any shape queue.

## Data Shapes

`VfxDataShapeTable` is the single source of truth for shape contracts, id
encoding helpers, and property names used by validation, allocation, and
dispatch.

`Circular`:

- `CircularVfxSpawnRequest { VfxId, Position, AreaSize }`
- buffers: `Positions(float2)`, `AreaSizes(float)`

`TimedCircular`:

- `TimedCircularVfxSpawnRequest { VfxId, Position, AreaSize, Duration, TickInterval }`
- buffers: `Positions(float2)`, `AreaSizes(float)`, `Durations(float)`,
  `TickIntervals(float)`

`LineSegment`:

- `LineSegmentVfxSpawn { VfxId, StartPosition, EndPosition, Width }`
- buffers: `StartPositions(float2)`, `EndPositions(float2)`, `Widths(float)`
- directional graph shape; AOE emitters do not produce this shape

All shapes also use common `SpawnCount(int)` and `OnSpawn`.

## VFX Identity

`VfxId` identifies a registered graph plus its shape. `0` is the no-VFX
sentinel. Nonzero ids encode:

```text
high bits: VfxDataShape ordinal
low bits:  1-based local graph index within that shape
```

`CombatVfxRoot` is the sole id allocator. The same asset reference returns the
first registered id; if a later registration requests a different shape, the
root logs a conflict and keeps the original shape/id.

## Key Classes

`CombatVfxRoot`:

- owns all VFX GameObjects and `AoeVfxTypeResources`
- registers assets with `Register(asset, shape)`
- validates graph contracts before allocating
- keeps per-shape owner lists addressed by decoded local index
- drains Circular, TimedCircular, and LineSegment buckets through one dispatcher

`CombatAoeVfxDispatchSystem`:

- runs in `PresentationSystemGroup`
- owns per-shape queues and grow-only scratch lists
- completes the single producer handle once
- buckets each data-shape queue separately
- adds dispatched request count to combat stats

`CombatAoeVfxDispatcher`:

- validates exposed graph properties against `VfxDataShapeTable`
- owns upload logic for all data-shape buffers
- grows each graph resource's buffers by doubling
- sends `OnSpawn` after setting buffers and `SpawnCount`

## VFX Graph Contract

Each registered `VisualEffectAsset` must match exactly the shape it is bound to.
Missing required buffers, wrong buffer types, wrong `SpawnCount` type, or an
unexpected extra `GraphicsBuffer` property fail registration and return id `0`.

Required properties:

- `Circular`: `GraphicsBuffer Positions`, `GraphicsBuffer AreaSizes`, `int SpawnCount`
- `TimedCircular`: `GraphicsBuffer Positions`, `GraphicsBuffer AreaSizes`,
  `GraphicsBuffer Durations`, `GraphicsBuffer TickIntervals`, `int SpawnCount`
- `LineSegment`: `GraphicsBuffer StartPositions`, `GraphicsBuffer EndPositions`,
  `GraphicsBuffer Widths`, `int SpawnCount`
- event: `OnSpawn`

Graphs that cannot satisfy the contract are invalid for this runtime. There is
no per-event `Play()` fallback.

## TimedCircular Authoring

A TimedCircular graph is emitted once for the selected slot and receives `Duration` and
`TickInterval` from authored AOE timing (`lifetimeSeconds` and
`tickIntervalSeconds`, carried at runtime as `VfxTimingData`). The graph should
self-drive any internal pulses over that duration.

Timing values are transient spawn payloads just like position and area size. The
graph must sample `Durations` and `TickIntervals` in `Initialize Particles` and
copy them to particle attributes. Existing particles must not read request
buffers from `Update Particle` or `Output Particle`.

## Shared Upload Buffer Semantics

Shape buffers and `SpawnCount` are transient payloads for the current dispatch
batch. They are shared by the one `VisualEffect` instance for a graph and are
overwritten on later dispatches for that same graph. They are not persistent
per-particle storage.

Graph authoring must follow this rule:

```text
request buffers -> Initialize Particles -> particle attributes
particle attributes + age/lifetime/random/curves -> Update/Output
```

Failure mode: if a particle reads `AreaSizes[spawnIndex]` in `Output Particle`,
an old particle can index into the latest uploaded batch instead of the batch
that spawned it.

See
[Shared VFX Graph Area-Size Corruption](./vfx-shared-graph-area-size-corruption.md)
for the exact same-graph/different-skill-size mechanism and required regression
matrix.

## Emitters

AOE simulation producers call `VfxEmit.Enqueue`, which decodes the existing
Circular or TimedCircular slot id and writes the matching concrete
request. `TargetedResolveSystem` calls `VfxEmit.EnqueueLineSegment` with each
resolved link's start and end coordinates, and `VfxEmit.Enqueue` for its hit and
expire effects.

Current AOE emitters:

- AOE spawn expansion systems for spawn bursts and arming telegraphs
- `CombatArmingSystem` when an arming AOE goes live
- `CombatLifetimeSystem` when lingering AOEs expire
- impact and lingering AOE collision systems on confirmed AOE hits
- `AoePulseVfxSystem` on lingering AOE pulse intervals

LineSegment producers:

- `TargetedResolveSystem` for resolved chain links, currently the only producer
  of this shape

No emitter branches on `LingeringAoeTag` to choose VFX shape, and no emitter
uses `CombatLifetimeComponent.Remaining` as a duration source.

## Authoring

AOE prefab roots expose a `VfxDataShape` selector beside each VFX graph slot:
spawn, hit, expire, arming, and lingering pulse. All selectors default to
`Circular`, so existing content keeps the previous behavior.

`SkillSetCompiler` copies the selectors into `RuntimeAoeDefinition`.
`SkillDriver` registers each effect asset with its corresponding shape, and the
returned encoded ids are stored in `AoeVfxIds`.

The pulse slot, `AoePulseVfxComponent`, and `AoePulseVfxSystem` remain available.
TimedCircular slots are an opt-in alternative for graphs that can self-drive
their whole-duration visuals from one emission.

LineSegment is the directional shape. Its graph receives a start and end
coordinate plus width per spawn. Targeted chains are its only current
producer. `TargetedPrefab`'s link slot must use `VfxDataShape.LineSegment`;
targeted prefab validation rejects a bad link shape. `SkillDriver` registers
the slot. `VfxEmit.EnqueueLineSegment` silently drops an id whose encoded
shape is not LineSegment. No AOE producer emits this shape.

## Performance Notes

- `Circular` is the hot path and carries only id, position, and area size.
- `TimedCircular` uploads the two extra timing buffers only for graphs registered as
  TimedCircular.
- `LineSegment` uploads start and end coordinate buffers plus a width buffer.
- Dispatch cost scales with graph count plus staged upload cost.
- The runtime still sends at most one `OnSpawn` event per graph per frame.
- Request buffers are staging/upload memory; prioritization or culling must
  happen before dispatch and must not mutate already alive particles.
- Gameplay decisions stay out of VFX graphs. VFX requests are visual-only.
