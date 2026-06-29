# ECS Stats Display

## Summary

Add a live debug stats readout that surfaces ECS combat counters on a scene
GameObject. Three pieces:

1. `CombatStatsSingleton` — an unmanaged `IComponentData` on one singleton entity
   holding all stat fields (the canonical ECS-side snapshot).
2. `CombatStatsGatherSystem : SystemBase` in `PresentationSystemGroup` — gathers
   per-frame counts from the systems that already compute them, writes the
   singleton, and pushes the snapshot to the bound GameObject.
3. `CombatStatsDisplay : MonoBehaviour` — mounted on a scene GameObject, bound to
   the singleton entity through a managed `CombatStatsBinding` component, renders
   the snapshot.

First-implementation stats:

1. Entities spawned via ECB (cold create) vs via reuse (disabled-slot claim).
2. Hit events created this frame.
3. VFX events created this frame.

A 1-tick delay is acceptable, so the gather system reads each producer's
last-recorded value without forcing strict update ordering.

## Where the data already lives

| Stat | Source system | Value today |
|---|---|---|
| Spawn cold-create (ECB) | `BasicProjectileSpawnApplySystem`, `ChildSpawnerProjectileSpawnApplySystem`, `AoeSpawnApplySystem` | local `totalRequests - reuseCount`, written only to write-only `ProfilerCounterValue` |
| Spawn reuse | same three systems | local `reuseCount`, same |
| Hit events created | `CombatApplyFinalizeSystem` | local `hitCount = HitQueue.Count` |
| VFX events created | `CombatVfxDispatchSystem` | `PendingSpawns.Count` before drain |

These values are computed every frame but are not readable after the fact
(`ProfilerCounterValue<int>` is write-only into the profiler). The change exposes
each as a readable `internal` last-frame field assigned from the same local — one
computation, two sinks (profiler + display).

## Architecture decisions

### Gather model: pull (not push)
The gather system reads each producer's last-frame `internal int` field via
`World.GetExistingSystemManaged<T>()` and is the **single writer** of the
singleton. Producers are not touched beyond exposing already-computed values.

Rejected alternative — producers write the singleton directly (push): that makes
N writers to one component, needs a per-frame reset coordinator, and adds
cross-system singleton access inside hot spawn paths. The pull model keeps one
source of truth and leaves hot paths untouched except one extra int assignment.

### Binding: managed component on the singleton entity
The singleton entity carries both `CombatStatsSingleton` (unmanaged data) and
`CombatStatsBinding` (managed `IComponentData` holding the `CombatStatsDisplay`).
This literally binds the entity to the GameObject and mirrors the existing
`CombatRenderResourceRegistry` managed-singleton pattern
(`AddComponentObject` + `ManagedAPI.GetSingleton`). The `CombatStatsDisplay`
MonoBehaviour registers itself into the binding in `OnEnable` via explicit
`Bind`/`Unbind` calls on the gather system (no scene scans, per coding-standards
"Unity Object Access").

### Folder
New `Assets/Scripts/System/Stats/`, mirroring `System/Vfx/` (which also houses a
scene MonoBehaviour `CombatVfxRoot` next to its ECS system).

## Constraints & invariants the change must respect

- **Threading** (ecs-notes, code): the last-frame fields are plain `int`s set on
  the main thread at the end of each producer's `OnUpdate`. No job reads or writes
  them. The gather system reads them on the main thread in a later group. No new
  job is scheduled. Source: producer `OnUpdate` bodies.
- **Every-frame honesty**: each producer must set its last-frame field on *every*
  path, including the `totalRequests == 0` / `hitCount == 0` / empty-queue early
  returns, or the display shows stale counts. Source: early-return branches in the
  four producers.
- **Structural changes** (ecs-notes §"What Is a Structural Change"): the only
  structural change is the one-time singleton creation in
  `CombatStatsGatherSystem.OnCreate`. Steady-state `OnUpdate` does
  `SetComponentData` only — no archetype churn. Mirrors
  `CombatBatchedRenderSystem.OnCreate`.
- **Allocation Rule** (coding-standards): the gather `OnUpdate` allocates nothing
  — it reads four ints, writes one component, and makes one managed method call.
  No native containers, no LINQ, no closures.
- **1-tick delay** (requirement): spawn/hit producers run in
  `SimulationSystemGroup` (earlier in the same frame) so those are same-frame.
  `CombatVfxDispatchSystem` is in `PresentationSystemGroup`; to keep VFX
  same-frame too, the gather system runs `[UpdateAfter(CombatVfxDispatchSystem)]`.
  The 1-tick allowance remains the correctness margin if ordering changes.
