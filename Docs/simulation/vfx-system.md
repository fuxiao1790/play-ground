# VFX System

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Summary

The VFX system fires batched Visual Effect Graph particle effects from ECS
simulation events without per-event CPU expansion. Positions for hundreds or
thousands of events per frame are staged into a `NativeList<float2>`, uploaded
once to a `GraphicsBuffer`, and dispatched to a single `VisualEffect` scene
instance via `SendEvent`. The main thread cost is O(registered effect types),
not O(events).

Primary uses:

- projectile hit sparks and impact flashes
- projectile expire / despawn trails
- AOE hit pulses on targets
- lingering AOE tick pulses at the AOE center
- AOE lifetime expire bursts

Non-goal:

- per-entity particle systems attached to projectile or AOE GameObjects
- VFX Graph evaluation driven by ECS component data written per-frame from jobs

## Architecture

### One VFX instance per (typeId, trigger) pair

`CombatVfxDispatcher` owns one `VisualEffect` scene instance for each
registered `(typeId, trigger)` combination. An instance is only created when a
non-null `VisualEffectAsset` is assigned to that slot. Unassigned slots are
skipped with zero allocation.

### Trigger values

| Value | Name   | When fired                                                  |
|-------|--------|-------------------------------------------------------------|
| 0     | spawn  | AOE materialized; projectile spawn is reserved, not yet wired |
| 1     | hit    | projectile hit target / AOE hit target                      |
| 2     | expire | projectile lifetime end or collision deactivation / AOE lifetime end |
| 3     | pulse  | lingering AOE interval tick (regardless of targets hit)     |

### Per-frame data flow

```
ECS simulation jobs
  → NativeQueue<VfxPendingSpawn>   (one queue per system per frame)
  → VfxFlushJob                    (drains queue into scope buffer, IJob, Burst)
  → DynamicBuffer<VfxSpawnRequestElement> on scope entity
  → CombatVfxDispatchSystem.OnUpdate (PresentationSystemGroup, main thread)
      → CombatVfxRoot.DrainAndDispatch
          → CombatVfxDispatcher.StageSpawn (O(events))
          → CombatVfxDispatcher.Dispatch   (O(registered types))
              → GraphicsBuffer.SetData
              → VisualEffect.SetGraphicsBuffer + SetInt + SendEvent
```

### VFX Graph authoring contract

Each `VisualEffectAsset` assigned to a trigger slot must expose:

- `GraphicsBuffer` property named `"Positions"` — `float2` stride, x/y world position per spawn event
- `int` property named `"SpawnCount"` — count of valid positions in the buffer
- Event named `"OnSpawn"` — triggers the effect burst

All three are set by `CombatVfxDispatcher.Dispatch`. Effect definition (lifetime,
color, size, shape) lives entirely in the VFX Graph asset.

The dispatcher fails fast when a registered graph does not expose this contract.
There is no simple `Play()` fallback; a graph that cannot consume uploaded ECS
positions is invalid for this runtime.

### AOE VFX size normalization

AOE gameplay area size is expressed in world units and resolved by the
translation layer before the spawn request crosses into ECS. The canonical reference area is the `Standard1RadiusAoe` prefab
(`Assets/Prefabs/Skills/Standard1RadiusAoe.prefab`). Its hurtbox defines radius
`1.0` in gameplay terms.

VFX graphs are authored at an arbitrary native particle size. To align particle
visuals with the gameplay hitbox at runtime:

1. Each AOE VFX Graph asset must expose a `GraphicsBuffer` property named
   `"AreaSize"` that carries one `float` area size per spawn event.

2. At AOE spawn time the runtime passes the resolved gameplay AOE area size
   (= `baseAreaSize * player.areaSizeMultiplier`, after support changes) into
   the VFX Graph. The graph scales its particles by that value. Any size
   normalization coefficient needed to match authored particle size to the
   logical hitbox radius is baked directly into the VFX Graph asset.

### Render Layering

Combat VFX scene objects inherit the Unity GameObject layer from their owning
`CombatVfxRoot`. This keeps camera culling behavior aligned with the scope the
root is bound to.

SpriteRenderer Sorting Layers do not automatically order Visual Effect Graph
outputs. VFX Graph outputs must set their own render ordering through output
settings such as render queue, VFX sorting priority, depth test, and graph
bounds. The bundled `BasicColorBlobVfx` is authored as a batched world-space
effect and should draw in the transparent queue after gameplay sprites.

