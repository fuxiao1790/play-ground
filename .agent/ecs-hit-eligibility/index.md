# ECS Faction Hit Eligibility And Tick Result Plan

## Summary

Extend target faction data so every ECS target proxy can select either current
different-faction behavior or one explicitly allowed attacker faction. Keep one
target snapshot and one hit/result path: projectile, AOE, targeted acquisition,
and projectile tracking all call one Burst-safe eligibility rule; accepted
contacts continue through `CombatHitEvent`, `CombatApplyFinalizeSingleSystem`,
`CombatTickResult`, and `CombatApplyBridge`.

`CombatTickResult.HitCount` will mean number of accepted `CombatHitEvent` values
for target during that simulation update, including non-damaging/status-only
hits. Add `TickDeltaSeconds` to same result so managed target receiving existing
`ReceiveCombatTick` callback knows aggregation interval without reading frame
time independently.

Scope contains ECS contracts, systems, bridge result data, tests, and their
documentation. Summoned GameObject/firing-proxy behavior, movement, energy
gauge, skill activation, prefab authoring, and content wiring are explicitly out
of scope.

## Architectural Decisions

### Keep eligibility inside existing `TargetFaction`

Expand `TargetFaction` with:

- target own `Value` faction;
- `TargetFactionFilterMode`;
- `AllowedAttackerFaction`.

Modes in this scope:

- `HostileOnly = 0`: attacker must differ from target own faction. Zero keeps
  existing `new TargetFaction { Value = ... }` setup behavior.
- `AllowedFactionOnly = 1`: attacker must equal `AllowedAttackerFaction`, even
  when attacker and target own factions match.

`CombatFaction.None` is never a valid attacker. An `AllowedFactionOnly` target
configured with `AllowedAttackerFaction.None` is therefore unhittable.

Do not add separate `TargetHitFilter` component or parallel native list. Faction
and faction-based acceptance form one target-side contract, and every target
already flows through `TargetFaction`.

### One eligibility rule

Add one Burst-compatible helper accepting attacker faction and target
`TargetFaction`. Replace every direct same-faction comparison in collision,
acquisition, and tracking. Selection and eventual collision must use identical
eligibility semantics; otherwise projectiles can aim at or track targets they
cannot hit.

Every hittable target participates in acquisition exactly like any other valid
target. `AllowedFactionOnly` does not mean collision-only: a qualifying proxy may
be chosen by interval-trigger projectile launch aim, may orient the resulting
aimed nova, may be selected by targeted chains, and may be acquired or retained
by homing projectiles. Do not add a separate auto-targetable flag.

### Preserve aggregate result path

Do not create charge-hit, summon-hit, or managed per-hit lanes. Increment
`HitCount` once per accepted `CombatHitEvent` before direct-damage-specific work.
Damage and crit aggregation remain conditional on `DirectDamageEnabled`; status
processing remains unchanged.

Store `TickDeltaSeconds` on each `CombatTickResult`. Results scale with changed
target count, not projectile count, and placing timing in result preserves the
existing callback signature and avoids separate batch metadata or a second
bridge path.

## Constraints And Invariants

1. **ECS jobs read unmanaged proxy data only.** Simulation must not inspect
   `GameObject`, `Transform`, `Collider2D`, or managed `TargetCompanion`.
   Sources: `Docs/architecture/layer-rules.md`,
   `Docs/layers/ecs-simulation.md`, `Docs/contracts/target-proxy.md`.
   Result: eligibility remains blittable data in `TargetFaction` and a static
   Burst-safe helper.

2. **All factions share one target schema and spatial hash.** Faction is target
   data, not scope or domain identity. Sources:
   `Docs/contracts/target-proxy.md`, `Docs/coding-standards.md`,
   `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs`.
   Result: no faction-specific hash, target archetype, or system is introduced.

3. **Proxy data must exist before target hash build.** Create/update systems run
   before `TargetSpatialHashSystem`; deletion waits until presentation after
   result replay. Sources: `Docs/flows/runtime-frame.md`,
   `Docs/contracts/target-proxy.md`.
   Result: eligibility is stamped during proxy creation and treated as immutable
   for proxy lifetime in this scope, matching existing `TargetFaction.Value`.

