# ECS Combat Rewrite — Design Document

Status: design (pre-implementation)
Source: condensed from `.agent/rewrite/context.md`
Next: this doc + existing code/docs → rewrite plan (step 3)

---

## 1. Goals & Non-Goals

### Goal
Architecture clarity. The current spawn/event/damage systems were built with incomplete knowledge of DOTS and accumulated jank. They are hard to extend, debug, and reason about. This rewrite replaces that cluster with a phase-oriented pipeline with explicit ownership boundaries.

### Secondary (constraints, not drivers)
Performance, authoring ergonomics, deterministic-enough behavior, debuggability. These benefit from clarity but do not justify added complexity on their own.

### In scope
- Event / data flow (buffers, streams, ownership, ordering)
- Spawn / despawn / structural changes (spawning, dead-slot reuse, ECB usage)
- Hit / damage application (collision consequences, damage replay, target bridge)
- Lifetime unification

### Out of scope (kept as-is)
- Projectile movement, lifetime math, collision math — already simple, math-heavy, architecturally sound
- Rendering / VFX dispatch
- Authoring / ScriptableObject definitions
- Existing target-bridge structure (refine the boundary, do not rebuild)

---

## 2. Core Architectural Rules

These rules are invariant. Every system in the rewrite must obey them.

### R1 — Snapshot at spawn
All data a runtime system will ever need is fat-copied into plain unmanaged fields at spawn time. Once an entity exists, no ECS system asks an authoring object what the entity means. No `GameObject`, `Transform`, `Collider2D`, managed attack object, managed callback, or centralized mutable definition store is touched on the hot path.

Rationale: lifetime safety (in-flight entities survive destruction/mutation of their authoring source), determinism (jobs operate on immutable plain data), Burst/job compatibility, cache friendliness, testability.

> Snapshot data is the runtime contract. A spawned entity already carries everything needed to finish its lifetime, spawn children, emit impact effects, and produce damage-replay data.

### R2 — Component presence = behavior presence
Optional behavior is encoded as separate components, never as flags inside one fat component. If an entity can do timed spawn, it has a `TimedProjectileSpawn`. Systems query only entities that have the relevant component; they never scan a giant snapshot and branch on flags.

### R3 — Event vs Command
- **SpawnEvent** = gameplay intent. May describe a volley / burst / fan / ring / scatter / timed child. One event ⇒ 0..N entities.
- **SpawnCommand** = allocation intent. Exactly one concrete entity. No multiplicity semantics.

> Events may be abstract. Commands may not. Only commands reach the reuse/allocation phase.

### R4 — Phase separation of concerns
A system does not both interpret gameplay intent and solve entity allocation. Producers do not care about allocation. Expansion does not care about reuse. Apply does not care about volley semantics. Each phase has one clear input and one clear output.

### R5 — Spawn is a next-tick effect
Events produced during tick N are expanded and materialized during tick N, but the resulting entities do not participate in simulation until tick N+1. Enforced by **system order only** — no `SpawnedThisTick` marker, no delayed-activation queue. This keeps the simulation pipeline acyclic and prevents same-tick recursive chains.

### R6 — No managed access in simulation
Burst/job simulation systems use only unmanaged ECS data and `Entity` references. A target proxy may carry a managed companion reference, but the **only** approved reader is the damage dispatch bridge (see §8).

### R7 — Unordered transport
Spawn queues are unordered. Systems must not rely on production/expansion/reuse/creation order. Correctness comes from command data, not queue position. Order metadata, if it exists, is debug-only.

---

## 3. Runtime Occupancy & Slot Reuse

```csharp
public struct Active : IComponentData, IEnableableComponent {}
```