---

## Key Classes

### `CombatVfxRoot` (`Assets/Scripts/System/Vfx/CombatVfxRoot.cs`)

MonoBehaviour; one scene-object instance wired to one or more scope roots via
the Inspector. Maintains a static `Dictionary<int, CombatVfxRoot>` keyed by
auto-incremented int so ECS never holds a managed reference.

| Method | Description |
|---|---|
| `Register(typeId, trigger, asset, maxPerFrame, requireAreaSizeContract)` | Delegates to owned `CombatVfxDispatcher`. No-op if `asset == null` or already registered. |
| `Bind(scopeEntity, entityManager)` | Upserts `CombatScopeVfxCatalog { VfxRootId }` onto the scope entity. Safe to call multiple times. |
| `ResetDispatcher()` | Disposes current dispatcher and creates a fresh one (used when type registry resets). |
| `DrainAndDispatch(buffer)` | Stages all events from the scope buffer, clears it, then calls `Dispatcher.Dispatch()`. Called by `CombatVfxDispatchSystem`. |

Registerers (`PlayerSkillDriver`, `MobProjectileAttack`) hold a serialized
`vfxRoot` field and call `vfxRoot.Register(...)` directly after registering
with `CombatRoot`. `CombatRoot` does not depend on `CombatVfxRoot`.

### `CombatVfxDispatchSystem` (`Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs`)

ECS `SystemBase` in `PresentationSystemGroup`. Queries all scope entities with
`CombatScopeVfxCatalog`, resolves the owning `CombatVfxRoot` via `TryGetRoot`,
and calls `root.DrainAndDispatch(buffer)` per scope. Calls `CompleteDependency()`
at the top of `OnUpdate` to ensure all `VfxFlushJob` work is complete before
reading the scope buffer.

### `CombatVfxDispatcher` (`Assets/Scripts/System/Vfx/CombatVfxDispatcher.cs`)

Managed class; one instance owned by `CombatVfxRoot`.

| Method | Description |
|---|---|
| `Register(typeId, trigger, asset, maxPerFrame)` | Creates one `VisualEffect` GO + `GraphicsBuffer` + `NativeList<float2>` staging. No-op if `asset == null` or already registered. |
| `StageSpawn(typeId, trigger, position)` | Appends a world position to the staging list for the given key. Called from `CombatVfxRoot.DrainAndDispatch` (invoked by `CombatVfxDispatchSystem`). Capped at `maxPerFrame`. |
| `Dispatch()` | For each registered resource with a non-empty staging list: uploads positions to GPU, sets SpawnCount, fires OnSpawn event, clears staging. |
| `Dispose()` | Disposes all NativeLists, releases all GraphicsBuffers, destroys all VFX GameObjects. |

`maxPerFrame` defaults to `2048` per `(typeId, trigger)` pair in both roots.
Tune after profiling. `GraphicsBuffer` size is `maxPerFrame * 8` bytes.

### `VfxTypeResources` (`Assets/Scripts/System/Vfx/CombatVfxDispatcher.cs`)

Plain data class; one instance per registered `(typeId, trigger)` key.
Fields: `Instance` (VisualEffect), `PositionBuffer` (GraphicsBuffer),
`Staging` (NativeList\<float2\>), `MaxPerFrame`.

Dispose order: NativeList first, then GraphicsBuffer, then destroy the GO.
This ensures the native memory is freed even if GPU teardown throws.

### `VfxFlushJob` (`Assets/Scripts/System/Vfx/VfxFlushJob.cs`)

`[BurstCompile] IJob`. Drains a `NativeQueue<VfxPendingSpawn>` into the
`DynamicBuffer<VfxSpawnRequestElement>` on the owning scope entity. One job
per system per frame. Runs after the simulation job that wrote the queue.

### `VfxPendingSpawn` / `VfxSpawnRequestElement` / `CombatScopeVfxCatalog` (`Assets/Scripts/System/Vfx/VfxEcsComponents.cs`)

`VfxPendingSpawn` — transient native payload; queued by simulation jobs,
drained by `VfxFlushJob`. Holds `Scope`, `TypeId`, `Trigger`, `Position`.

`VfxSpawnRequestElement` — scope buffer; added to scope entity at root setup;
drained by `CombatVfxDispatchSystem` in `PresentationSystemGroup`.