- **Debug UI Ownership** (coding-standards): this is a per-object debug widget on
  its own GameObject, not a write to the shared `DebugOverlay` label — allowed.
- **Test Hooks** (coding-standards): the counters are genuine diagnostics the
  Performance Budget Rule explicitly asks for ("Debug counters should exist before
  content stress tests become hard to explain"), not test-only breadcrumbs.
- **Native/handle ownership** (coding-standards): the gather system owns no native
  handles; nothing to dispose. The singleton entity is destroyed with the world.

## Mechanisms reused vs. introduced

- **Reused**: managed-singleton pattern (`CombatRenderResourceRegistry` via
  `AddComponentObject`/`ManagedAPI.GetSingleton`); presentation-group
  system→scene-object push (`CombatVfxDispatchSystem`, `CombatApplyBridge`);
  cross-system `internal` field exposure (`expansionSys.PendingCommands`,
  `vfx.ProducerHandle`).
- **Introduced**: `CombatStatsSingleton`, `CombatStatsBinding`,
  `CombatStatsGatherSystem`, `CombatStatsDisplay`. Justified: there is no existing
  readable aggregate of these counters and no existing stats display entity.
- **Considered and rejected — `ProfilerRecorder`**: read the existing named
  `ProfilerCounterValue`s back via `ProfilerRecorder` instead of adding fields.
  Rejected because (a) hit-event and VFX counts are not counters today, so new
  counters would be needed anyway; (b) `ProfilerRecorder` is stringly-typed by
  counter name and only yields values while the profiler category is active — more
  fragile than reading a field.

## Design validation

- *Single source of truth*: the gather system is the only writer of
  `CombatStatsSingleton`; producers expose read-only last-frame values. ✓
- *No second data path*: counts are computed once per producer and assigned to the
  profiler counter and the readable field from the same local — no parallel
  computation or parallel routing through the singleton. ✓
- *Hot paths untouched*: spawn/finalize/vfx hot loops gain only a trailing int
  assignment; no new allocation or job. ✓
- *Lifecycle*: structural change confined to `OnCreate`; binding resolved through a
  managed component, registered/cleared by MonoBehaviour `OnEnable`/`OnDisable`. ✓

## Minimal/additive vs. refactor comparison

- **Minimal/additive (chosen)**
  - data flow: producers compute counts (as today) → expose readable field →
    gather system pulls 4 ints → writes singleton → pushes to display.
  - new concepts/types: `CombatStatsSingleton`, `CombatStatsBinding`, gather
    system, display MonoBehaviour.
  - copies/translations: producer→gather (int reads), gather→component,
    gather→display (one struct). All trivial.
  - long-term cost: each new displayed stat needs a producer field + one
    aggregation line.
- **Refactor (push into singleton)**
  - data flow: each producer writes the singleton; a frame-boundary system resets;
    display pulls/receives.
  - concepts changed: producers gain a dependency on the stats singleton; needs a
    reset owner.
  - copies removed: the 4 int field reads.
  - long-term benefit: marginal; introduces multi-writer + reset coordination into
    hot spawn systems.
- **Decision**: choose additive (pull). Reason: the singleton keeps a single
  writer and single source of truth, hot spawn paths stay clean, and producers
  already compute every value. The "second data path" structural warning does not
  apply because nothing routes a parallel copy through the singleton.

## Default decision rule

`CombatStatsSingleton` is the one ECS-side representation of the displayed stats;
the gather system is its sole writer. The MonoBehaviour is a pure view that
receives a pushed copy. No competing representation is introduced.

## Task list

- [001-stats-ecs-components.md](001-stats-ecs-components.md) — `CombatStatsSingleton`
  + `CombatStatsBinding` in new `System/Stats/` folder.
- [002-expose-producer-counts.md](002-expose-producer-counts.md) — readable
  last-frame fields on the four producing systems, set on every path.
- [003-stats-gather-system.md](003-stats-gather-system.md) —
  `CombatStatsGatherSystem` (singleton owner, Bind/Unbind, gather+write+push).
- [004-stats-display-monobehaviour.md](004-stats-display-monobehaviour.md) —
  `CombatStatsDisplay` MonoBehaviour + scene wiring.
- [005-verification.md](005-verification.md) — manual + optional PlayMode check.

## Open questions / considerations

- **Stat granularity**: spawns are aggregated across the three apply systems into
  two totals (ECB, reuse). Per-domain breakdown is a later extension (add fields,
  not architecture).
- **Render style**: `CombatStatsDisplay` renders via `OnGUI` for zero scene wiring.
  Swappable for a serialized `TMP_Text` later without touching the system or
  components.
- **Counts are per-frame instantaneous**, not cumulative. If a running total or
  rolling average is wanted, that is an additive change in the gather system only.
