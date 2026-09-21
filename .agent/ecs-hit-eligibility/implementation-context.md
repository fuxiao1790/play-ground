# Implementation Context

## Architectural Decisions
- Expand existing `TargetFaction` (no new component/array) with `FilterMode`
  (`HostileOnly = 0`, `AllowedFactionOnly = 1`) and `AllowedAttackerFaction`.
- One Burst-safe `CanHit(CombatFaction attacker, in TargetFaction target)` helper
  replaces every direct same-faction comparison across collision, acquisition,
  tracking.
- Aggregate result path preserved: one `CombatTickResult` per changed target per
  finalizer update; add `TickDeltaSeconds`; `HitCount` now increments once per
  accepted `CombatHitEvent` (including non-damaging/status-only), independent of
  `DirectDamageEnabled`-gated `DamageTaken`/`CritCount`.

## Global Invariants
1. ECS jobs read unmanaged proxy data only — no GameObject/Transform/Collider2D/
   managed TargetCompanion access.
2. One target schema/spatial hash for all factions; no faction-specific hash or
   archetype.
3. Eligibility stamped at proxy creation, immutable for proxy lifetime (no
   runtime mutation path in this scope).
4. Target snapshot arrays stay index-aligned; no new list/allocation/handle.
5. Collision/acquisition hot paths stay Burst/job-safe, allocation-free, no
   managed dispatch or lookups.
6. Domain identity (projectile/AOE/targeted tags, pooling/archetypes) unchanged.
7. Hit delivery stays aggregated per changed target; managed work doesn't scale
   with raw hit count.
8. Producer/consumer ownership of `CombatHitDispatchSingleton` unchanged; no new
   queue/handle/consumer/disposal.
9. `CombatApplyFinalizeSingleSystem` snapshots `SystemAPI.Time.DeltaTime` into
   each result; bridge never recomputes from `UnityEngine.Time`.
10. Bridge remains result-driven: callback only when result has hit/status
    content; zero-result ticks produce no callback.
11. Agent writes tests but does NOT run them. User runs tests and provides XML
    under `Logs/` for review.

## Ownership Boundaries
- Faction + hit-acceptance policy owned entirely by `TargetFaction` (target
  proxy side), not by attacker/source components.
- `CombatFaction.None` is never a valid attacker; centrally rejected by the
  helper even if `AllowedAttackerFaction == None`.

## Data Flow
- `TargetProxyEvents` creation event carries full `TargetFaction` snapshot ->
  `TargetProxyCreateApplySystem` stamps it -> `TargetSpatialHashSystem` gathers
  existing `NativeList<TargetFaction>` (unchanged container, expanded fields
  copy automatically) -> collision/acquisition/tracking systems call shared
  `CanHit` helper against snapshot -> accepted contact -> `CombatHitEvent` ->
  `CombatApplyFinalizeSingleSystem` (increments `HitCount`, stamps
  `TickDeltaSeconds`, does direct-damage/crit/health only inside
  `DirectDamageEnabled`) -> `CombatApplyResultSingleton` /
  `CombatTickResult` -> `CombatApplyBridge` -> `ICombatTarget.ReceiveCombatTick`.

## Lifecycle / Allocation Rules
- No new native container, snapshot array, or disposal obligation anywhere.
- `TargetFaction` stays inside existing target archetype; component count
  unchanged.

## ECS / Job / Threading Constraints
- Helper must be static, Burst-compatible, value-comparison only.
- No structural changes inside hot loops.

## Determinism Requirements
- Preserve existing tie-breaking, pierce/candidate caps, contact/repeat-hit
  gates, and broadphase/narrowphase ordering exactly; eligibility is an added
  predicate, not a reordering.

## Producer / Consumer Separation
- Producers append to `CombatHitDispatchSingleton`; finalizer completes
  producers; bridge completes result producer before replay/clear. No changes
  to this shape.

## Reused Mechanisms
- `TargetFaction` component, `TargetSpatialHashSingleton.TargetFactions`.
- `TargetProxyCreateEvent` -> `TargetProxyCreateApplySystem`.
- Existing projectile/AOE/targeted identity faction fields.
- `CombatTargetAcquisition` shared nearest-target logic.
- `CombatHitDispatchSingleton` / `CombatHitEvent` producer lane.
- Per-target accumulation in `CombatApplyFinalizeSingleSystem`.
- `CombatApplyResultSingleton`, `CombatTickResult`, `CombatApplyBridge`,
  `ICombatTarget.ReceiveCombatTick`.

## Introduced Mechanisms
- `TargetFactionFilterMode` enum (byte): `HostileOnly = 0`,
  `AllowedFactionOnly = 1`.
- `TargetFaction.FilterMode`, `TargetFaction.AllowedAttackerFaction` fields +
  named factories for hostile-default / selected-faction construction.
- One static Burst-safe `CanHit(CombatFaction attacker, in TargetFaction target)`
  helper.
- `CombatTickResult.TickDeltaSeconds : float`.

## Validation Requirements
- Agent must NOT run tests; only write them.
- After implementation, request user run:
  - EditMode -> `Logs/TestResults-EditMode-EcsHitEligibility.xml`
  - PlayMode -> `Logs/TestResults-PlayMode-EcsHitEligibility.xml`
- Compile-correctness can be checked via code reading / static search since no
  build tool is run by the agent.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Targets/CombatTargetProxy.cs`
- `Assets/Scripts/System/Targets/TargetProxyEvents.cs`
- `Assets/Scripts/System/Targets/TargetProxyCreateApplySystem.cs` (name inferred,
  verify exact path)
- `CombatTargetRegistry` (verify exact path)
- `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs`
- `ProjectileDiscreteCollisionSystem`, `ProjectileContinuousCollisionSystem`
- `AoeCollisionCore`
- `CombatTargetAcquisition`
- `ProjectileTrackingSystem`
- `ExternalSpawnGateSystem`, `TargetedResolveSystem`,
  `ProjectileSpawnExpansionSystem`
- `Assets/Scripts/System/Application/CombatApplyResults.cs`
- `Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs`
- `Assets/Scripts/System/Presentation/CombatApplyBridge.cs`
- `ICombatTarget`
- Docs: `Docs/contracts/target-proxy.md`,
  `Docs/contracts/combat-hit-and-tick-results.md`,
  `Docs/flows/collision-to-combat-result.md`, `Docs/layers/ecs-simulation.md`,
  `Docs/layers/presentation-and-feedback.md`, `Docs/flows/runtime-frame.md`,
  `Docs/reference/simulation/projectile-system.md`,
  `Docs/reference/simulation/targeted-system.md`,
  `Docs/reference/simulation/project-ecs-implementation.md`,
  `Docs/reference/simulation/spawn-template-registry.md`,
  `Docs/contracts/spawn-events-and-commands.md`