`CombatScopeVfxCatalog` — unmanaged `IComponentData`; added to scope entities
by `CombatVfxRoot.Bind`; holds only `int VfxRootId` — the opaque key used by
`CombatVfxDispatchSystem` to look up the owning `CombatVfxRoot` from the static
registry. No managed reference crosses the ECS boundary.

---

## Emitting Sources

### `ProjectileLifetimeSystem`

Enqueues `Trigger=2` (expire) for every projectile whose lifetime hits zero.
Position is `CombatKinematicsComponent.Position` at the frame of expiry.

### `ProjectileCollisionSystem`

- Enqueues `Trigger=1` (hit) for each confirmed projectile-target collision.
- Enqueues `Trigger=2` (expire) on the deactivation path (pierce consumed,
  null scope, or zero lifetime). Position is `kinematics.Position` at
  deactivation time.

`Trigger=2` can fire from both `ProjectileLifetimeSystem` and
`ProjectileCollisionSystem`. Each deactivation path fires exactly once per
entity per lifetime — no dedup needed.

### `AoeCollisionSystem`

Enqueues `Trigger=1` (hit) for each confirmed AOE-target collision.
Position is the AOE center (`CombatKinematicsComponent.Position`), not the
target position.

### `AoeSpawnSystem`

Enqueues `Trigger=0` (spawn) when an AOE spawn request is materialized.
Position is the AOE center.

### `AoeLifetimeSystem` (`Assets/Scripts/System/Aoe/AoeLifetimeSystem.cs`)

Two `IJobEntity` jobs, both scheduled inside this system:

- `AoeLifetimeJob` — ticks `RemainingLifetime` on lingering AOEs (`IsPulse==0`
  with `RemainingLifetime > 0`). On expire: disables `AoeActiveTag`, enqueues
  `AoePendingRecycle` + `Trigger=2` (expire).
- `AoePulseVfxJob` — ticks `AoePulseVfxComponent.RemainingInterval` on all
  active AOEs with `Interval > 0` and `IsPulse==0`. On interval fire: resets
  remaining interval, enqueues `Trigger=3` (pulse). Position is AOE center.

`AoePulseVfxJob` is chained after `AoeLifetimeJob` (both write to the same
`NativeQueue<VfxPendingSpawn>.ParallelWriter`; concurrent scheduling is not
safe).

Runs after `AoeSpawnSystem`, before `AoeCollisionSystem`.

---

## Authoring

### Projectile VFX slots (`BasicAttackPrefab`)

```
spawnEffect   → Trigger 0 (reserved)
hitEffect     → Trigger 1 (fires on each target hit)
expireEffect  → Trigger 2 (fires on lifetime end or collision deactivation)
```

All three fields are optional. A null field means no VFX for that trigger.

### AOE VFX slots (`BasicAoePrefab`)

```
spawnEffect   → Trigger 0 (fires when the AOE spawns at its center)
hitEffect     → Trigger 1 (fires on each target hit)
expireEffect  → Trigger 2 (fires on lifetime end, lingering AOEs only)
pulseEffect   → Trigger 3 (fires on interval tick, lingering AOEs only)
```

All four fields are optional and live on the AOE template prefab's
`BasicAoePrefab`, beside the required `Hurtbox` reference and optional `Visual`
debug sprite reference.