- Generic occupancy flag shared by all reusable runtime entities (projectile, AoE, future types).
- **Entity kind** is defined by its marker/data components. **Slot availability** is defined by the enabled state of `Active`.
- Death/lifetime does not destroy entities — it disables `Active`.
- Reuse query: `WithDisabled<Active>()` + the required archetype components. This matches chunks that *have* the components but where `Active` is disabled. Not a blind full-active scan.
- **No external free-list, no pooling registry.** The query is the source of truth. Add a free-list only if profiling later proves the query path insufficient.

> `Active` is the occupancy flag. Component presence defines the slot kind. Enabled state defines whether the slot is alive.

Exception — target proxies (§8) are **deleted immediately** when they stop simulating, not disabled/pooled. They are not reusable slots.

---

## 4. Lifetime Unification

Replace `ProjectileLifetimeSystem` + `AoeLifetimeSystem` with one generic system:

```
for each entity with (Lifetime, Active):
    Lifetime.Remaining -= dt
    if Lifetime.Remaining <= 0:
        SetComponentEnabled<Active>(entity, false)
```

The logic was never projectile- or AoE-specific; the split was incidental.

---

## 5. Spawn Pipeline

### 5.1 Stage shape (per spawnable entity domain)

```
Producer Systems (parallel)
  └─ NativeQueue<SpawnEvent>.ParallelWriter

[producer jobs complete]

Event Finalize
  └─ NativeQueue<SpawnEvent> → NativeArray<SpawnEvent>

SpawnExpansionSystem
  ├─ reads event array (parallel, non-overlapping ranges)
  ├─ resolves ALL per-entity math (count, spread, jitter, direction, position, rotation)
  ├─ strips one recursive spawn layer (see 5.4)
  └─ routes each result to the correct exact-shape command queue
        NativeQueue<SpawnCommand>.ParallelWriter

[expansion jobs complete]

Command Finalize (per shape)
  └─ NativeQueue<SpawnCommand> → NativeArray<SpawnCommand>

SpawnApplySystem (per shape)
  ├─ reads command array by index ranges
  ├─ query disabled-Active slots of the matching component set
  ├─ overwrite component data, enable Active
  └─ ECB create + AddComponent for remaining commands (overflow path)
```

### 5.2 Typed by what is spawned

Streams are typed by the entity being materialized, not by why or who produced it. Multiple producers write the same stream; the consumer/apply system is specific to the spawned type.

```
ProjectileSpawnEvent ── TimedProjectileSpawnSystem ─┐
                        ImpactProjectileConsequence ─┤→ ProjectileSpawnExpansion → ...
                        (future producers) ─────────┘

AoeSpawnEvent ──────── TimedAoeSpawnSystem ─────────┐
                        ImpactAoeConsequence ───────┤→ AoeSpawnExpansion → ...
                        (future producers) ─────────┘
```

Rationale: spawning different entity types is where logic genuinely diverges. Forcing them into one generic `SpawnEvent` + `switch(kind)` only defers the branch. Separate components/systems is more idiomatic ECS.

### 5.3 Exact-shape command queues

Expansion classifies each result into an **exact component-set** command queue. Each unique shape gets its own queue and its own apply system.

```
BasicProjectileCommandQueue       → BasicProjectileSpawnApplySystem
ImpactAoeProjectileCommandQueue   → ImpactAoeProjectileSpawnApplySystem
TimedChildProjectileCommandQueue  → TimedChildProjectileSpawnApplySystem
TimedImpactProjectileCommandQueue → TimedImpactProjectileSpawnApplySystem
...
```

Invariant: **command queue == required component set == dead-slot query shape == overflow creation shape.**

Start explicit. Generalize (merge shapes) only after real duplication appears — not on speculation. Shape is selected during **expansion**, never re-derived from a mask in apply.

### 5.4 Expansion owns all spawn math

Expansion resolves count, spread, jitter, final direction, final spawn position, final rotation, and any per-entity randomized variation. Apply only copies resolved fields into components.

> If a spawn apply system ever sees `Count`, `SpreadDegrees`, `JitterDegrees`, or `JitterSeed`, the phase boundary has leaked.

