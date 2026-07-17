# VFX System

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be checked against code before implementation work.

This is the detailed VFX ECS dispatch doc. Use [index.md](./index.md) for the
simulation overview and aspect map.

## Summary

The VFX system dispatches batched Visual Effect Graph events from ECS simulation
data. Simulation jobs emit plain native VFX requests. A presentation system
stages those requests into GPU buffers and fires VFX Graph events.

The current runtime only supports AOE-shaped VFX requests: each request carries
one graph-kind id, one position, and one area size. One VFX Graph instance
represents every event of that graph kind on screen, and every graph receives
both buffers. The `AoeVfx*` type names make the AOE-shaped payload limitation
explicit. Projectile systems do not emit, request, or register AOE VFX.

The runtime treats VFX graphs as fire-and-forget renderers. CPU-side request
buffers are spawn-event payloads only: the presentation path uploads the current
batch, sends `OnSpawn`, and then the graph must copy any per-instance values it
needs into particle attributes during `Initialize Particles`. After spawn, the
GPU owns lifetime, animation, and rendering for those particles.

Primary uses:

- AOE hit pulses
- lingering AOE interval pulses
- AOE lifetime expire bursts

Non-goals:

- per-projectile or per-AOE particle-system GameObjects
- VFX Graph evaluation driven by per-entity ECS components every frame
- managed VFX calls from collision jobs

## VFX Identity

`VfxId` identifies the registered VFX graph kind. It must not be derived from
event cause, and it should not imply player, mob, faction, or individual AOE
instance ownership. Event timing decides when a request is emitted; it does not
make a separate visual resource.

Examples:

- poison cloud VFX graph -> one `VfxId` for every poison cloud event on screen
- fire burst VFX graph -> one `VfxId` for every fire burst event on screen
- repeated AOE types using the same graph -> same `VfxId`

## Data Flow

```text
Simulation producer job
  -> AoeVfxSpawnRequest enqueued into the shared NativeQueue<AoeVfxSpawnRequest>
     owned by CombatAoeVfxDispatchSystem via AsParallelWriter()
  -> producer job handle combined into CombatAoeVfxDispatchSystem.ProducerHandle
  -> CombatAoeVfxDispatchSystem in PresentationSystemGroup
  -> ProducerHandle.Complete()
  -> CombatVfxRoot.DrainAndDispatch(ref queue) on the main thread
  -> CombatAoeVfxDispatcher.StageAoeSpawn
  -> CombatAoeVfxDispatcher.Dispatch
  -> GraphicsBuffer.SetData (Positions + AreaSizes)
  -> VisualEffect.SetGraphicsBuffer / SetInt / SendEvent
  -> graph Initialize Particles copies spawn payload into particle attributes
  -> graph Update/Output animates alive particles from particle attributes,
     age/lifetime, random values, and curves
```

`AoeVfxSpawnRequest` carries:

- `int VfxId`
- `float2 Position`
- `float AreaSize`

`AoeVfxSpawnRequest` is the single VFX request payload. Like batched sprite
rendering, VFX dispatch is faction-agnostic; its presentation system owns the
shared native queue that bridges simulation producers to main-thread dispatch.
Event cause is not carried as a separate field.

## Key Classes

`CombatVfxRoot`:

- scene-object owner for one `CombatAoeVfxDispatcher`
- static `Instance` set in `Awake` for ECS presentation lookup; a second active root is rejected
  (logged and disabled) rather than silently replacing `Instance`, because queued ids are root-local
- `Register(asset, requireAreaSizeContract) -> VfxId`: the sole graph-kind id allocator.
  Same asset reference always returns the same nonzero id without allocating again; a `null` asset
  returns `0`; distinct assets always get distinct ids. The first successful registration owns that
  graph's contract options - a later call with conflicting options logs an error and keeps the
  original settings rather than creating a second identity.
- names each effect's scene object `{asset.name}_{vfxId}` for diagnostics only; the name never
  defines identity
- `DrainAndDispatch(ref queue)` dequeues events, stages them, and dispatches
- ids are stable only for the root's lifetime, not persisted across sessions or scene rebuilds

`CombatAoeVfxDispatchSystem`:

- `PresentationSystemGroup`
- owns the persistent shared `NativeQueue<AoeVfxSpawnRequest>`
- exposes `AsParallelWriter()` and `ProducerHandle` for simulation producers
- completes producers and drains the queue through `CombatVfxRoot.Instance`
  each frame

`CombatAoeVfxDispatcher`:

- owns one VFX instance per registered graph-kind id
- owns `GraphicsBuffer` and staging `NativeList` data for both `Positions` and `AreaSizes`
- always uploads both `Positions` and `AreaSizes` buffers on every dispatch
- sends the graph event
- disposes native/GPU resources on teardown

