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
| 0 | spawn | registered by authoring, currently reserved for future spawn VFX |
| 1 | hit | `ProjectileCollisionSystem`, `AoeCollisionSystem` |
| 2 | expire | `CombatLifetimeSystem`, projectile collision deactivation |
| 3 | pulse | `AoePulseVfxSystem` |

Authoring can register trigger 0 assets today, but the current ECS runtime does
not emit spawn VFX from projectile or AOE apply systems.

## Data Flow

```text
Simulation job
  -> VfxPendingSpawn in a NativeQueue or NativeStream
  -> VfxFlushJob or stream flush job
  -> DynamicBuffer<VfxSpawnRequestElement> on shared CombatScope
  -> CombatVfxDispatchSystem in PresentationSystemGroup
  -> CombatVfxRoot.DrainAndDispatch
  -> CombatVfxDispatcher.StageSpawn
  -> CombatVfxDispatcher.Dispatch
  -> GraphicsBuffer.SetData
  -> VisualEffect.SetGraphicsBuffer / SetInt / SendEvent
```

`VfxPendingSpawn` carries:

- `CombatFaction Faction`
- `int TypeId`
- `byte Trigger`
- `float2 Position`
- `float AreaSize`

`VfxSpawnRequestElement` is the scope-buffer version of the same data.

## Key Classes

`CombatVfxRoot`:

- scene-object owner for one `CombatVfxDispatcher`
- static int-keyed registry for ECS lookup
- `Register(typeId, trigger, asset, maxPerFrame, requireAreaSizeContract)`
- `Bind(scopeEntity, entityManager)` adds `CombatScopeVfxCatalog`
- `DrainAndDispatch(buffer)` stages events, clears the buffer, and dispatches

`CombatVfxDispatchSystem`:

- `PresentationSystemGroup`
- queries scope entities with `CombatScopeVfxCatalog`
- resolves `CombatVfxRoot` from the static registry
- calls `DrainAndDispatch`

`CombatVfxDispatcher`:

- owns one VFX instance per `(typeId, trigger)` pair
- owns `GraphicsBuffer` and staging `NativeList` data
- caps staged events by `maxPerFrame`
- uploads positions and optional area size data
- sends the graph event
- disposes native/GPU resources on teardown

`VfxFlushJob`:

- Burst `IJob`
- drains `NativeQueue<VfxPendingSpawn>` into the shared scope buffer
- used by common lifetime and AOE pulse VFX paths

Collision systems use a `NativeStream` for hit/expire VFX and a local stream
flush job to append into the same scope buffer.

## VFX Graph Contract

Each registered `VisualEffectAsset` must expose:

- `GraphicsBuffer` named `Positions`, with one `float2` world position per
  spawn event
- `int` named `SpawnCount`
- event named `OnSpawn`

AOE VFX registered with `requireAreaSizeContract: true` must also expose:

- `GraphicsBuffer` named `AreaSizes`, with one `float` area size per spawn event

Graphs that cannot satisfy the contract are invalid for this runtime. There is
no per-event `Play()` fallback.

## Emitters

`ProjectileCollisionSystem` emits:

- trigger 1 on each confirmed projectile-target hit
- trigger 2 when collision deactivates the projectile

`CombatLifetimeSystem` emits:

- trigger 2 when projectile lifetime expires
- trigger 2 when lingering AOE lifetime expires

`AoeCollisionSystem` emits:

- trigger 1 for confirmed AOE-target hits

`AoePulseVfxSystem` emits:

- trigger 3 when `AoePulseVfxComponent.RemainingInterval` reaches zero on an
  active lingering AOE

## Authoring

Projectile VFX slots live on `BasicAttackPrefab`:

```text
spawnEffect   -> trigger 0, registered but not emitted by current runtime
hitEffect     -> trigger 1
expireEffect  -> trigger 2
```

AOE VFX slots live on `BasicAoePrefab`, `LingeringAoePrefab`, and
`AoeTypeDefinition`:

```text
spawnEffect   -> trigger 0, registered but not emitted by current runtime
hitEffect     -> trigger 1
expireEffect  -> trigger 2
pulseEffect   -> trigger 3
```

`PlayerSkillDriver` and `MobProjectileAttack` register VFX slots against the
configured `CombatVfxRoot`. `CombatRoot` does not depend on `CombatVfxRoot`.

## Area Size

AOE VFX receive `AreaSize` so graph particles can scale to gameplay area. AOE
geometry is resolved before ECS receives the spawn event, and that area value is
copied into VFX requests from collision, pulse, and lifetime paths.

Projectile VFX also carry `AreaSize`, usually derived from render visual scale,
so graphs can size impact or expire effects consistently.

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