`ProjectileSpawnEvent` and `ProjectileSpawnCommand` are structurally similar; the event additionally carries multiplicity fields (`Count`, `SpreadDegrees`, `JitterDegrees`, `JitterSeed`, …) that expansion consumes and discards.

### 5.5 Recursive spawn — enforced by type shape

Recursion depth is bounded by struct layout, not a runtime counter. Spawn payloads are fixed-depth nested structs with no unbounded child list and no self-reference. Each spawn system strips the top layer when emitting the child event, so the remaining nested depth strictly decreases.

```
Initial:  Entity → SpawnCondition → Entity → SpawnCondition → Entity
After 1:  Entity → SpawnCondition → Entity
After 2:  Entity
```

> Recursive spawn is not runtime-recursive. Deeper recursion is impossible by construction because the struct cannot express it.

### 5.6 Spawn condition components — separate by trigger AND output

No generic `SpawnKind` enum, no shared component that branches between projectile/AoE.

```csharp
public struct TimedProjectileSpawn : IComponentData
{
    public float Interval;
    public float TimeUntilNext;
    public ProjectileSpawnSnapshotDepth1 ChildProjectile; // one stripped layer
}

public struct TimedAoeSpawn : IComponentData
{
    public float Interval;
    public float TimeUntilNext;
    public AoeSpawnSnapshotDepth1 ChildAoe;
}

public struct ImpactProjectileSpawn : IComponentData { public ProjectileSpawnSnapshotDepth1 ChildProjectile; }
public struct ImpactAoeSpawn        : IComponentData { public AoeSpawnSnapshotDepth1 ChildAoe; }
```

### 5.7 AoE uses the identical pipeline

AoE will gain timed child spawns and scatter-style expansion. It is not a shortcut path — it follows the same event → expansion → exact-shape command → apply structure as projectiles. Projectile and AoE differ in **data and component shape, not in pipeline structure**.

### 5.8 Overflow creation

When no matching disabled slot exists, apply creates the entity via ECB:

```
ECB.CreateEntity()
ECB.AddComponent<Active>()
ECB.AddComponent<...core components...>()
ECB.AddComponent<...optional components for this exact shape...>()
```

Reuse is the normal path; ECB creation is the exceptional overflow path. Overflow is still a structural change — the point is that it is isolated to overflow and handled in a clear, batched, Unity-native way, not that structural change disappears. No prototype/prefab entities unless profiling proves they matter.

---

## 6. Native Container Ownership

The hot-path event/command transport uses native containers (not singleton dynamic buffers) because many jobs write in parallel. Singleton entities may still exist as phase/ownership markers, but the high-volume data path is native.

| Container | Writer responsibility | Reader responsibility |
|---|---|---|
| `NativeQueue<T>` | assert empty, then `ParallelWriter` writes | convert to `NativeArray`, then **`Clear()`** |
| `NativeStream` | **create** at start of write phase | **dispose** after read phase |

Phase boundary contract:

```
parallel write phase → producer jobs complete
  → finalize: queue → NativeArray (frozen)
  → parallel read phase by non-overlapping index ranges
```

> Queues are for parallel append. Arrays are for parallel consume. No system reads a queue still being written. No system mutates command data after it is finalized into an array. No container crosses a phase boundary partially consumed.

Ownership model: keep the **current implementation style** (system-owned containers), but enforce the stricter phase rules above. Do not introduce a central pipeline-state god object unless ownership becomes a proven problem.

---

## 7. Collision & Consequence

### 7.1 Roles

**ContactGateSystem** (`ProjectileContactGateSystem`, `AoeContactGateSystem`) — kept. Role narrowed to **gate state maintenance only**: tick cooldowns, expire stale contact entries, clear invalid state.

