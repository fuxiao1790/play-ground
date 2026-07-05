# Combat Event Singletons — Decouple Cross-System Queue Reach-Ins

## Summary

Combat producer systems currently reach into other systems' managed instances via
`World.GetExistingSystemManaged<T>()` and touch their internal `NativeQueue`/`NativeList`
fields plus a hand-threaded `ProducerHandle`/`PendingHandle`. This creates an invisible
mesh: every new event producer must be wired into every sink, and the dependency
protocol is duplicated at every call site.

This plan moves each sink's shared containers **and their job handles** into a per-sink
**singleton `IComponentData`**, accessed uniformly through `SystemAPI.GetSingleton` /
`TryGetSingletonRW`. It mirrors the existing [`TargetSpatialHashSingleton`](../../Assets/Scripts/System/Common/TargetSpatialHashSystem.cs)
(native containers + `BuildHandle`/`ConsumerHandle` stored in the struct) and the
[`CombatStatsSingleton`](../../Assets/Scripts/System/Stats/CombatStatsGatherSystem.cs)
cross-system accumulation pattern.

Scope (per decision): **all five Flavor-A queue lanes + the Flavor-B apply handoff.**

## The two reach-in flavors

**Flavor A — parallel-writer fan-in.** Producers grab `sink.Queue.AsParallelWriter()`,
schedule a job, then push the job handle into `sink.ProducerHandle` via
`CombineDependencies`. The sink drains on the main thread after `ProducerHandle.Complete()`.
Five sinks:

| Sink system | Container | Handle field(s) |
|---|---|---|
| `ProjectileSpawnExpansionSystem` | `NativeQueue<ProjectileSpawnEvent> EventQueue` | `ProducerHandle`, `PendingHandle` |
| `ImpactAoeSpawnExpansionSystem` | `NativeQueue<ImpactAoeSpawnEvent> EventQueue` | `ProducerHandle`, `PendingHandle` |
| `LingeringAoeSpawnExpansionSystem` | `NativeQueue<LingeringAoeSpawnEvent> EventQueue` | `ProducerHandle`, `PendingHandle` |
| `CombatApplyFinalizeSingleSystem` | `NativeQueue<CombatHitEvent> HitQueue` | `ProducerHandle` |
| `CombatVfxDispatchSystem` | `NativeQueue<VfxPendingSpawn> PendingSpawns` | `ProducerHandle` |

**Flavor B — command NativeList handoff.** The three apply systems read the expansion
system's produced `NativeList<*SpawnCommand>` plus `PendingHandle` on the main thread:
- `ProjectileSpawnApplySystem` ← `ProjectileSpawnExpansionSystem.ProjectileCommands` + `PendingHandle`
- `ImpactAoeSpawnApplySystem` ← `ImpactAoeSpawnExpansionSystem.ImpactCommands` + `PendingHandle`
- `LingeringAoeSpawnApplySystem` ← `LingeringAoeSpawnExpansionSystem.LingeringCommands` + `PendingHandle`

The three spawn-event lanes carry **both** flavors on one singleton: the inbound
`EventQueue` (A) and the outbound `Commands` + `PendingHandle` (B). Hit and Vfx lanes
are Flavor A only.

### Producer inventory (who writes each lane)

- **VfxPending:** `ProjectileCollisionSystem`, `ImpactAoeCollisionSystem`,
  `LingeringAoeCollisionSystem`, `ImpactAoeSpawnExpansionSystem`,
  `LingeringAoeSpawnExpansionSystem`, `CombatLifetimeSystem`, `AoePulseVfxSystem`.
- **HitQueue:** the three collision systems.
- **ProjectileSpawnEvent / ImpactAoeSpawnEvent / LingeringAoeSpawnEvent (each):** the
  three collision systems, `StatusProcessSystem`, `TimedSpawnSystem`.

## Constraints & invariants (must respect)

