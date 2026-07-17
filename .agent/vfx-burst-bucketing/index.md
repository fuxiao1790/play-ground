# Bucket AOE VFX spawn requests in a Burst job

## Summary

`CombatAoeVfxDispatchSystem` owns a single shared `NativeQueue<AoeVfxSpawnRequest>` that every
AOE/projectile/pulse producer job enqueues into, tagged only with a `VfxId` int. Bucketing —
routing each queued event to the `AoeVfxTypeResources` (and its `GraphicsBuffer`s) matching its
`VfxId` — currently happens in managed, non-Burst C# on the main thread
(`CombatVfxRoot.DrainAndDispatch` → `StageAoeSpawn` → `AoeVfxTypeResources.TryStage`, one
`NativeList.Add` per item). This moves that bucketing into a Burst-compiled counting-sort `IJob`,
and removes the now-redundant managed "staging" step entirely — the sorted buffer becomes the
staged batch, consumed directly by `GraphicsBuffer.SetData`.

## Rationale

Two representations of "the current frame's staged batch" (a Burst-sorted scratch buffer plus the
existing per-resource `Staging` NativeList) would have to stay in sync for no benefit — no external
caller depends on `Staging`/`TryStage`/`ClearStaging` (verified via full-tree grep: only
`CombatVfxRoot.cs`, `CombatAoeVfxDispatcher.cs`, and one EditMode test reference them). Refactoring
`DrainAndDispatch`/`Dispatch` to consume the sorted slices directly removes a copy, removes the
duplicate concept, and gives a single source of truth.

## Constraints & invariants

1. **Producer/consumer threading contract** — the queue is written by parallel producer jobs via
   `.AsParallelWriter()`, folded into `CombatAoeVfxDispatchSingleton.ProducerHandle`; consumer must
   `.Complete()` that handle before touching the queue.
   Source: `Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs:65-66`,
   `Assets/Scripts/System/Aoes/AoePulseVfxSystem.cs:29-46`.
2. **Ownership boundary** — `CombatVfxRoot` is sole owner/disposer of `AoeVfxTypeResources`,
   `GraphicsBuffer`s, and `VisualEffect` GameObjects; `CombatAoeVfxDispatcher` stays stateless,
   GPU-protocol-only. Source: file-header comments in
   `Assets/Scripts/System/Vfx/CombatVfxRoot.cs:22-24` and
   `Assets/Scripts/System/Vfx/CombatAoeVfxDispatcher.cs:22-24`.
3. **Layer rule** — jobs/simulation systems must not read GameObjects, Transforms, Colliders,
   Physics2D, ScriptableObjects. Source: `Docs/architecture/layer-rules.md:58-59`.
4. **Lifecycle/allocation** — `PendingAoeSpawns` is `Allocator.Persistent`, created in `OnCreate`,
   disposed in `OnDestroy`. Source: `CombatAoeVfxDispatchSystem.cs:39,55-58`.
5. **Performance budget: reuse allocations** — avoid new heap addresses every frame for
   cross-frame-visible buffers; the project already uses a grow-only pattern
   (`AoeVfxTypeResources.EnsureBufferCapacity`, never shrinks a `GraphicsBuffer`).
   Source: `Docs/reference/simulation/ecs-notes.md` (Temporary Memory section),
   `Docs/project-overview.md` ("Performance is a primary constraint").
6. **`owners.Count` is not stable** — `CombatVfxRoot.Register` can be called mid-gameplay when a
   mob is rented (`SpawnController.WireMob` → `MobRoot.BindVfxRoot` → `SkillDriver` →
   `CombatVfxRoot.Register`), so bucket count must be read fresh every `OnUpdate`.

## Mechanisms reused vs. introduced

- **Reused**: grow-only capacity idiom (already used by `EnsureBufferCapacity`), applied via
  `NativeList<T>`'s built-in amortized `Capacity`/`Length` instead of hand-rolled doubling;
  existing `Allocator.Persistent` + `OnCreate`/`OnDestroy` lifecycle; existing
  `ProducerHandle.Complete()`-then-touch-queue contract.