**CollisionSystem** (`ProjectileCollisionSystem`, `AoeCollisionSystem`) — kept, refined output. Detects overlap, checks the contact gate **inline** (gating is part of deciding "is this a real hit this tick"), and emits **final typed consequence events**. May disable the source entity's `Active` immediately if the hit consumes it.

### 7.2 Collision emits final, separate consequence events

Collision writes one typed stream per consequence — not a single generic qualified-hit stream (avoids every consequence system re-scanning the same list). It reads the consequence snapshot already on the entity and writes the final event directly.

```
ProjectileCollisionSystem, on qualified hit:
  ContactDamage present        → DamageReplayEvent
  ImpactAoeSpawn present       → AoeSpawnEvent
  ImpactProjectileSpawn present → ProjectileSpawnEvent
  (VfxHit later)
```

### 7.3 Boundary

```
Collision MAY:                          Collision MAY NOT:
  qualify the hit (gate inline)           apply the damage callback
  update pierce/hit-count/contact state   directly spawn child entities
  disable source Active if consumed       mutate GameObject / scene objects
  emit final consequence events           call any managed callback
```

> Collision is the final producer of consequence events, not the executor of consequences. Emitting an `AoeSpawnEvent` is fine; it is still plain data that goes through expansion and apply. Spawning the AoE inside collision is not.

The source projectile's own state transition (consumed-on-hit, pierce, expiry) is **not** a consequence event — it is the source entity mutating itself, so collision owns it directly. AoE collision reuses existing logic and additionally emits these final consequence events.

---

## 8. Damage & Target Bridge

### 8.1 Damage replay is atomic
Every qualified hit produces its own `DamageReplayEvent`. No per-target-per-tick condensation in this rewrite. Preserves semantics for on-hit effects, lifesteal, procs, hit counters, "gain X on hit". Condensation may be added later as an explicit bridge optimization, but must not silently change gameplay semantics.

```csharp
public struct DamageReplayEvent
{
    public Entity        TargetProxy;
    public DamageSnapshot Damage;
    public float2        HitPosition;
    public float2        HitDirection;
}
```

Storage: `NativeQueue<DamageReplayEvent>.ParallelWriter` during simulation → `NativeArray` → dispatch. Consistent with the spawn/consequence transport rules (§6).

### 8.2 Target proxy entities
Each GameObject target has an ECS proxy entity — the unmanaged identity used by simulation (collision, spatial queries, hit/damage event references). The proxy carries:

```
TargetPosition
TargetCollisionShape
TargetAlive / Team / Faction (as needed)
managed companion reference (restricted — see 8.4)
```

The GameObject pushes its own transform/state into the proxy during `Update()`.

### 8.3 Proxy lifecycle — owned by the GameObject
The target MonoBehaviour creates/registers the proxy on enable, pushes position/state each `Update()`, and **deletes the proxy immediately** when it should stop participating in simulation. Not disabled, not pooled.

Death follows from managed damage callbacks on the GameObject, so the GameObject is the lifecycle authority. Scene lifetime ≠ simulation lifetime: a GameObject may stay in the scene for death animation/VFX after its proxy is deleted, but it is no longer targetable.

Deletion is safe because of frame ordering (§9): all ECS events referencing the proxy are resolved before deletion. Any event referencing a deleted proxy is a lifecycle-violation bug, not normal flow.

### 8.4 Managed companion access is a controlled boundary
The proxy may hold a managed reference to its GameObject target. The **only** approved reader is the damage dispatch bridge.

```
Allowed:    DamageReplayEvent.TargetProxy → DamageDispatchBridge → managed companion → GameObject callback
Forbidden:  Collision / Tracking / Spawn / VFX / any simulation system → managed companion
```

> The ECS combat pipeline detects and describes damage. For now it does not own the full target lifecycle — scene targets remain the authority for animation-driven behavior. Hybrid (ECS owns simple health, GO owns animation/UI/death) is a possible later direction, not part of this rewrite.

---

## 9. Frame & Phase Order

