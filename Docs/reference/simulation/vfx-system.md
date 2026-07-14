# VFX System

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be checked against code before implementation work.

This is the detailed VFX ECS dispatch doc. Use [index.md](./index.md) for the
simulation overview and aspect map.

## Summary

The VFX system dispatches batched Visual Effect Graph events from ECS simulation
data. Simulation jobs emit plain native VFX requests. A presentation system
stages those requests into GPU buffers and fires VFX Graph events.

Primary uses:

- projectile hit sparks and impact flashes
- projectile expire/despawn trails
- AOE hit pulses
- lingering AOE interval pulses
- AOE lifetime expire bursts

Non-goals:

- per-projectile or per-AOE particle-system GameObjects
- VFX Graph evaluation driven by per-entity ECS components every frame
- managed VFX calls from collision jobs

## Trigger Values

Runtime code uses `CombatVfxTrigger` instead of raw numeric trigger ids. The
enum is byte-backed so existing graph registration keys keep the same values.

| Enum | Value | Current emitters |
|---|---|---|
| `CombatVfxTrigger.Spawn` | 0 | `AOE spawn expansion systems` for expansion-spawned AOEs, `CombatArmingSystem` when an arming AOE goes live |
| `CombatVfxTrigger.Hit` | 1 | `ProjectileCollisionSystem`, `ImpactAoeCollisionSystem`, `LingeringAoeCollisionSystem` |
| `CombatVfxTrigger.Expire` | 2 | `CombatLifetimeSystem`, projectile collision deactivation |
| `CombatVfxTrigger.Pulse` | 3 | `AoePulseVfxSystem` |
| `CombatVfxTrigger.Arming` | 4 | projectile and AOE spawn expansion systems for arming telegraphs |

Authoring can register `CombatVfxTrigger.Spawn` assets today. The current ECS
runtime emits spawn VFX for expansion-spawned AOEs; projectile spawn VFX is
still registered but not emitted by the projectile spawn apply path.

## Data Flow

```text
Simulation producer job
  -> VfxPendingSpawn enqueued into the shared NativeQueue<VfxPendingSpawn>
     owned by CombatVfxDispatchSystem via AsParallelWriter()
  -> producer job handle combined into CombatVfxDispatchSystem.ProducerHandle
  -> CombatVfxDispatchSystem in PresentationSystemGroup
  -> ProducerHandle.Complete()
  -> CombatVfxRoot.DrainAndDispatch(ref queue) on the main thread
  -> CombatVfxDispatcher.StageSpawn
  -> CombatVfxDispatcher.Dispatch
  -> GraphicsBuffer.SetData (Positions + AreaSizes)
  -> VisualEffect.SetGraphicsBuffer / SetInt / SendEvent
```

`VfxPendingSpawn` carries:

- `int TypeId`
- `CombatVfxTrigger Trigger`
- `float2 Position`
- `float AreaSize`

`VfxPendingSpawn` is the single VFX request payload. Like batched sprite
rendering, VFX dispatch is faction-agnostic; its presentation system owns the
shared native queue that bridges simulation producers to main-thread dispatch.

## Key Classes

`CombatVfxRoot`:

- scene-object owner for one `CombatVfxDispatcher`
- static `Instance` set in `Awake` for ECS presentation lookup
- `Register(typeId, CombatVfxTrigger trigger, asset, maxPerFrame, requireAreaSizeContract)`
- `DrainAndDispatch(ref queue)` dequeues events, stages them, and dispatches

`CombatVfxDispatchSystem`:

- `PresentationSystemGroup`
- owns the persistent shared `NativeQueue<VfxPendingSpawn>`
- exposes `AsParallelWriter()` and `ProducerHandle` for simulation producers
- completes producers and drains the queue through `CombatVfxRoot.Instance`
  each frame

`CombatVfxDispatcher`:

- owns one VFX instance per `(typeId, CombatVfxTrigger)` pair
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

- `CombatVfxTrigger.Spawn` for each expansion-spawned AOE

`ProjectileCollisionSystem` emits:

- `CombatVfxTrigger.Hit` on each confirmed projectile-target hit
- `CombatVfxTrigger.Expire` when collision deactivates the projectile

`CombatLifetimeSystem` emits:

- `CombatVfxTrigger.Expire` when projectile lifetime expires
- `CombatVfxTrigger.Expire` when lingering AOE lifetime expires

`ImpactAoeCollisionSystem` and `LingeringAoeCollisionSystem` emit:

- `CombatVfxTrigger.Hit` for confirmed AOE-target hits

`AoePulseVfxSystem` emits:

- `CombatVfxTrigger.Pulse` when `AoePulseVfxComponent.RemainingInterval` reaches zero on an
  active lingering AOE

Projectile and AOE spawn expansion systems emit:

- `CombatVfxTrigger.Arming` when `ArmSeconds > 0`

## Authoring

Projectile VFX slots live on `BasicAttackPrefab`:

```text
spawnEffect   -> CombatVfxTrigger.Spawn, registered but not emitted by current projectile runtime
hitEffect     -> CombatVfxTrigger.Hit
expireEffect  -> CombatVfxTrigger.Expire
armingEffect  -> CombatVfxTrigger.Arming
```

AOE VFX slots live on `BasicAoePrefab`, `LingeringAoePrefab`, and
`AoeTypeDefinition`:

```text
spawnEffect   -> CombatVfxTrigger.Spawn, emitted by AOE spawn expansion systems
hitEffect     -> CombatVfxTrigger.Hit
expireEffect  -> CombatVfxTrigger.Expire
pulseEffect   -> CombatVfxTrigger.Pulse
armingEffect  -> CombatVfxTrigger.Arming
```

`SkillDriver` and `MobProjectileAttack` register VFX slots against the
configured `CombatVfxRoot`. `CombatRoot` does not depend on `CombatVfxRoot`.

## Area Size

Every VFX request carries an `AreaSize` value. AOE requests populate it from
resolved AOE geometry before the event reaches ECS �?that area value is copied
into VFX requests from collision, pulse, and lifetime paths. Projectile requests
populate it from render visual scale so graphs can size impact or expire effects
consistently.

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
- `maxPerFrame` defaults to `2048` per `(typeId, CombatVfxTrigger)` pair.
- Dropping low-priority events after the cap is acceptable for visual-only
  effects.
- Keep gameplay decisions out of VFX graphs. VFX requests are visual-only.