- **Introduced**: one new Burst `IJob` (`BucketAoeVfxSpawnsJob`) doing a two-pass counting sort.
  No existing counting-sort/bucket-into-contiguous-array pattern exists in the codebase; closest
  precedent (`TargetSpatialHashSystem`, `NativeParallelMultiHashMap`) doesn't give the contiguous
  per-bucket slices needed for direct `GraphicsBuffer.SetData` upload.

## Minimal/additive vs. refactor comparison

**Additive**: keep `DrainAndDispatch`/`Dispatch`/`Staging`/`TryStage` as-is; Burst sort writes to a
new scratch buffer, then an unchanged managed loop copies scratch → `Staging` → GPU.
- Data flow: `NativeQueue` → Burst sort → scratch → managed copy → `Staging` → GPU (two hops).
- New concepts: a second "staged batch" representation alongside `Staging`.
- Copies added: one extra full copy of every event.
- Long-term cost: two representations to keep in sync; `TryStage`/`ClearStaging` become vestigial.

**Refactor (chosen)**: `DrainAndDispatch`/`Dispatch` consume sorted slices directly;
`Staging`/`AreaSizeStaging`/`TryStage`/`ClearStaging` deleted.
- Data flow: `NativeQueue` → Burst sort (into persistent, reused `NativeList`s) → GPU (one hop).
- Types changed: `AoeVfxTypeResources` loses staging members; `DrainAndDispatch`/`Dispatch`
  signatures change to take slices.
- Copies removed: the per-item managed `NativeList.Add` step disappears entirely.
- Long-term benefit: single source of truth for the staged batch.

**Decision: refactor.** No compatibility reason to keep the old shape (confirmed via grep — no
external callers). Additive path reintroduces exactly the structural smells this process flags.

## Design validation

1. Threading — job only touches the queue after `ProducerHandle.Complete()`, runs synchronously
   single-threaded via `.Run()`. ✅
2. Ownership — `CombatVfxRoot` still sole owner; `Dispatch` still stateless. ✅
3. Layer rule — job touches only `NativeQueue<AoeVfxSpawnRequest>` / `NativeList<float2/float/int>`. ✅
4. Lifecycle — new buffers created/disposed alongside `PendingAoeSpawns`. ✅
5. Reuse allocations — sort output buffers are grow-only `NativeList`s reused every frame; only
   job-local scratch (`items`/`counts`/`cursor`) uses `Allocator.Temp`. ✅
6. `owners.Count` volatility — read fresh every `OnUpdate` via `root.RegisteredVfxCount`. ✅

## Tasks

1. [001-singleton-and-bucket-job.md](001-singleton-and-bucket-job.md) — add persistent sort
   buffers to the singleton; add `BucketAoeVfxSpawnsJob`; rewrite `OnUpdate`.
2. [002-drain-and-dispatch-refactor.md](002-drain-and-dispatch-refactor.md) — refactor
   `DrainAndDispatch`/`Dispatch`; delete staging members from `AoeVfxTypeResources`.
3. [003-update-registration-tests.md](003-update-registration-tests.md) — update the EditMode test
   that depends on the removed `TryStage` API.

Dependencies: 002 depends on 001 (needs the agreed `DrainAndDispatch` call shape); 003 depends on
002 (needs the final `AoeVfxTypeResources` shape).

## Open questions

None outstanding.

## Verification

- Run `Assets/Tests/EditMode/CombatVfxRootRegistrationTests.cs`.
- Run the broader `Assets/Tests` suite (PlayMode included).
- Launch the game, trigger several AOE skills with different spawn/hit/expire/pulse/arming VFX
  assets simultaneously, and visually confirm correct positions/area sizes with no
  cross-contamination between VFX types (GPU correctness needs an in-editor eyeball check).