System order is part of the architecture, not incidental scheduling.

```
MonoBehaviour Update()
  • live targets push position/collision state into ECS
  • targets leaving simulation delete their proxy entity (before simulation runs)

ECS Simulation
  9.1  Lifetime / Active update
  9.2  Timed spawn event production
  9.3  Projectile tracking / movement
  9.4  Projectile collision   (+ contact gate inline)
  9.5  AoE collision          (+ contact gate inline)
  9.6  Contact gate state maintenance (cooldown tick / expiry)
  9.7  Spawn event finalize    (queue → array)
  9.8  Spawn expansion         (events → commands, all math, layer strip)
  9.9  Spawn apply             (reuse disabled slots / ECB overflow)
  9.10 Damage replay export    (NativeArray ready)

Damage Dispatch (main thread, same Update frame)
  • DamageDispatchBridge: array → managed companion → GameObject callback

MonoBehaviour LateUpdate()
  • death animation transitions
  • scene cleanup / GameObject destruction
  • no unresolved ECS events remain
```

Next-tick spawn (R5) is guaranteed because all simulation phases that could process new entities (9.1–9.6) run before spawn apply (9.9). Local `[UpdateBefore]`/`[UpdateAfter]` remain useful, but this phase order is the contract.

---

## 10. Time Model

Variable `deltaTime`. No fixed simulation tick.

Timed spawn catches up fully: if a frame's `deltaTime` spans multiple intervals, the timed-spawn system emits all due spawn events that frame. Burst cost accepted unless profiling later proves a cap is needed.

> Catch-up applies to **event production** only. Even when a timed spawn emits multiple events in one frame, the spawned entities do not move, collide, or emit their own spawns until the next simulation update (R5 still holds).

---

## 11. System Inventory

### Stays (kept / refined)
| System | Change |
|---|---|
| `ProjectileMovementSystem` | none |
| `ProjectileTrackingSystem` | none |
| `ProjectileCollisionSystem` | emit final typed consequence events; gate inline |
| `AoeCollisionSystem` | same as above |
| `ProjectileContactGateSystem` | role narrowed to gate-state maintenance |
| `AoeContactGateSystem` | same |
| Rendering / VFX dispatch | none |
| Authoring / ScriptableObjects | none |
| `CombatTargetRegistry`, `CombatTargetSync`, `CombatTargetSyncSystem`, `CombatTargetSet`, `ICombatTarget` | refine boundary, do not rebuild |

### Rewritten / replaced
| Current | Becomes |
|---|---|
| `ProjectileSpawnSystem`, `AoeSpawnSystem`, `ProjectileChildSpawnSystem`, `ProjectileMultiExpandSystem` | typed event → expansion → exact-shape apply pipeline (§5) |
| `ProjectileSpawnCommand` | split into `ProjectileSpawnEvent` / `ProjectileSpawnCommand` (R3) |
| `CombatSpawnConvertJob` | folded into expansion/apply stages |
| `CombatHitElement`, `CombatHitDispatchSystem`, `CombatHitFlushJob` | typed consequence streams + `DamageDispatchBridge` (§7–8) |
| `ProjectileLifetimeSystem` + `AoeLifetimeSystem` | unified generic lifetime system (§4) |

---

## 12. Open Items (for step 3 / future)

- Free-list reuse — only if query-based reuse profiles poorly.
- Damage condensation at the managed bridge — explicit later optimization.
- Hybrid target model (ECS-owned health) — later direction.
- VFX/audio consequence streams — slot exists in §7.2, not designed yet.
- Which current file is the "main source of truth for the existing spawn mess" (candidates: `ProjectileSpawnSystem.cs`, `AoeSpawnSystem.cs`, `ProjectileChildSpawnSystem.cs`, `CombatSpawnConvertJob.cs`, `ProjectileSpawnCommand.cs`) — resolve when grounding the rewrite plan against real code.
