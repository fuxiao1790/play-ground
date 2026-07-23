# Event-Based Spawn Submission

Replace the **direct-call** managed spawn path — where game logic reaches into ECS
and writes spawn *events* itself — with a **request → process → reply** loop:

- Game logic appends a plain **`CombatSpawnRequest`** to a scope-owned container.
- A new ECS **`SpawnIntakeSystem`** drains requests, builds the spawn events, and
  writes a **`CombatSpawnResult`** back.
- A managed **`CombatSpawnResultBridge`** replays results to the caster.

This is the **prerequisite for ECS-owned mana**. Mana will live on the caster the
same way health does (on the target-proxy entity), and the "can this cast afford
its cost?" decision must run where mana lives — inside `SpawnIntakeSystem`. Today
managed code unconditionally writes the spawn event, leaving no seam for that
decision. This task builds the seam; it does **not** add mana.

Per the user's scoping decisions:
- **All public spawn entry points** are converted (not only the runtime cast path).
- The caster is identified by its **target-proxy entity** ("same as health,
  entity/GameObject binding").
- The result type is an **empty/minimal struct for now** with the **consuming
  methods ready** (no accept/reject behavior yet — that arrives with mana).

## Current shape (what changes)

Runtime cast path today (only real gameplay producer):

```
PlayerRoot.Update (Update phase)
  -> SkillDriver.Tick
    -> SkillSpawnTranslator.Spawn
      -> CombatRoot.SpawnRegisteredProjectile / SpawnRegisteredAoe   // DIRECT CALL
         - EnsureRuntimeReady
         - allocate managed id (nextProjectileId / nextAoeId)
         - build ProjectileSpawnEvent / Impact|LingeringAoeSpawnEvent
         - entityManager.GetBuffer<...Event>(scopeEntity).Add(...)   // writes ECS buffer
```

Public one-off/test path (`CombatRoot.Spawn(ProjectileSpawnRequest|AoeSpawnRequest|
ProjectileAoeSpawnRequest, faction)`) is the same, plus it registers the template
at submit time before writing the event.

ECS already drains spawn events from **two** channels per lane, merged by the
expansion systems (`ProjectileSpawnExpansionSystem`, `AoeSpawnExpansionSystem`):
- a `NativeQueue<...Event>` filled by in-sim job producers (e.g. `TimedSpawnSystem`);
- the scope `DynamicBuffer<...Event>` filled by the managed `CombatRoot` writes above.

ECS already replies to managed today via `CombatApplyBridge`
(`PresentationSystemGroup`): it reads a `NativeList` result singleton and dispatches
to `ICombatTarget.ReceiveCombatTick`, resolving the managed caster from the proxy
entity's `TargetCompanion`.

## Target shape

```
Managed submit (Update phase, pre-tick):
  CombatRoot.Spawn* / SpawnRegistered*  (+ Entity caster arg)
    - EnsureRuntimeReady / validate
    - register template if needed (public overloads only; pre-tick registry write)
    - allocate managed id (unchanged)
    - append CombatSpawnRequest{ kind, templateKey, pos, aim, faction, sourceId,
        jitterSeed, contactGateSeedTargetId, caster } to scope request buffer

ECS Simulation (same frame, before expansion):
  SpawnIntakeSystem
    - drain + clear the scope CombatSpawnRequest buffer
    - [future: check caster mana; reject if insufficient]
    - build the Projectile/Impact/Lingering spawn event, enqueue into the
      existing NativeQueue lane for that kind
    - append CombatSpawnResult{ caster } to the result lane

ECS Presentation (same frame):
  CombatSpawnResultBridge
    - read result lane, resolve caster via TargetCompanion
    - call ICombatTarget.ReceiveSpawnResult(in result)  // default no-op stub today
```

The managed scope `DynamicBuffer<...Event>` producer channel is **removed**: after
this change all spawn events are produced inside ECS (intake + timed-spawn job), so
the expansion systems drain only their `NativeQueue` lane. One event channel, not two.

## Rationale for major decisions

- **Unify three managed event writes into one request type.** Managed code today
  writes three distinct event buffers (`ProjectileSpawnEvent`, `ImpactAoeSpawnEvent`,
  `LingeringAoeSpawnEvent`). A single `CombatSpawnRequest` (discriminated by
  `IntervalChildKind`) replaces all three managed write sites and carries the caster
  handle the events lack. A new type is justified here because it *removes* three
  parallel submission paths rather than adding a fourth.
- **Event construction moves into ECS.** "ECS will process these" means the
  event-building (and the future mana gate) lives in `SpawnIntakeSystem`, not in
  managed `CombatRoot`. Managed builds only the higher-level request.
- **Reuse the queue lane, drop the scope event buffer.** Intake feeds the existing
  `NativeQueue` lanes (the same channel `TimedSpawnSystem` uses), which lets the
  managed scope event buffers be deleted — collapsing the event intake to a single
  source of truth.
- **Reply reuses the health read-back.** `CombatSpawnResult` + result singleton +
  `CombatSpawnResultBridge` + `ICombatTarget.ReceiveSpawnResult` mirror
  `CombatTickResult` / `CombatApplyResultSingleton` / `CombatApplyBridge` /
  `ReceiveCombatTick` exactly, and route by the same proxy `TargetCompanion` binding
  mana will use.
- **Id/stat allocation stays managed this task.** Non-breaking (`CombatRoot.Counters`
  and the sequential id/jitter scheme are preserved), and nothing rejects yet. The
  request carries the pre-allocated `SourceId`/`JitterSeed`.

## Constraints & invariants the change must respect

1. **Template registry is written only pre-tick and is read-only during the tick.**
   Source: [spawn-events-and-commands.md](../../Docs/contracts/spawn-events-and-commands.md)
   ("registry is written only by external (pre-tick) spawns and is read-only and
   immutable during the simulation tick");
   `CombatRoot.RegisterSpawnTemplate` calls `CompleteAllTrackedJobs`.
   => Template registration stays a **managed submit-time** step (Update phase, before
   the ECS tick). `SpawnIntakeSystem` never writes the registry; expansion still
   dereferences it read-only. The event a request produces is a slim registry link,
   exactly as today.

2. **Managed game logic must not create combat entities directly and must not put
   managed references in event/command payloads.** Source:
   [combat-bridge.md](../../Docs/layers/combat-bridge.md) forbidden dependencies;
   [spawn-events-and-commands.md](../../Docs/contracts/spawn-events-and-commands.md)
   restrictions. => The request carries only plain data plus an `Entity` (the caster
   proxy). `Entity` is unmanaged and allowed; no `GameObject`/`Transform`/managed
   companion goes on the request. The bridge resolves the managed caster ECS-side via
   `TargetCompanion`, the only place managed companions may be resolved
   ([layer-rules.md](../../Docs/architecture/layer-rules.md)).

3. **Frame ordering: managed submit precedes the ECS tick; expansion runs after
   producers.** Source: `CombatEcsWorld.AppendWorldToCurrentPlayerLoop` (combat world
   runs in the normal player loop); `PlayerRoot.Update` calls `SkillDriver.Tick`;
   `ProjectileSpawnExpansionSystem` is `[UpdateAfter(TimedSpawnSystem)]`. => Managed
   fills the request buffer in the `Update` phase, before the appended
   `SimulationSystemGroup`. `SpawnIntakeSystem` must run in Simulation **before** the
   expansion systems and be sequenced safely against the other queue producer (run it
   `[UpdateBefore(TimedSpawnSystem)]` so its main-thread enqueue cannot race a
   `TimedSpawnSystem` producer job in flight). Reply is readable the same frame in
   `PresentationSystemGroup`.

4. **Two event-intake channels exist per lane and are merged by expansion.** Source:
   `ProjectileSpawnEventSingleton` (`EventQueue` + scope `DynamicBuffer`) and
   `ProjectileSpawnExpansionSystem.OnUpdate` (drains both); same for impact/lingering
   AOE. => Intake enqueues into the `NativeQueue` lane. The managed scope event
   `DynamicBuffer`s become dead producers and are removed from `CombatScopeOwner` and
   from the expansion drain code, leaving one channel per lane.

5. **ECS→managed reply shape.** Source: `CombatApplyResultSingleton` (`NativeList`
   produced by sim, `ProducerHandle`), `CombatApplyBridge`
   (`PresentationSystemGroup`, resolves `TargetCompanion` → `ICombatTarget`),
   `ICombatTarget.ReceiveCombatTick` default method. => Spawn reply copies this shape:
   `CombatSpawnResult` list singleton, `CombatSpawnResultBridge`, and a
   `ICombatTarget.ReceiveSpawnResult` default (no-op) method.

6. **Caster identity = target-proxy entity ("same as health").** Source: user
   decision; `ICombatTarget.CombatTargetProxy`; health is finalized on the proxy and
   mirrored back via `CombatTickResult.TargetProxy`. => `CombatSpawnRequest.Caster =
   owner.CombatTargetProxy`. `Entity.Null` is the "no owner" sentinel (tests /
   ownerless spawns).

7. **Id and lifetime stats are managed today.** Source: `CombatRoot.nextProjectileId`,
   `nextAoeId`, `spawnedAoes`, `Counters`. => Allocation stays managed at submit this
   task; the request carries the pre-allocated `SourceId`/`JitterSeed`. Determinism is
   unchanged because expansion's `ProjectileIdFor` reads the same fields it does today.

## Mechanisms reused vs. introduced

- **Reused:** scope `DynamicBuffer` submission + `CombatScopeOwner` create/dispose
  pattern; `NativeQueue<...Event>` lanes and the expansion drain; `CombatApplyBridge`
  result-lane → managed reply; `ICombatTarget` default-method reply +
  `TargetCompanion` routing; managed pre-tick template registration; the managed
  id/jitter/stat allocation.
- **Introduced:** `CombatSpawnRequest` (unified, kind-discriminated
  `IBufferElementData`) + its scope buffer; `SpawnIntakeSystem`; `CombatSpawnResult`
  (minimal) + result singleton lane; `CombatSpawnResultBridge`;
  `ICombatTarget.ReceiveSpawnResult` default stub; an `Entity caster` argument on the
  submission APIs.

## Design validation (against each invariant)

- **(1)** Registration remains in `CombatRoot.Spawn*` at submit (Update phase);
  intake only reads request data and writes events/results. Registry untouched by the
  tick.
- **(2)** `CombatSpawnRequest`/`CombatSpawnResult` are unmanaged structs; the only
  reference-like field is `Entity`. Companion resolution happens in the ECS bridge.
- **(3)** Intake ordered `[UpdateBefore(TimedSpawnSystem)]` and thus before expansion;
  main-thread enqueue happens while no producer job is in flight; last frame's
  producer handle was completed by expansion.
- **(4)** Intake writes the queue lane; scope event buffers removed; expansion drains
  one source.
- **(5)** Result lane + bridge + default method are structural copies of the health
  read-back and pass through the same routing.
- **(6)** Request carries `owner.CombatTargetProxy`; ownerless callers pass
  `Entity.Null`.
- **(7)** Managed counters unchanged; expansion id math reads unchanged fields.

## Minimal/additive vs. refactor comparison

- **Minimal/additive** (keep `CombatRoot` building events into the scope buffers, and
  *also* add a request buffer + intake that unwraps requests back into those same
  buffers):
  - resulting data flow: managed still builds events; a request layer wraps then an
    intake unwraps to the identical event, into the identical scope buffer; two event
    channels remain.
  - new concepts/types: `CombatSpawnRequest` **plus** the still-live managed event
    build path; a wrap/unwrap shim.
  - copies/translations added: request→event re-expansion that reproduces what managed
    already built; the event is represented twice.
  - long-term cost: two spawn-submission representations, an intake that exists only to
    forward, and no single place that "owns" spawn decisions — the mana gate would sit
    beside a redundant managed builder.
- **Refactor** (move event construction into intake; managed builds only requests;
  remove the scope event buffers — chosen):
  - resulting data flow: one managed submission path (request buffer) → one ECS
    processor (intake) → one event channel (queue) → expansion.
  - existing concepts/types changed: `CombatRoot` submission methods (build request,
    not event), `CombatScopeOwner` (drop event buffers, add request buffer + result
    lane), the two expansion systems (drop scope-buffer drain).
  - copies/translations removed: no managed event builder, no scope event buffer, no
    queue/buffer merge.
  - long-term benefit: `SpawnIntakeSystem` is the single, obvious home for the mana
    gate; ECS owns "decide + emit + reply"; managed owns "register + submit".
- **Decision:** **choose refactor.** It collapses three managed event writes and a
  redundant event representation into one request path and one processor, which is
  exactly the seam mana needs.

## Default decision rule applied

`CombatSpawnRequest` and the spawn *events* are near-identical in shape, but they are
**different domain stages** — pre-authorization cast intent (managed, carries caster)
vs. post-authorization allocation intent (ECS-internal). That distinction is the whole
point of the mana gate, so a distinct request type is warranted. Within the managed
side, however, the three per-kind event writes are one concept (managed spawn
submission) and collapse to the single request buffer — one source of truth for
"managed asked for a spawn".

## Task list

- [001-spawn-request-contract.md](001-spawn-request-contract.md) — Define
  `CombatSpawnRequest` (+ scope buffer) and `CombatSpawnResult` (+ result singleton
  lane); wire both into `CombatScopeOwner`. No behavior change yet.
- [002-spawn-intake-system.md](002-spawn-intake-system.md) — `SpawnIntakeSystem`:
  drain requests, build the per-kind spawn event into the existing `NativeQueue`
  lanes, emit results. Relocates event construction out of `CombatRoot`.
- [003-managed-submission-and-caster.md](003-managed-submission-and-caster.md) — Flip
  `CombatRoot.Spawn*`/`SpawnRegistered*` to append requests (+ `Entity caster`); plumb
  the caster proxy from `SkillDriver`/`SkillSpawnTranslator`; delete the scope event
  buffers and the expansion scope-buffer drain.
- [004-spawn-result-reply.md](004-spawn-result-reply.md) — `CombatSpawnResultBridge`
  and the `ICombatTarget.ReceiveSpawnResult` default no-op stub (consuming method
  ready for mana).
- [005-tests-and-docs.md](005-tests-and-docs.md) — Update play-mode tests for the
  deferred (intake-produced) spawns; update the spawn/bridge/flow docs.

Dependencies: 002 depends on 001. 003 depends on 001 + 002. 004 depends on 001 + 002.
005 depends on 002 + 003 + 004.

## Open questions / considerations

- **Deferred spawn timing in tests.** Today `CombatRoot.Spawn(...)` writes the event
  synchronously, so a test can `Spawn` then advance one world update and observe the
  entity. After this change the event is produced by intake *during* the tick, still
  the same frame the request is submitted — so a submit-then-`World.Update()` test
  sees the entity on the same schedule. Task 005 verifies no test relied on the event
  existing in the scope buffer *before* the tick. (No test captures the returned id.)
- **Future mana reject wastes a pre-allocated id / `spawnedAoes` count.** Because id
  and stat allocation stay managed (pre-gate), a cast the mana gate later rejects will
  have consumed an id and incremented `spawnedAoes`. Harmless now (nothing rejects).
  The mana task should decide whether to move post-gate id/stat allocation into intake
  then; it is out of scope here.
- **`CombatSpawnResult` is intentionally near-empty.** It carries only the caster
  `Entity` needed to route the reply. Accept/reject, assigned id, and cost fields are
  added with mana. Confirm this matches the "empty struct for now" intent.
- **Intake threading.** Recommended main-thread `SystemBase` (cast counts are low; the
  multiplicity explosion is downstream in expansion). A Burst `IJob` is unnecessary
  this task and can come with mana if profiling wants it.
- **`SkillDriver` → caster proxy wiring.** `SkillDriver` reads its owner via
  `GetComponent<ICombatTarget>()` (co-located `PlayerRoot`/`MobRoot`) to pass
  `owner.CombatTargetProxy`; ownerless callers pass `Entity.Null`.
