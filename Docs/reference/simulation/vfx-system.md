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

| Value | Name | Current emitters |
|---|---|---|
| 0 | spawn | `AOE spawn expansion systems` for expansion-spawned AOEs |
| 1 | hit | `ProjectileCollisionSystem`, `ImpactAoeCollisionSystem`, `LingeringAoeCollisionSystem` |
| 2 | expire | `CombatLifetimeSystem`, projectile collision deactivation |
| 3 | pulse | `AoePulseVfxSystem` |

Authoring can register trigger 0 assets today. The current ECS runtime emits
spawn VFX for expansion-spawned AOEs; projectile spawn VFX is still registered
but not emitted by the projectile spawn apply path.

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
- `byte Trigger`
- `float2 Position`
- `float AreaSize`

`VfxPendingSpawn` is the single VFX request payload. Like batched sprite
rendering, VFX dispatch is faction-agnostic; its presentation system owns the
shared native queue that bridges simulation producers to main-thread dispatch.

## Key Classes

`CombatVfxRoot`:

- scene-object owner for one `CombatVfxDispatcher`
- static `Instance` set in `Awake` for ECS presentation lookup
- `Register(typeId, trigger, asset, maxPerFrame, requireAreaSizeContract)`
- `DrainAndDispatch(ref queue)` dequeues events, stages them, and dispatches

`CombatVfxDispatchSystem`:

- `PresentationSystemGroup`
- owns the persistent shared `NativeQueue<VfxPendingSpawn>`
- exposes `AsParallelWriter()` and `ProducerHandle` for simulation producers
- completes producers and drains the queue through `CombatVfxRoot.Instance`
  each frame

`CombatVfxDispatcher`:

- owns one VFX instance per `(typeId, trigger)` pair
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

- trigger 0 for each expansion-spawned AOE

`ProjectileCollisionSystem` emits:

- trigger 1 on each confirmed projectile-target hit
- trigger 2 when collision deactivates the projectile

`CombatLifetimeSystem` emits:

- trigger 2 when projectile lifetime expires
- trigger 2 when lingering AOE lifetime expires

`ImpactAoeCollisionSystem` and `LingeringAoeCollisionSystem` emit:

- trigger 1 for confirmed AOE-target hits

`AoePulseVfxSystem` emits:

- trigger 3 when `AoePulseVfxComponent.RemainingInterval` reaches zero on an
  active lingering AOE

## Authoring

Projectile VFX slots live on `BasicAttackPrefab`:

```text
spawnEffect   -> trigger 0, registered but not emitted by current projectile runtime
hitEffect     -> trigger 1
expireEffect  -> trigger 2
```

AOE VFX slots live on `BasicAoePrefab`, `LingeringAoePrefab`, and
`AoeTypeDefinition`:

```text
spawnEffect   -> trigger 0, emitted by AOE spawn expansion systems
hitEffect     -> trigger 1
expireEffect  -> trigger 2
pulseEffect   -> trigger 3
```

`PlayerSkillDriver` and `MobProjectileAttack` register VFX slots against the
configured `CombatVfxRoot`. `CombatRoot` does not depend on `CombatVfxRoot`.

## Area Size

Every VFX request carries an `AreaSize` value. AOE requests populate it from
resolved AOE geometry before the event reaches ECS â€?that area value is copied
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
- `maxPerFrame` defaults to `2048` per `(typeId, trigger)` pair.
- Dropping low-priority events after the cap is acceptable for visual-only
  effects.
- Keep gameplay decisions out of VFX graphs. VFX requests are visual-only.
