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
one position plus one area size, and every graph receives both buffers. The
`AoeVfx*` type names make that limitation explicit. Projectile systems do not
emit, request, or register AOE VFX.

Primary uses:

- AOE hit pulses
- lingering AOE interval pulses
- AOE lifetime expire bursts

Non-goals:

- per-projectile or per-AOE particle-system GameObjects
- VFX Graph evaluation driven by per-entity ECS components every frame
- managed VFX calls from collision jobs

## Trigger Values

Runtime code uses `AoeVfxTrigger` instead of raw numeric trigger ids. The
enum is byte-backed so existing graph registration keys keep the same values.

| Enum | Value | Current emitters |
|---|---|---|
| `AoeVfxTrigger.Spawn` | 0 | `AOE spawn expansion systems` for expansion-spawned AOEs, `CombatArmingSystem` when an arming AOE goes live |
| `AoeVfxTrigger.Hit` | 1 | `ImpactAoeCollisionSystem`, `LingeringAoeCollisionSystem` |
| `AoeVfxTrigger.Expire` | 2 | `CombatLifetimeSystem` for lingering AOEs |
| `AoeVfxTrigger.Pulse` | 3 | `AoePulseVfxSystem` |
| `AoeVfxTrigger.Arming` | 4 | AOE spawn expansion systems for arming telegraphs |

Authoring can register `AoeVfxTrigger.Spawn` assets today. The ECS runtime emits
spawn VFX for expansion-spawned AOEs.

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
```

`AoeVfxSpawnRequest` carries:

- `int TypeId`
- `AoeVfxTrigger Trigger`
- `float2 Position`
- `float AreaSize`

`AoeVfxSpawnRequest` is the single VFX request payload. Like batched sprite
rendering, VFX dispatch is faction-agnostic; its presentation system owns the
shared native queue that bridges simulation producers to main-thread dispatch.

## Key Classes

`CombatVfxRoot`:

- scene-object owner for one `CombatAoeVfxDispatcher`
- static `Instance` set in `Awake` for ECS presentation lookup
- `Register(typeId, AoeVfxTrigger trigger, asset, maxPerFrame, requireAreaSizeContract)`
- `DrainAndDispatch(ref queue)` dequeues events, stages them, and dispatches

`CombatAoeVfxDispatchSystem`:

- `PresentationSystemGroup`
- owns the persistent shared `NativeQueue<AoeVfxSpawnRequest>`
- exposes `AsParallelWriter()` and `ProducerHandle` for simulation producers
- completes producers and drains the queue through `CombatVfxRoot.Instance`
  each frame

`CombatAoeVfxDispatcher`:

- owns one VFX instance per `(typeId, AoeVfxTrigger)` pair
- owns `GraphicsBuffer` and staging `NativeList` data for both `Positions` and `AreaSizes`
- caps staged events by `maxPerFrame`
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

## Emitters

`AOE spawn expansion systems` emits:

- `AoeVfxTrigger.Spawn` for each expansion-spawned AOE

`CombatLifetimeSystem` emits:

- `AoeVfxTrigger.Expire` when lingering AOE lifetime expires

`ImpactAoeCollisionSystem` and `LingeringAoeCollisionSystem` emit:

- `AoeVfxTrigger.Hit` for confirmed AOE-target hits

`AoePulseVfxSystem` emits:

- `AoeVfxTrigger.Pulse` when `AoePulseVfxComponent.RemainingInterval` reaches zero on an
  active lingering AOE

AOE spawn expansion systems emit:

- `AoeVfxTrigger.Arming` when `ArmSeconds > 0`

## Authoring

AOE VFX slots live on `BasicAoePrefab`, `LingeringAoePrefab`, and
`AoeTypeDefinition`:

```text
spawnEffect   -> AoeVfxTrigger.Spawn, emitted by AOE spawn expansion systems
hitEffect     -> AoeVfxTrigger.Hit
expireEffect  -> AoeVfxTrigger.Expire
pulseEffect   -> AoeVfxTrigger.Pulse
armingEffect  -> AoeVfxTrigger.Arming
```

`SkillDriver` registers AOE VFX slots against the configured `CombatVfxRoot`.
`CombatRoot` does not depend on `CombatVfxRoot`.

## Area Size

Every VFX request carries an `AreaSize` value. AOE requests populate it from
resolved AOE geometry before the event reaches ECS; that area value is copied
into VFX requests from collision, pulse, and lifetime paths.

The `AreaSizes` buffer is always uploaded on every dispatch. Graphs that do not
need to scale by area size can expose the buffer and ignore it.

## Render Layering

Combat VFX scene objects inherit the Unity GameObject layer from their owning
`CombatVfxRoot`. Camera culling should therefore follow the root setup.

SpriteRenderer sorting layers do not automatically order Visual Effect Graph
outputs. VFX Graph outputs must set their own render ordering through output
settings such as render queue, VFX sorting priority, depth test, and graph
bounds.

## Performance Notes

- Dispatch cost should scale with registered effect type count plus staged event
  upload cost, not with one managed call per event.
- `maxPerFrame` defaults to `2048` per `(typeId, AoeVfxTrigger)` pair.
- Dropping low-priority events after the cap is acceptable for visual-only
  effects.
- Keep gameplay decisions out of VFX graphs. VFX requests are visual-only.
