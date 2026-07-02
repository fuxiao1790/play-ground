# Render Instance Buffers — drop the `CombatRenderElement` component, prepare matrices into per-batch native buffers

## Summary

The object-to-world matrix is currently stored as a per-entity ECS component
`CombatRenderElement : IComponentData { Matrix4x4 objectToWorld; }`.
`CombatBatchedRenderSystem` gathers it per render batch with
`ToComponentDataArray<CombatRenderElement>(Allocator.Temp)` — a full O(active
entities) copy every frame — before handing it to `Graphics.RenderMeshInstanced`.

This plan removes the component and has `CombatRenderPrepareSystem` compute
matrices **directly into dense, per-batch `NativeList<Matrix4x4>` buffers**
(compacting only enabled entities as it goes). The render system submits
`list.AsArray()` with **zero copy**. The existing `OrderFirst` prepare / later
submit overlap is preserved via the producer-handle pattern already used
elsewhere in the project.

## Problem / motivation

- The `ToComponentDataArray` copy is not gratuitous: `CombatRenderActiveTag` is
  enableable and dead/pooled entities share chunks with active ones, so the
  gather *also* compacts the enabled subset into a contiguous array. Any
  zero-copy path must still perform that compaction.
- Because the matrix is a per-entity component, the pipeline pays twice: once to
  write the matrix into chunk storage (prepare) and again to memcpy it out
  (render). Storing the matrix as a component is the root cause the user called
  out.
- Collapsing "compute the matrix" and "produce the contiguous instance buffer"
  into a single pass removes an entire O(active) memcpy per frame and removes a
  64-byte-per-entity component from every projectile/AOE archetype (raising chunk
  capacity, shrinking spawn work).

## Constraints & invariants the change must respect

- **Enableable compaction** — dead/pooled entities are disabled
  (`CombatRenderActiveTag` off, `Active` off) and remain in chunks; the produced
  buffer must contain only enabled entities. Source: `CombatRenderActiveTag`
  is `IEnableableComponent` (`CombatRenderComponents.cs:30`); dead-slot reuse
  queries filter `WithDisabled<Active>` (`ProjectileSpawnApplySystem.cs:418`).
- **Prepare/submit overlap** — `CombatRenderPrepareSystem` runs
  `PresentationSystemGroup` `OrderFirst = true` and leaves its job in flight so it
  overlaps other presentation systems' main-thread work; the render system
  consumes later. This must not regress into a synchronous complete-in-prepare.
  Source: `.agent/render-prepare-overlap/index.md`, `CombatRenderPrepareSystem.cs:8`.
- **Native/ECS handle ownership** — whoever allocates a `NativeList` owns
  disposal: create lazily, complete producers, dispose in `OnDestroy`. Source:
  `Docs/coding-standards.md` "Native And ECS Handle Ownership"; exemplar
  `CombatApplyFinalizeSystem.cs` (HitQueue), `ProjectileSpawnExpansionSystem.cs`
  (command lists + `PendingHandle`).
- **Filter-mutation hazard** — a single `EntityQuery` whose
  `SetSharedComponentFilter` is mutated while deferred jobs referencing it are in
  flight is unsafe. The spawn systems avoid it by `Complete()`-ing each per-key
  job before the next; prepare wants concurrency, so it must not share one
  mutating query. Source: spawn reuse pattern in
  `ProjectileSpawnApplySystem.cs` / `AoeSpawnApplySystem.cs`.
- **Render is read-only over simulation** — the render/prepare path may read
  simulation components and write presentation buffers only; it must not mutate
  simulation state. Source: `Docs/reference/.../presentation-and-feedback.md`.
- **Batch id semantics** — `CombatRenderBatchId.Value` == `RenderTypeId`, minted
  from a single incrementing counter in `CombatRenderResourceRegistry.Register`
  (`_nextRenderId++`), so every id is globally unique and belongs to exactly one
  kind (projectile **or** aoe, never both). Source: `CombatRenderComponents.cs:74`;
  register call sites in `CombatRoot.cs`.

## Mechanisms reused vs introduced

- **Reused — producer-handle overlap.** Prepare exposes an
  `internal JobHandle PendingHandle` and the render system completes it before
  consuming, exactly as `ProjectileSpawnExpansionSystem.PendingHandle` is
  completed by its apply systems and `CombatApplyFinalizeSystem` completes its
  queue producers. No new synchronization concept.
- **Reused — cross-system handshake.** Render obtains the prepare system via
  `World.GetExistingSystemManaged<CombatRenderPrepareSystem>()` (cached in
  `OnCreate`), mirroring how apply systems reach
  `ProjectileSpawnExpansionSystem`'s command containers.
- **Reused — the existing registry** as the source of truth for which batch ids
  exist; it gains a `Kind` field so render picks the right telemetry counter and
  prepare needs only one query per id.
- **Introduced — per-batch persistent `NativeList<Matrix4x4>` + dedicated
  per-batch `EntityQuery`** owned by the prepare system. New, but it *removes* a
  component and a copy rather than adding a parallel data path (see comparison).

## Design validation (against each invariant)

- *Enableable compaction* → the prepare job iterates with
  `ChunkEntityEnumerator(useEnabledMask, ...)` and `AddNoResize` only enabled
  entities, so the list is exactly the active set. ✔