1. **Manual dependency threading is load-bearing and stays manual.** A `NativeQueue`
   reached through a singleton is *not* auto-tracked by ECS's dependency system — ECS
   tracks component-*type* access, not the containers nested inside a component
   (ecs-notes.md "Sync Points"; confirmed by `TargetSpatialHashSingleton` storing its
   own `BuildHandle`/`ConsumerHandle`). Therefore the `ProducerHandle`/`PendingHandle`
   move **into the singleton struct** and are threaded exactly as today. This refactor
   changes *storage + access mechanism only*, never the dependency logic. Source:
   [ImpactAoeCollisionSystem.cs:80-98](../../Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs).
2. **Main-thread-only handle mutation.** Producers combine into `ProducerHandle`
   sequentially on the main thread; sinks `Complete()` then drain on the main thread.
   `GetSingletonRW` access must stay on the main thread — never inside a job. Preserved.
3. **Cross-frame handoff timing.** On-hit spawn events land in a lane's queue during the
   collision phase and are drained by the expansion system's next update (asserted by
   `AoeSimulationTests`: "the on-hit AOE event lands in `aoeExpansion.EventQueue` during
   tick 1's collision"). `[UpdateBefore]`/`[UpdateAfter]` ordering and drain timing must
   be byte-for-byte preserved.
4. **Determinism.** Spawn fan-out derives deterministic IDs from tick index / jitter
   seed; enqueue/drain order must not change. No reordering of producer scheduling.
5. **Lifetime ownership stays per-sink.** Each sink system still creates its queue and
   its singleton entity in `OnCreate`, and disposes in `OnDestroy` after
   `ProducerHandle.Complete()`/`PendingHandle.Complete()` (Native/ECS Handle Ownership
   rule, coding-standards.md). No ownership is transferred to a central system.
6. **Second input path is out of scope.** Expansion systems also drain a
   `DynamicBuffer<*SpawnEvent>` on the `CombatScope` entity (main-thread skill casts).
   That path is ECS-tracked and untouched — only the `NativeQueue` relocates.
7. **Absent-system tolerance.** Producers today no-op when a sink is `null` (test worlds
   omit systems). Preserve with `TryGetSingleton*` rather than asserting existence.
8. **Allocation-light hot paths.** No new per-frame allocations, copies, or translation
   layers. The singleton holds the *same* container instance the sink already owned.

## Mechanisms reused vs. introduced

- **Reused:** `TargetSpatialHashSingleton` (containers + handles in a singleton struct;
  `GetSingleton`/`GetSingletonRW` access; create in `OnCreate`, dispose in `OnDestroy`)
  and `CombatStatsSingleton` (many systems accumulate through one singleton). This plan
  propagates that established shape to five more lanes.
- **Introduced:** five singleton component types (one per sink), each a thin relocation
  of fields the sink already declared. No new data concept, no new data path, no adapter.

## Additive vs. refactor comparison

- **Minimal/additive** (add singletons *beside* the existing internal fields, bridge both):
  - resulting data flow: two references to the same queue (system field + singleton),
    kept in sync by a shim.
  - new concepts/types: 5 singletons **plus** retained duplicate fields.
  - copies/translations added: sync/bridge code; two sources of truth per queue.
  - long-term cost: structural warning — duplicate ownership of one container, unclear
    source of truth, drift risk.
- **Refactor** (move container + handles into the singleton, delete the internal fields):
  - resulting data flow: one owner, one storage location, uniform `SystemAPI` access.
  - existing types changed: each sink loses its `internal` container/handle fields and
    `AsParallelWriter()` shim; each producer swaps `GetExistingSystemManaged` for
    `TryGetSingleton*`.
  - copies/translations removed: eliminates `GetExistingSystemManaged` lookups and the
    per-call `!= null`/`.IsCreated` field probing.
  - long-term benefit: new producers/consumers wire to a type, not to a system's guts;
    encapsulation enforced by the ECS type system; matches the in-repo precedent.