Each assigned AOE VFX Graph asset must also expose `AreaSize` so the runtime
can scale particle size to match the gameplay AOE area size. See
[AOE VFX size normalization](#aoe-vfx-size-normalization).
`AoeConfig.CreateTypeDefinition` forwards them into the registered
`AoeTypeDefinition`; `pulseEffect` is forwarded only when
`AoeConfig.lifetimeSeconds > 0`. Pulse AOEs (`IsPulse==1`) do not fire Trigger 2
or 3; their deactivation is handled by `AoeCollisionSystem`.

Direct code-side `AoeTypeDefinition` registration can still assign the same four
slots directly.

### `AoePulseVfxComponent`

Added to every AOE entity archetype at creation time. For pulse AOEs
(`IsPulse==1`) and any AOE with `Interval <= 0`, `AoePulseVfxJob` is a
no-op. Set from `RepeatHitCooldownSeconds` on the spawn request.

---

## Root Wiring

### `CombatVfxRoot`

- `Awake`: auto-registers in static registry; creates owned `CombatVfxDispatcher`
- `Register(typeId, trigger, asset, ...)`: delegates to dispatcher; called by
  registerers (`PlayerSkillDriver`, `MobProjectileAttack`) after each
  `RegisterTemplate` / `RegisterType` call
- `Bind(scopeEntity, entityManager)`: upserts `CombatScopeVfxCatalog` onto a
  scope entity; called from `PlayerSkillDriver.Start` and `BindAoeRoot` for
  each bound scope
- `ResetDispatcher()`: disposes dispatcher and creates a new one; call when
  type registry resets (e.g., scene reload)
- `OnDestroy`: removes from registry; disposes dispatcher

### `CombatVfxDispatchSystem`

- `OnCreate`: builds `EntityQuery` for `CombatScopeVfxCatalog`
- `OnUpdate` (runs in `PresentationSystemGroup`):
  1. `CompleteDependency()` — ensures all `VfxFlushJob` completions are visible
  2. Iterates scope entities; resolves `CombatVfxRoot` from static registry
  3. Calls `root.DrainAndDispatch(buffer)` per scope

`CombatRoot` does not own or reference `CombatVfxDispatcher`. Its `BindWorld`
method still adds the `VfxSpawnRequestElement` buffer to the shared scope
entity (needed by simulation jobs), but drain and dispatch are handled
entirely by `CombatVfxDispatchSystem`.

---

## Scope Buffers

`VfxSpawnRequestElement` is added to the scope entity at root setup alongside
the existing hit buffers:

- `CombatRoot`: added once in `BindWorld`, on the single shared `CombatScope`
  entity serving both the projectile and AOE domains

Systems write to the buffer via `VfxFlushJob` using `BufferLookup<VfxSpawnRequestElement>`.
`CombatVfxDispatchSystem` drains and clears the buffer each `PresentationSystemGroup`
tick via `CombatVfxRoot.DrainAndDispatch`. Roots do not touch the buffer after
`BindWorld`.

---

## VFX Graph Authoring Pitfalls

### Per-particle buffer indexing — do not use Get Spawn Index (Source)

When sampling a `GraphicsBuffer` per-particle inside an Initialize Particle
context driven by a Single Burst Spawn block, `Get Spawn Index` with Location
`Source` returns the **spawn event index** — the same value for every particle
in that burst. All particles in the burst therefore sample `buffer[eventIndex]`
and spawn at the same world position, appearing stacked.

**Correct pattern:** use `particleId % SpawnCount` as the buffer index.

```
Get Attribute: particleId  (Current)
    ↓
Modulo (%)  ←  SpawnCount  (property)
    ↓
Sample Graphics Buffer → Index
```

`particleId` is unique per particle across all time. Because each burst spawns
exactly `SpawnCount` particles with consecutive IDs, `particleId % SpawnCount`
resolves to `0, 1, 2 ... SpawnCount-1` for every burst regardless of how many
bursts have already fired.

### VisualEffect GO must be at world origin for world-space position buffers

`BasicColorBlobVfx` and any graph authored against this runtime use **world
simulation space**. Positions in the `Positions` buffer are absolute world-space
`float2` coordinates. The `VisualEffect` component's GO must sit at world origin
`(0, 0, z)` so that the graph's local-space transform does not offset the
spawned particles.

`CombatVfxDispatcher` enforces this by resetting the VFX GO position to
`(0, 0, z)` in every `Dispatch()` call. Any standalone MonoBehaviour that owns
its own `VisualEffect` must do the same before calling `SendEvent`, or must
create a dedicated child GO at world origin rather than attaching the component
to a moving actor.

Symptom of violation: particles appear at `2 × actor_position` rather than
around the actor, or at world origin when the actor is off-origin.

---

## Performance Notes

- `GraphicsBuffer` for each registered `(typeId, trigger)`: `maxPerFrame * 8` bytes on GPU.
  Default cap is 2048 events/trigger, 16 KB per slot.
- `NativeList<float2>` staging: `maxPerFrame * 8` bytes on CPU heap (Persistent allocator).
  Allocated once at registration, cleared each frame, never reallocated at runtime.
- Main-thread cost in `Dispatch()` is O(registered effect types), not O(events).
  Per-type cost: one `SetData`, one `SetGraphicsBuffer`, one `SetInt`, one `SendEvent`.
- Trigger=2 (expire) can originate from two systems in the same frame for the same
  projectile (LifetimeSystem and CollisionSystem deactivation paths). Both fire;
  the VFX cap absorbs extras within the same frame.