- *Overlap* → prepare only `Clear`s/sizes lists and `ScheduleParallel`s on the
  main thread, then returns without completing; the matrix math runs on workers
  during subsequent presentation systems. ✔
- *Handle ownership* → lists/queries created lazily per batch id, disposed in
  prepare `OnDestroy` after `PendingHandle.Complete()`. ✔
- *Filter hazard* → each batch id has its **own** `EntityQuery` whose shared
  filter is set once at creation and never mutated, so concurrent deferred jobs
  never share a mutating filter. ✔
- *Read-only* → the job reads `CombatKinematicsComponent`/`CombatRenderComponent`
  (RO) and writes only the native lists. Assigning the combined handle to
  `Dependency` chains those RO reads so next-frame simulation writers to
  kinematics still wait correctly. ✔
- *Capacity vs enabled count* → `CalculateEntityCount()` on a query containing the
  enableable tag returns the enabled count == number of `AddNoResize` calls;
  safe because enable/kinematics state is final before presentation and prepare
  runs `OrderFirst`. ✔

## Minimal/additive vs refactor comparison

- **Minimal/additive** (keep the component; make render avoid the copy):
  - resulting data flow: matrix → component (chunk storage) → still needs a
    compaction because of enableable gaps → per-chunk direct submit would render
    dead entities; the only "keep component + no copy" variant is writing the
    matrix into *both* the component and a side buffer.
  - new concepts/types: a side buffer duplicating the component.
  - copies/translations added: none removed cleanly; the component write becomes
    dead weight (written, never read).
  - long-term cost: two representations of one matrix, unclear source of truth,
    vestigial component on every archetype.
- **Refactor** (this plan — remove the component, prepare into buffers):
  - resulting data flow: kinematics/render (RO) → prepare job →
    per-batch `NativeList<Matrix4x4>` → `RenderMeshInstanced`. One producer, one
    consumer, one representation.
  - existing types changed/removed: `CombatRenderElement` component + `ElementFor`
    removed; matrices become `Matrix4x4` in native buffers; registry entry gains
    `Kind`.
  - copies/translations removed: the per-frame `ToComponentDataArray` memcpy and
    the spawn-time matrix seed writes.
  - long-term benefit: single source of truth for the render matrix, smaller
    archetypes, better cache/job behavior, fewer queries.
- **Decision: choose refactor.** The additive path creates a duplicate
  representation of the same concept with no clear owner (a structural warning);
  the refactor collapses to one data path and directly satisfies the user's
  intent ("avoid the copy caused by the matrix being a component").

## Default decision rule applied

The matrix has two candidate representations (per-entity component vs per-batch
buffer) describing the same concept. Per the rule, refactor toward one source of
truth: the per-batch buffer, which is what the GPU actually consumes.

## Task list

1. [001-element-type-and-registry-kind.md](001-element-type-and-registry-kind.md)
   — `CombatRenderComponents.cs`: remove the `CombatRenderElement` component and
   `ElementFor`; add `CombatRenderMatrixUtility.MatrixFor(kin, render) -> Matrix4x4`;
   add `CombatRenderKind` enum + `CombatRenderResourceEntry.Kind`; add a `kind`
   parameter to `Register`.
2. [002-combatroot-register-kind.md](002-combatroot-register-kind.md)
   — `CombatRoot.cs`: pass the kind at each `Register` call site.
3. [003-prepare-into-batch-buffers.md](003-prepare-into-batch-buffers.md)
   — rewrite `CombatRenderPrepareSystem.cs` to fill per-batch
   `NativeList<Matrix4x4>` via dedicated per-batch queries + producer handle.
4. [004-render-consume-buffers.md](004-render-consume-buffers.md)
   — rewrite `CombatBatchedRenderSystem.cs` to zero-copy submit from the buffers
   and route counters by `entry.Kind`.
5. [005-projectile-spawn-remove-element.md](005-projectile-spawn-remove-element.md)
   — `ProjectileSpawnApplySystem.cs`: drop the component from archetypes, spawn
   writes, and both reuse jobs.
6. [006-aoe-spawn-remove-element.md](006-aoe-spawn-remove-element.md)
   — `AoeSpawnApplySystem.cs`: drop the component from three archetypes,
   `RecordAoeReset`, and `AoeSpawnJob`.
7. [007-test-migration.md](007-test-migration.md)
   — migrate PlayMode tests off the removed component.
8. [008-docs.md](008-docs.md)
   — update render/AOE docs.

Suggested order: 001 → 002 → (003, 005, 006 in parallel) → 004 → 007 → 008.
001 is foundational (types); 002/003/005/006/007 depend on it; 004 depends on 003.

## Open questions / risks

- **Capacity vs enabled-count TOCTOU** — relies on "enable + kinematics state is
  final before presentation." Keep prepare `OrderFirst` as a load-bearing
  invariant; nothing in presentation should flip enable bits before the job runs.
- **AOE first-frame matrix** now comes solely from prepare (the spawn-time seed is
  removed). Prepare runs `OrderFirst` after same-frame spawn/apply and its query
  matches newly spawned chunks (`CombatRenderActiveTag` enabled at spawn), so
  entities are matrixed the frame they appear. Verify via `AoePlayModeTests`.
- **No `SharedComponentTypeHandle`/`GetSharedComponent` is used anywhere today** —
  the routing-job alternative was rejected (needs nested native containers or an
  extra compaction pass); dedicated per-batch queries are simpler and safe.