4. **Target snapshot list order must remain index-aligned.** Entity, position,
   shape, and faction arrays are gathered in one sequential `.Run()` pass and
   consumed by jobs after `BuildHandle`. Source:
   `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs`.
   Result: expanding existing `TargetFaction` preserves one aligned copy and
   adds no list, allocation, handle, or disposal obligation.

5. **Collision and acquisition hot paths stay Burst/job compatible and
   allocation-free.** Sources: `Docs/reference/simulation/ecs-notes.md`,
   `Docs/coding-standards.md`.
   Result: helper uses value comparisons only; no managed dispatch, lookup,
   allocation, structural change, or extra random access.

6. **Domain identity stays explicit.** Projectile, AOE, and targeted systems
   retain their domain tags and current pool/archetype behavior. Sources:
   `Docs/coding-standards.md`, `Docs/layers/ecs-simulation.md`.
   Result: target eligibility does not alter source archetypes or pooling.

7. **Hit delivery remains aggregated per changed target.** Managed work must not
   scale with raw hit count. Sources:
   `Docs/decisions/adr-006-ecs-aggregated-combat-results.md`,
   `Docs/contracts/combat-hit-and-tick-results.md`.
   Result: accepted hits still collapse into one `CombatTickResult` and one
   `ReceiveCombatTick` call per target.

8. **Shared native lanes retain explicit producer ownership.** Producers append
   to `CombatHitDispatchSingleton`; finalizer completes producers; bridge
   completes result producer before replay and clearing. Sources:
   `Docs/coding-standards.md`,
   `Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs`,
   `Assets/Scripts/System/Presentation/CombatApplyBridge.cs`.
   Result: no new queue, job handle, consumer, or disposal lifecycle.

9. **Tick duration means ECS update duration.** ECS simulation owns time-step
   input. Source: `Docs/layers/ecs-simulation.md`. Result:
   `CombatApplyFinalizeSingleSystem` snapshots `SystemAPI.Time.DeltaTime` into
   each result made during that update. Bridge does not recompute it from
   `UnityEngine.Time`.

10. **Bridge is result-driven, not an every-tick actor driver.** Current bridge
    calls targets only when a result has hit/status content. Sources:
    `Assets/Scripts/System/Presentation/CombatApplyBridge.cs`,
    `Docs/layers/presentation-and-feedback.md`. Result: actors get tick duration
    only with dispatched result; zero-result simulation ticks produce no managed
    callback.

11. **Tests are executed by user and judged from XML under `Logs/`.** Source:
    `Docs/project-overview.md`, `Docs/testing.md`. Result: implementation agent
    writes tests but does not run them; user provides result XML for review.

## Mechanisms Reused Vs. Introduced

### Reused

- `TargetFaction` component and `TargetSpatialHashSingleton.TargetFactions`
  snapshot.
- `TargetProxyCreateEvent` -> `TargetProxyCreateApplySystem` creation flow.
- Existing projectile/AOE/targeted identity faction fields.
- `CombatTargetAcquisition` shared nearest-target logic.
- `CombatHitDispatchSingleton` and `CombatHitEvent` producer lane.
- Per-target accumulation in `CombatApplyFinalizeSingleSystem`.
- `CombatApplyResultSingleton`, `CombatTickResult`, `CombatApplyBridge`, and
  `ICombatTarget.ReceiveCombatTick`.

### Introduced

- `TargetFactionFilterMode` with two explicit modes.
- Two fields on `TargetFaction`: filter mode and allowed attacker faction.
- One Burst-safe faction eligibility helper.
- `CombatTickResult.TickDeltaSeconds`.

No new lane, target archetype, snapshot array, managed callback, or GameObject
component is introduced.

## Design Validation

- **Same-faction allowed target:** `AllowedFactionOnly` compares attacker to
  selected faction rather than target own faction, so same-faction contact can
  qualify.
- **Unselected faction:** rejected by acquisition, tracking refresh/acquisition,
  and collision through same helper.
