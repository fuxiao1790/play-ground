# VFX New Contract Path (Area-Timed)

## Summary

Today the VFX dispatch supports exactly one request contract: `Positions (float2)` +
`AreaSizes (float)` + `SpawnCount (int)`, hard-coded end to end (payload struct, one queue,
dispatcher that always uploads both buffers, graph that must expose both). See
`Docs/reference/simulation/vfx-system.md`.

This plan makes the system support **more than one contract** by standing up a **new, parallel
path** for a second contract, **without migrating the existing Area path**. The new contract:

```
Area-Timed = Positions(float2) + AreaSizes(float) + Durations(float, ms) + TickIntervals(float, ms) + SpawnCount(int)
```

Its purpose: a graph that **self-pulses on the GPU** (spawn once, repeat on GPU for `Duration`
every `TickInterval`), for stationary lingering AOEs. It is offered as an authorable alternative;
the existing per-tick `AoePulseVfxSystem` and the Area path stay exactly as they are.

The mechanism generalizes: adding a future contract = new payload struct + new queue + new
dispatcher resource kind + one `VfxDataType` enum value + a producer branch. No existing path is
disturbed and no unsafe code is introduced.

## Constraints & invariants (source)

- **No unsafe in gameplay/shared runtime** — `Docs/coding-standards.md:95`. Kills the previously
  discarded generic byte-schema staging (memcpy fields by `{offset,stride}` into
  `NativeList<byte>`). All staging stays strongly typed: typed `NativeList<T>` +
  `GraphicsBuffer.SetData<T>` where `T : unmanaged`.
- **No per-vfx-kind entity component / archetype** (user directive). One entity can emit many vfx
  kinds across triggers; a component-per-kind would fork archetypes into many tiny chunks. The
  `(TypeId, Trigger) -> VfxDataType` mapping MUST live in a **flat side table** in the dispatch
  singleton, not baked on entities. Entity carries only `TypeId` (already on
  `AoeIdentityComponent`, `AoeSpawnExpansionSystem.cs`, `CombatArmingSystem.cs:111`).
- **VFX is a visual-only lane, kept distinct from damage/spawn payloads** —
  `Docs/coding-standards.md:150-167`. New payload stays visual-only; no gameplay fields.
- **Lane singleton pattern (no cross-system field reach-in)** — `Docs/coding-standards.md:169-249`.
  New queue lives in the existing `CombatAoeVfxDispatchSingleton`; producers combine their job
  handle into the singleton's `ProducerHandle`; the dispatch system completes it before draining.
  Singleton-nested containers do NOT auto-chain, so the manual `ProducerHandle` threading is
  required (mirrors current code, `AoePulseVfxSystem.cs:40-44`).
- **VFX dispatch is a named hot path** — `Docs/coding-standards.md:282`. New queue + per-field
  staging lists must be persistent and reused; no per-frame alloc, LINQ, or closures in the drain.
- **Native/GPU handle ownership** — `Docs/coding-standards.md:292-313`. New `GraphicsBuffer`s and
  the new queue disposed on every teardown path; the `Register` try/catch partial-alloc cleanup
  (`CombatAoeVfxDispatcher.cs:126-137`) extended to cover the new buffer set.
- **ECS lifecycle comments** — `Docs/coding-standards.md:251-265`. New payload struct + singleton
  field carry `// ECS Lifecycle:` comments.
- **Sim time is seconds; graph wants ms** (user directive). Producer converts seconds -> ms when
  building the Area-Timed payload (`Lifetime * 1000`, `RepeatHitCooldownSeconds * 1000`).
- **VFX Graph "Sample Graphics Buffer" cannot sample arbitrary struct type/length** (user note).
  Therefore keep **one typed `GraphicsBuffer` per field**, not one struct buffer. Confirmed design
  choice, not a shortcut to revisit for correctness.

## Mechanisms reused vs. introduced

Reused (conform, do not fork):
- `CombatAoeVfxDispatchSingleton` lane singleton + its `ProducerHandle` threading.
- `CombatAoeVfxDispatcher` per-`(typeId, trigger)` resource dictionary + `maxPerFrame` cap +
  `Dispatch()` SetData/SendEvent flow + `Register` validate/try-catch shape.
- Flat `(TypeId, Trigger)` keying (dispatcher already uses `KeyFor`).
- Existing emit sites keep their Area emit unchanged; new emit is an added branch.

Introduced (justified):
- `VfxDataType` enum (`Area = 0` default, `AreaTimed = 1`) — required so a producer in a Burst job
  can pick a contract from authored data via a flat lookup. `Area = 0` means default-zero == today's
  behavior, so unauthored effects are untouched.
- `NativeList<VfxDataType> DataTypeByKey` on the singleton — the flat side table (the only
  allowed home for the mapping per the no-archetype rule).