- **Decision: refactor.** Two representations of the same domain concept (a lane's queue)
  collapse to one source of truth. The additive path creates exactly the duplicate-ownership
  warning the workflow's default rule says to refactor away.

## Design validation against invariants

- (1)/(2) Handles ride *inside* the singleton and are mutated only via main-thread
  `GetSingletonRW`; the threading algorithm is copied verbatim → dependency correctness
  preserved. ✔
- (3)/(4) No `[UpdateBefore/After]` edits, no scheduling reorder, drain code unchanged
  except for where the container is *read from* → timing/determinism preserved. ✔
- (5) `OnCreate` creates entity+component; `OnDestroy` completes handles then disposes —
  same teardown as today, just relocated. ✔
- (6) `DynamicBuffer<*SpawnEvent>` drain left intact. ✔
- (7) `TryGetSingleton*` reproduces the current null-tolerant no-op. ✔
- (8) Same container instance relocated; zero added allocation. ✔

## Risk & sequencing rationale

Failure modes are mostly silent (under-sync → race under Jobs-Debugger-off builds;
over-sync → perf regression; drain-timing shift → one-frame gameplay change). Mitigation:
migrate **one lane per subtask**, keep the full PlayMode suite green between each, run in
the Editor with **Jobs Debugger + Leak Detection on**, and diff a profiler capture on a
stress scene for accidental over-sync. Start with **VfxPending** (write-only fan-in, no
Flavor B) as the pattern-proving proof-of-concept.

Note: the three collision systems write to five lanes from one job, so they are re-touched
in tasks 001–005. Tasks 003–005 are mechanically identical and may be co-implemented in
one PR to touch the collision systems once; they are kept as separate files for review
clarity.

## Default decision rule

If two representations describe the same domain concept (system field vs. singleton for a
lane's queue), collapse to one source of truth — the singleton — unless a concrete
migration blocker appears. None identified.

## Task list

- [001-vfx-pending-singleton.md](001-vfx-pending-singleton.md) — **PoC.** Relocate
  `CombatVfxDispatchSystem.PendingSpawns` + `ProducerHandle` into
  `CombatVfxDispatchSingleton`; migrate all 7 VFX producers. Establishes the pattern.
- [002-hit-queue-singleton.md](002-hit-queue-singleton.md) — Relocate
  `CombatApplyFinalizeSingleSystem.HitQueue` + `ProducerHandle`; migrate 3 collision
  producers.
- [003-projectile-spawn-event-singleton.md](003-projectile-spawn-event-singleton.md) —
  Relocate `ProjectileSpawnExpansionSystem` `EventQueue` + `ProjectileCommands` +
  `ProducerHandle`/`PendingHandle` (A + B). Migrate 5 producers + `ProjectileSpawnApplySystem`.
- [004-impact-aoe-spawn-event-singleton.md](004-impact-aoe-spawn-event-singleton.md) —
  Same shape for the impact AOE lane.
- [005-lingering-aoe-spawn-event-singleton.md](005-lingering-aoe-spawn-event-singleton.md) —
  Same shape for the lingering AOE lane.
- [006-verify-and-cleanup.md](006-verify-and-cleanup.md) — Confirm zero remaining
  combat-lane `GetExistingSystemManaged` field reach-ins, determinism, full test pass,
  profiler diff; cross-link the coding-standard.

## Open questions / considerations

- **ECS auto-dependency as a follow-up:** accessing the queue via `GetSingletonRW` *could*
  eventually let ECS carry the component dependency and shrink the manual handle threading.
  Deliberately **excluded** here — it changes dependency semantics and carries its own
  risk; land the behavior-preserving relocation first.
- **Shared helper:** the "combine my handle into the sink's `ProducerHandle`" idiom repeats.
  A small `RefRW<T>`-taking helper is optional polish, not required; keep call sites
  explicit if it obscures the dependency flow.