## VFX Graph Contract

Each registered `VisualEffectAsset` must expose:

- `GraphicsBuffer` named `Positions`, with one `float2` world position per
  spawn event
- `GraphicsBuffer` named `AreaSizes`, with one `float` area size per spawn event
- `int` named `SpawnCount`
- event named `OnSpawn`

The runtime always uploads both `Positions` and `AreaSizes` buffers on every
dispatch, regardless of VFX type. `requireAreaSizeContract: true` at
registration only enables upfront validation that the graph exposes `AreaSizes`;
omitting it skips the check but the buffer is still sent.

Graphs that cannot satisfy the contract are invalid for this runtime. There is
no per-event `Play()` fallback.

### Spawn Payload Buffer Lifetime

`Positions`, `AreaSizes`, and `SpawnCount` are transient spawn payloads for the
current dispatch batch. They are shared by the one `VisualEffect` instance that
represents a graph kind, and they are overwritten on later dispatches for that
same graph kind. They are not persistent per-particle storage.

Graph authoring must follow this rule:

```text
request buffers -> Initialize Particles -> particle attributes
particle attributes + age/lifetime/random/curves -> Update/Output
```

Values that describe one spawned VFX instance, such as position, area size,
initial color, initial rotation, seed, or other event payload values, must be
sampled from request buffers in `Initialize Particles` and stored on particle
attributes. Existing particles must not read request buffers from `Update
Particle` or `Output Particle` to recover per-instance values.

`Initialize Particles` runs only for particles created by the spawn event, so it
is the handoff from CPU spawn payload to GPU-owned particle state. `Output
Particle` runs for alive particles when they render, so a buffer read there sees
the currently bound buffer contents, not the contents from that particle's spawn
event.

Failure mode: if `AreaSizes[spawnIndex]` is read in `Output Particle`, an old
particle keeps its original `spawnIndex` but indexes into the latest uploaded
`AreaSizes` batch. When two skill sets use the same graph with different area
sizes, alive particles can briefly render at another batch's size. This looks
like a one-frame size shrink/grow even though particle age and lifetime did not
reset.

## Emitters

AOE simulation producers emit requests when gameplay timing says a visual should
appear:

- AOE spawn expansion systems for expansion-spawned AOEs and arming telegraphs
- `CombatArmingSystem` when an arming AOE goes live
- `CombatLifetimeSystem` when lingering AOEs expire
- `ImpactAoeCollisionSystem` and `LingeringAoeCollisionSystem` on confirmed AOE hits
- `AoePulseVfxSystem` on lingering AOE pulse intervals

## Authoring

AOE VFX authoring is graph-kind scoped. A registered VFX graph kind represents
all events of that kind on screen, regardless of which AOE type, actor faction,
or producer emitted the request.

`SkillDriver` registers each AOE definition's authored graph slots against the
configured `CombatVfxRoot` and stores the returned ids as an `AoeVfxIds`
snapshot. Simulation producers select `SpawnId`, `HitId`, `ExpireId`, `PulseId`,
or `ArmingId` from that snapshot; none of them derive a VFX id from
`AoeIdentityComponent.TypeId` or an event-cause enum.

## Area Size

Every VFX request carries an `AreaSize` value. AOE requests populate it from
resolved AOE geometry before the event reaches ECS; that area value is copied
into VFX requests from collision, pulse, and lifetime paths.

The `AreaSizes` buffer is always uploaded on every dispatch. Graphs that do not
need to scale by area size can expose the buffer and ignore it.

Graphs that use `AreaSize` must treat it as an initial per-instance value.
Sample `AreaSizes` in `Initialize Particles`, write it into a particle-owned
size or custom attribute, and drive over-life animation in `Output Particle`
from that stored value. Do not sample `AreaSizes` directly from `Output
Particle`.

## Render Layering

Combat VFX scene objects inherit the Unity GameObject layer from their owning
`CombatVfxRoot`. Camera culling should therefore follow the root setup.

SpriteRenderer sorting layers do not automatically order Visual Effect Graph
outputs. VFX Graph outputs must set their own render ordering through output
settings such as render queue, VFX sorting priority, depth test, and graph
bounds.

## Performance Notes

- Dispatch cost should scale with registered graph-kind count plus staged event
  upload cost, not with one managed call per event.
- Request buffers are staging/upload memory. If a future visual budget drops or
  prioritizes requests, it must do so before dispatch and must not change
  already alive particles.
- Keep gameplay decisions out of VFX graphs. VFX requests are visual-only.