- **Existing actors:** default mode value is `HostileOnly`; existing explicit
  `Value` initialization preserves player-vs-mob behavior.
- **Invalid source:** `CombatFaction.None` is rejected centrally even if allowed
  field is also `None`.
- **Targeting consistency:** root targeted acquisition, targeted chain walk,
  launch aim, homing acquire/refresh, projectile collision, and AOE collision
  all evaluate same rule.
- **Interval-trigger aim:** an `AllowedFactionOnly` proxy is a normal candidate
  for nearest-target launch aim and can orient the complete triggered nova when
  it wins nearest eligible selection.
- **Aggregation:** N accepted events for one proxy produce one result with
  `HitCount == N`; direct damage and crit counts keep their narrower meanings.
- **Status-only hit:** increments `HitCount`, can carry status range, applies no
  direct damage, and reaches existing bridge once.
- **Timing:** every result finalized during one ECS update carries same
  `SystemAPI.Time.DeltaTime`; bridge forwards struct unchanged.
- **Performance:** target snapshot grows only by fields inside existing compact
  element; candidate loops add a small mode branch and comparisons, with no
  allocations or component lookups.
- **Lifecycle:** no new native container or structural churn; proxy remains one
  archetype and result lane retains existing clear/dispose behavior.

## Minimal/Additive Vs. Refactor Comparison

### Minimal/additive approach

- **Resulting data flow:** add optional `TargetHitFilter` component, gather a
  second filter array beside `TargetFactions`, thread both through all consumers,
  then add a separate charge/summon result or batch context.
- **New concepts/types introduced:** optional component, native list, snapshot
  field, lifecycle/default handling, possibly new event/result lane.
- **Copies/translations added:** extra gather copy, capacity/resize/clear/dispose
  work, target index alignment requirement, bridge translation.
- **Long-term cost:** faction acceptance split across two target components and
  every consumer must keep parallel arrays aligned; optional/default behavior is
  easy to diverge.

### Refactor approach

- **Resulting data flow:** enrich existing `TargetFaction`; replace all direct
  comparisons with one helper; continue through existing hit and result lane.
- **Existing concepts/types changed or removed:** `TargetFaction` becomes full
  faction-eligibility snapshot; implicit `!=` rules disappear from consumers;
  `HitCount` is defined at accepted-event boundary.
- **Copies/translations removed or avoided:** no additional target array, result
  lane, callback, or per-hit managed replay.
- **Long-term benefit:** one source of truth for faction eligibility and one
  target-result boundary across every combat domain.

### Decision

- **Choose refactor.** It yields fewer runtime paths, no parallel snapshot state,
  consistent targeting/collision, and preserves aggregate bridge architecture.
- Default decision rule applied: one representation and one result path own each
  domain concept; no compatibility/migration need justifies duplicates.

## Task Index

1. [001-target-faction-contract.md](./001-target-faction-contract.md) - expand
   target faction data and proxy creation snapshot.
2. [002-unify-hit-eligibility.md](./002-unify-hit-eligibility.md) - route every
   collision/acquisition/tracking path through one rule.
3. [003-hit-count-and-tick-delta.md](./003-hit-count-and-tick-delta.md) - refine
   aggregate hit semantics and add simulation tick duration to existing result.
4. [004-regression-tests.md](./004-regression-tests.md) - cover policy across all
   source domains and result delivery.
5. [005-document-contracts.md](./005-document-contracts.md) - update authoritative
   and implementation-reference documentation.

## Open Questions And Dependencies

- No unresolved design question blocks implementation.
- Explicit product decision: selection is by attacker faction, not exact
  originating actor. Every attacker in selected faction is eligible.
- Explicit product decision: every eligible/hittable target also participates
  in normal target acquisition. No collision-only or auto-target exclusion bit
  belongs in this change.
- Exact actor ownership propagation is not part of this plan.
- Filter mutation after proxy creation is not required. If later gameplay can
  change target eligibility at runtime, add a documented proxy-update event then;
  do not mutate proxy components from managed code directly.
- Concrete summoned actor/firing proxy and energy/skill behavior remain future
  consumers of this ECS contract.
