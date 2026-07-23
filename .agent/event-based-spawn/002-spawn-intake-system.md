# 002 — Spawn intake system

Add `SpawnIntakeSystem`: the ECS processor that turns `CombatSpawnRequest`s into spawn
events and emits results. This relocates the per-kind event construction that lives in
`CombatRoot` today into ECS.

## Placement / ordering

- `[UpdateInGroup(typeof(SimulationSystemGroup))]`
- `[UpdateBefore(typeof(TimedSpawnSystem))]` — runs before the other queue producer, so
  its main-thread `NativeQueue` enqueue cannot race a `TimedSpawnSystem` producer job,
  and therefore before `ProjectileSpawnExpansionSystem` / `AoeSpawnExpansionSystem`
  (which are `[UpdateAfter(TimedSpawnSystem)]`).
- Managed `SystemBase`, main-thread (cast counts are low; recommended over a job for
  this task — see index open questions).

## Lifecycle

- `OnCreate`: create the result singleton entity and its
  `NativeList<CombatSpawnResult>(Allocator.Persistent)`.
- `OnDestroy`: complete `ProducerHandle`, dispose the list. Mirror
  `ProjectileSpawnExpansionSystem` / `CombatApplyBridge` disposal guards.

## OnUpdate

1. Clear the result list (results are single-frame, consumed by the bridge in
   Presentation the same frame).
2. Query the scope(s) (`CombatScope` + `CombatSpawnRequest`), same
   `ToEntityArray(Allocator.Temp)` shape the expansion systems use.
3. For each scope, read `DynamicBuffer<CombatSpawnRequest>`; for each request:
   - **[future mana gate goes here]** — this task always accepts.
   - Build the event for `request.Kind` and enqueue into the matching lane singleton's
     `NativeQueue`:
     - `Projectile` → `ProjectileSpawnEventSingleton.EventQueue.Enqueue(new
       ProjectileSpawnEvent{ ... })`
     - `ImpactAoe` → `ImpactAoeSpawnEventSingleton.EventQueue.Enqueue(...)`
     - `LingeringAoe` → `LingeringAoeSpawnEventSingleton.EventQueue.Enqueue(...)`
     The event field mapping is a straight copy from the request (`TemplateKey`,
     `Position`, `AimDirection`, `Faction`, `SourceId`→`SourceId`, `JitterSeed`,
     `ContactGateSeedTargetId`; `DeterministicIdTickIndex = 0`). This is exactly what
     `CombatRoot.ProjectileEventFor` / `AppendAoeSpawnEvent` produce today, so lift
     that construction here.
   - Append `new CombatSpawnResult { Caster = request.Caster }` to the result list.
   - Then `buffer.Clear()` the request buffer for that scope.
4. Set `ProducerHandle = default` (main-thread writes; no scheduled producer job this
   task).

Because intake runs before `TimedSpawnSystem` and expansion completes each lane's
`ProducerHandle` before draining, the enqueued events are picked up by expansion the
same frame — identical timing to the current managed scope-buffer writes.

## Notes

- Enqueue directly to the lane singletons via
  `SystemAPI.GetSingletonRW<...SpawnEventSingleton>()`. These singletons are created in
  the expansion systems' `OnCreate`, which run before any `OnUpdate`, so they exist.
- Do **not** touch the template registry; the event is a slim link and expansion
  dereferences it (invariant 1).
- Keep the intake resilient to a missing scope / missing lane singleton (early-out
  like the expansion systems do), so ownerless test worlds don't throw.

## Acceptance criteria

- With task 003 not yet applied, intake is inert (no requests are produced yet), so
  existing spawns still work via the old managed event-buffer path — no regression.
- Unit/edit check: submitting a hand-written `CombatSpawnRequest` into the scope
  buffer (test-only) results in the corresponding entity after one world update.
- No registry writes, no structural changes, no managed refs read in the system.

## Dependencies

001 (types + lane). Precedes 003 (which starts producing requests) and 004 (which
consumes results).

## Scope / complexity

Medium. One system; the event-build logic is lifted from `CombatRoot`.