- `VfxAreaTimedRequest` payload struct + `NativeQueue<VfxAreaTimedRequest>` on the singleton.
- `AoeVfxTimedTypeResources` (a second resource record with 4 buffers) inside the existing
  dispatcher — data-driven per registration, **not** a managed handler class hierarchy.

## Minimal/additive vs. refactor comparison

- **Minimal/additive (CHOSEN, per user)**:
  - resulting data flow: existing Area path unchanged; a second queue + second dispatcher resource
    kind carry Area-Timed; producer branches on `DataTypeByKey` to pick which queue.
  - new concepts/types: `VfxDataType`, `DataTypeByKey`, `VfxAreaTimedRequest`, `AoeVfxTimedTypeResources`.
  - copies/translations added: one extra typed drain + one seconds->ms convert at the producer.
  - long-term cost: two naming lineages coexist (`AoeVfx*` Area vs `VfxAreaTimed*`); dispatcher holds
    two resource dictionaries. Acceptable, isolated, documented as debt.
- **Refactor (rejected now)**:
  - resulting data flow: one generic schema-driven consumer over N contracts, single mental model.
  - existing types changed/removed: rewrite payload/queue/dispatcher/registration/all emit sites.
  - copies/translations removed: the two-lineage split.
  - long-term benefit: one source of truth for "a contract".
- **Decision**: choose **additive**. Reason: user explicitly directed "create a new path for the
  new contract now, don't migrate existing"; the prior full-generic refactor was already built and
  discarded for requiring unsafe. Additive keeps working Area effects untouched and ships the new
  contract with minimal risk. Refactor toward one source of truth is deferred, not abandoned.

## Default decision rule note

`VfxDataType` is the single source of truth for "which contract does this `(TypeId, Trigger)`
use". Producers and registration both read/write it; no second representation of the contract
choice is introduced.

## Design validation vs. invariants

- No unsafe: staging uses `NativeList<float2>/<float>` + `SetData<T>` only. PASS.
- No archetype growth: contract choice read from flat `DataTypeByKey`, entity keeps only `TypeId`.
  One entity emits Area on one trigger and Area-Timed on another with zero added components. PASS.
- Lane singleton / handle: new queue in the same singleton; producers thread `ProducerHandle`;
  dispatch system completes before draining both queues. PASS.
- Hot path alloc: queue + staging persistent, drained with `while (TryDequeue)`. PASS.
- Ownership: new buffers/queue disposed in `Dispose`/`OnDestroy`; `Register` rollback covers 4
  buffers. PASS.
- Visual-only separation: payload carries only Position/AreaSize/Duration/TickInterval. PASS.

## Tasks

- `001-vfxdatatype-flat-lookup.md` — `VfxDataType` enum + `DataTypeByKey` flat table on the
  singleton + a main-thread setter the managed registration calls; default `Area`.
- `002-areatimed-payload-and-queue.md` — `VfxAreaTimedRequest` struct + second `NativeQueue` on the
  singleton; `OnCreate`/`OnDestroy`/`OnUpdate` create, dispose, complete, and count-gate both queues.
- `003-dispatcher-areatimed-resources.md` — `AoeVfxTimedTypeResources` (Positions/AreaSizes/
  Durations/TickIntervals) + `StageAreaTimed` + extend `Dispatch()`/`Register`/validation +
  `CombatVfxRoot.DrainAndDispatch` drains the new queue.
- `004-producer-areatimed-emit.md` — at lingering-AOE spawn emit sites, branch on
  `DataTypeByKey[(TypeId, Spawn)]`: `AreaTimed` -> enqueue `VfxAreaTimedRequest` (Duration/TickInterval
  in ms) into the new queue; else existing Area emit unchanged.
- `005-authoring-datatype.md` — author `VfxDataType` per effect (binding) + `SkillDriver`
  registration passes the type so `Register` builds the right resource kind and sets `DataTypeByKey`.
- `006-docs-lifecycle-tests.md` — update `vfx-system.md`, coding-standards line-162 mention,
  ECS lifecycle comments; add a play-mode/preview check for an Area-Timed graph.

## Open questions / considerations

- **Arming emit**: the non-arming expansion spawn site (`AoeSpawnExpansionSystem.cs:111`) has
  `Lifetime` + `RepeatHitCooldownSeconds` on the command directly. The arm-complete spawn site
  (`CombatArmingSystem.cs:109`) does not; it would read them via optional `ComponentLookup`
  (`CombatLifetimeComponent`, `AoeHitGateComponent`). Task 004 handles expansion first; arming
  Area-Timed emit is a clearly-marked sub-step that can ship after.
- **Naming debt**: existing lineage keeps `AoeVfx*`; new lineage is `VfxAreaTimed*`. A future
  refactor toward one source of truth (and dropping the `Aoe` prefix, since VFX is source-agnostic)
  is out of scope here and noted for later.
- Supersedes the discarded `.agent/vfx-schema-payloads/` design (generic byte-schema, needed unsafe).
