# Implementation Context

Compact shared context for every task executor in this plan. Source of truth
is `index.md`; this file only condenses it.

## Architectural Decisions
- New `VfxDataShape.ProjectileTrail = 3` for stateful projectile ribbons; `LineSegment = 2` stays for targeted chain links. No dual projectile-trail path.
- `ulong TrailKey` allocated once per trail in `ProjectileSpawnExpansionJob`, persisted across pool reuse, never `ProjectileId`.
- One visual command path: `ProjectileTrailVfxComponent` owns key/sequence/started/width/step/last-position. `Begin`→`Append`(distance-gated)→`End`(always, on lifetime or either collision death path).
- `ProjectileTrailVfxDefinition` (graph, MaxStrips, ParticlesPerStrip, PointLifetimeSeconds) is the one per-graph authored contract; root rejects two definitions sharing a graph with different settings.
- One native queue + existing `ProducerHandle`; bucket by VFX id, sort each graph slice by `(TrailKey, Sequence)`; one upload/compute/`OnSpawn` per graph per frame.
- GPU owns the key->slot hash table, free-slot stack, and strip state; CPU never allocates/recycles slots. Invalid resolution = `uint.MaxValue`, checked before VFX Graph reserves a strip point.
- Cleanup runs every presentation frame (including zero-command frames); releases a closing slot only after `PointLifetimeSeconds` plus a verified VFX-init latency margin, in the same visual time domain as the graph.

## Global Invariants
- VFX requests are visual-only; simulation jobs never call managed VFX objects.
- One `VisualEffect` per shared graph; one `OnSpawn` batch per graph per frame.
- Request GraphicsBuffers are transient; only `Initialize Particles`/`Initialize Particle Strip` may sample them, copying into persistent attributes. `Update`/`Output` never sample a request buffer.
- `NativeQueue` producer order is unspecified; explicit per-key sequence + sort restores order; all producer handles complete before the bucket/drain job.
- Projectile pool reuse rewrites `ProjectileTrailVfxComponent`; new key + unstarted state assigned on every activation.
- Lifetime can kill before movement (no commands emitted); both discrete and continuous collision death paths call one shared trail-end helper before `Active` is disabled.
- VFX Graph 17.4's `Initialize Particle Strip` rejects a point before reservation when `stripIndex >= STRIP_COUNT` (confirmed: context has a plain `stripIndex` input slot, no special dynamic-mode toggle needed).

## Ownership Boundaries
- Producers (movement/lifetime/collision systems) only enqueue `ProjectileTrailVfxEvent`; they never touch compute buffers or `VisualEffect`.
- Presentation layer (root/dispatcher/compute) owns bucketing, sorting, upload, compute dispatch, and the one `SendEvent` per graph.
- `ProjectileTrailVfxDefinition` is the single owner of per-graph capacity/lifetime settings; the graph's static strip/particle counts must match it.

## Data Flow
`ProjectileMovementSystem` / lifetime / collision -> `ProjectileTrailVfxEvent{VfxId, GpuCommand}` native queue (shared `ProducerHandle`) -> bucket by VFX id -> sort by `(TrailKey, Sequence)` within each graph's slice, building key-group ranges in the same pass -> one upload per graph -> one compute dispatch (hash table claim/lookup/close + write `ResolvedStripIndices`/`TrailPositions`/`TrailWidths`) -> one `OnSpawn` per graph -> `Initialize Particle Strip` samples this batch's buffers by `spawnIndex`/`stripIndex` only.

## Lifecycle / Allocation Rules
- `Begin` claims a free GPU slot; `Append` looks the key up; `End` marks the slot closing (not freed).
- Compute cleanup ticks every frame and frees a closing slot only after its final point's `PointLifetimeSeconds` has elapsed in visual time **and** a verified init-latency margin has passed. Graph must `Always Simulate` (or allocator/graph share pause) unless a proven synchronized-pause alternative exists.
- A failed `Begin` (allocator full) drops that trail until its `End`; no occupied slot is ever stolen.
- `GpuCommand` is 32 bytes, stride divisible by 16: key low/high, sequence, kind, position, width, padding.

## ECS / Job / Threading Constraints
- `TrailKey` allocation is single-threaded, inside `ProjectileSpawnExpansionJob`, using a persistent native counter on the expansion singleton; reject counter wrap rather than reuse.
- GPU hash table: one thread per key group sequentially (so `Begin`/`Append`/`End` for one key never race each other); different key groups may run in parallel.

## Determinism Requirements
- Not explicitly a determinism-sensitive path per index.md (visual-only), but ordering within a key must be exact: sort key is `(TrailKey, Sequence)`, and `Sequence` is assigned by the single-threaded producer, not derived from queue order.

## Producer / Consumer Separation
- Simulation systems are producers only (native queue writes). Presentation (root + compute) is the sole consumer: draining, bucketing, uploading, dispatching compute, and sending the graph event.

## Reused Mechanisms
- Encoded `VfxId` / `VfxDataShapeTable` (see `Assets/Scripts/System/Vfx/VfxDataShapes.cs`), shared native VFX queue + `ProducerHandle`, counting-sort graph buckets, one root-owned resource per graph (`CombatVfxRoot`/`AoeVfxResourcesBase` pattern in `Assets/Scripts/System/Vfx/CombatVfxRoot.cs` and `CombatAoeVfxDispatcher.cs`), dispatcher contract validation (`ValidateGraphContract`), transient-buffer-to-particle-attribute copying convention.

## Introduced Mechanisms
- `VfxDataShape.ProjectileTrail` shape + buffer contract.
- GPU-persistent compute allocator: full-key hash table, free-slot stack, per-slot strip state, cleanup pass.
- `ProjectileTrailVfxDefinition` asset (graph + MaxStrips + ParticlesPerStrip + PointLifetimeSeconds), one owner for per-graph lifetime/capacity.

## Validation Requirements
- Task 001 must empirically establish, on target graphics APIs, that a compute write immediately before `SendEvent` is visible to that batch's `Initialize Particle Strip`, that an invalid (`uint.MaxValue`) resolved index never touches a live strip, and that a later batch never rewrites an older batch's still-alive points. This is proven in a disposable `Assets/Tests/` fixture, not the production graph.
- Findings (safe reuse delay, culled/paused behavior, same-frame ordering) must be recorded back into `index.md` before later tasks rely on them; a failed assumption means revising the plan, not improvising around it.
- Later tasks: capacity/hash-table/no-request-frame checks as listed in index.md's "Design validation and gates" section (not condensed further here — re-read that section directly when starting tasks 004-007).

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionJob.cs` / `ProjectileSpawnExpansionSystem.cs` (`ProjectileIdFor`)
- `Assets/Scripts/System/Projectiles/ProjectileMovementSystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs`, `ProjectileContinuousCollisionSystem.cs`
- `Assets/Scripts/System/Lifetime/CombatLifetimeSystem.cs`
- `Assets/Scripts/System/Vfx/VfxDataShapes.cs`, `CombatVfxRoot.cs`, `CombatAoeVfxDispatcher.cs`, `CombatAoeVfxDispatchSystem.cs`
- `ProjectileDiscreteSpawnApplySystem.cs` (pool reuse component rewrite)
- Installed package source: `com.unity.visualeffectgraph@17.4.0`'s `VFXBasicInitialize.cs`, `VFXInit.template`, `UpdateStrips.compute`
