# Task Execution Packet

## Task
004-regression-tests.md

## Goal
Add regression coverage proving: (a) the shared `TargetFaction.CanHit`
eligibility rule behaves correctly in isolation, (b) every independent
selection/collision path (projectile discrete/continuous, AOE, targeted
root/chain, launch aim, tracking acquire/refresh) obeys the same policy, and
(c) the finalizer/bridge result semantics (`HitCount` including status-only
hits, `TickDeltaSeconds`) are correct. **You write tests only. Do not run
them** — the user runs EditMode/PlayMode suites afterward and reviews XML
output.

## Files Allowed To Modify
- `Assets/Tests/EditMode/TargetedResolveEditModeTests.cs`
- `Assets/Tests/EditMode/CombatHitDamageScaleEditModeTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileContinuousSimulationTests.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`

## Files Allowed To Create
- One new EditMode test file for the shared eligibility truth table, e.g.
  `Assets/Tests/EditMode/TargetFactionEligibilityEditModeTests.cs` (exact name
  your call; follow existing naming convention `*EditModeTests.cs`). Also
  create its matching `.meta` only if your environment requires manually
  created `.meta` files for new C# files in this Unity project — check whether
  other recently-added test files have committed `.meta` companions before
  deciding; if Unity auto-generates them on next editor import, you do not
  need to hand-author one.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading (do not modify beyond the allowed list)
- `Assets/Scripts/System/Targets/CombatTargetProxy.cs` — current
  `TargetFaction` shape: `Value : CombatFaction`, `FilterMode :
  TargetFactionFilterMode` (`HostileOnly = 0`, `AllowedFactionOnly = 1`),
  `AllowedAttackerFaction : CombatFaction`; static factories
  `TargetFaction.Hostile(CombatFaction ownFaction)` and
  `TargetFaction.AllowedFrom(CombatFaction ownFaction, CombatFaction allowedAttacker)`;
  static predicate `TargetFaction.CanHit(CombatFaction attacker, in TargetFaction target)`
  (rejects `CombatFaction.None` attacker first; `HostileOnly` →
  `attacker != target.Value`; `AllowedFactionOnly` →
  `attacker == target.AllowedAttackerFaction`; unknown mode → `false`). Also
  `CombatTargetProxy.Create(EntityManager, ICombatTarget, TargetFaction policy)`
  (new overload from task 001) alongside the original
  `Create(EntityManager, ICombatTarget, CombatFaction faction)` (now a thin
  hostile-default wrapper).
- `Assets/Scripts/System/Application/CombatApplyResults.cs` — current
  `CombatTickResult`: `TargetProxy`, `Health`, `DamageTaken` (direct-damage-only),
  `HitCount` (now: accepted-hit count including status-only, from task 003),
  `CritCount` (direct-damage-only), `StatusStart`, `StatusCount`,
  `TickDeltaSeconds` (new field from task 003, set from the finalizer's
  `SystemAPI.Time.DeltaTime`).
- `Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs` — see
  `CombatHitDamageScaleEditModeTests.cs` (read below) for the exact working
  pattern this file's tests already use to drive it directly in EditMode.
- `Assets/Tests/EditMode/CombatHitDamageScaleEditModeTests.cs` — READ THIS
  FIRST for the finalizer test pattern: it creates a `World`, gets
  `CombatApplyFinalizeSingleSystem` via `GetOrCreateSystemManaged`, creates a
  source entity with `CombatHitPayload` and a target entity with `Health`,
  enqueues raw `CombatHitEvent`s directly onto the `CombatHitDispatchSingleton.HitQueue`,
  calls `finalizeSystem.Update()`, then reads the single
  `CombatApplyResultSingleton.Results[0]`. Extend this exact pattern for the
  new count/tick-delta test methods (task 004 change-item 4) — do not
  reinvent a different fixture.
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`,
  `ProjectileContinuousSimulationTests.cs`, `AoeSimulationTests.cs`,
  `ProjectileTrackingSimulationTests.cs`, `ProjectileSpawnPipelineTests.cs`,
  `Assets/Tests/EditMode/TargetedResolveEditModeTests.cs` — read each file's
  existing fixture/helper methods (proxy creation, world setup, hash build)
  before adding new test methods; match each file's existing style and helper
  reuse rather than duplicating setup.
- `Assets/Scripts/System/Core/CombatScope.cs` — `CombatFaction` enum
  (`None = 0, Player = 1, Mob = 2`). There are only two non-None concrete
  factions today; "selected-faction target accepting its own faction" test
  cases should use `AllowedFrom(CombatFaction.Player, CombatFaction.Player)`-
  style same-faction construction, and "unselected faction" cases should use
  the other concrete faction (`Mob`) as the rejected attacker.

## Behavior To Preserve
- Every existing test in the 6 modified files and 1 new file must keep passing
  under hostile-default semantics — you are adding new test methods, not
  changing existing ones, except where task 004 change-item 5 explicitly says
  to update fixtures constructing `TargetFaction`/proxy-creation requests to
  use hostile-default policy explicitly for clarity (optional, cosmetic only;
  do not change their behavior).
- Do not add production-only hooks/backdoors to make testing easier — task 004
  explicitly forbids this.
- Do not introduce dependence on unordered queue/hashmap iteration in any new
  assertion.

## Behavior To Change (i.e., what to add)
Per `004-regression-tests.md`'s own change list — treat that file as the
authoritative acceptance-criteria source; this packet summarizes it:

1. **New EditMode truth-table test file** for `TargetFaction.CanHit`:
   - hostile mode rejects same faction, accepts different faction;
   - selected mode (`AllowedFactionOnly`) accepts the selected faction
     including when it equals the target's own faction;
   - selected mode rejects a non-selected faction;
   - `CombatFaction.None` as attacker always rejects, regardless of mode;
   - an unknown/out-of-range `TargetFactionFilterMode` value rejects.
   This is a pure unit test over the static method — no ECS `World` needed
   unless you find it clearer to construct `TargetFaction` structs directly
   and call `TargetFaction.CanHit(attacker, in target)` inline.

2. **Collision integration coverage** — extend:
   - `ProjectileCollisionSimulationTests.cs`: add a same-faction-hit test
     using a target proxy created with `TargetFaction.AllowedFrom(faction, faction)`
     for the discrete collision path, and an unselected-faction-rejection test.
   - `ProjectileContinuousSimulationTests.cs`: same two cases for the
     continuous collision path.
   - `AoeSimulationTests.cs`: same selected-faction behavior through
     `AoeCollisionCore`; cover at least one impact-AOE case plus a
     lingering-AOE case if needed to prove the repeat/tick gate still applies
     under selected-faction policy (check existing lingering tests in this
     file for the gate pattern to reuse).

3. **Target-selection coverage** — extend:
   - `TargetedResolveEditModeTests.cs`: root/chain selection picks a
     policy-eligible target and skips a nearer policy-ineligible one (note:
     this file already calls `CombatTargetAcquisition.TryNearestEligible`
     post-task-002 rename — reuse existing helpers).
   - `ProjectileSpawnPipelineTests.cs`: nearest launch-aim test proving it uses
     policy, including a same-faction-eligible-target case.
   - `ProjectileTrackingSimulationTests.cs`: acquire/refresh keeps a
     selected-faction target and rejects a closer unselected-faction target.

4. **Result/finalizer coverage** — extend `CombatHitDamageScaleEditModeTests.cs`
   using its existing `FinalizeDirectHits`-style pattern (or a new sibling
   private helper in the same file if a status-only/mixed-event case doesn't
   fit that exact helper's signature):
   - direct hits still aggregate damage/crit correctly (should already pass
     via existing tests — add only what's missing);
   - a non-damaging/status-only accepted event (payload with
     `DirectDamageEnabled = false`, `StackEffect.Enabled = true`) increments
     `HitCount` but leaves `DamageTaken`/`CritCount` untouched;
   - multiple mixed direct+status events for one target aggregate into one
     result with total `HitCount` and direct-damage-only totals;
   - setting a known world delta time yields a matching
     `CombatTickResult.TickDeltaSeconds` (you will need to advance/set the
     test `World`'s time — check how `SystemAPI.Time.DeltaTime` is driven in
     this EditMode `World` fixture; if the existing fixture doesn't expose a
     way to set delta time, use `World.SetTime(new TimeData(...))` or the
     equivalent DOTS API compatible with this project's Entities package
     version — grep the codebase for any existing `World.SetTime`/`.Time =`
     usage in other EditMode tests for the established pattern before adding
     a new one);
   - a test-owned `ICombatTarget` listener (implement the interface minimally
     in a test-local class) captures the bridge callback via
     `CombatApplyBridge` and observes identical `HitCount`/`TickDeltaSeconds`
     to what the finalizer produced, with presentation replay occurring
     exactly once for the aggregated result. If wiring a full bridge pass is
     significantly more complex than the existing EditMode fixture supports,
     you may instead directly assert on `CombatApplyResultSingleton.Results[0]`
     (as `CombatHitDamageScaleEditModeTests` already does) AND separately
     confirm, by code reading, that `CombatApplyBridge.ReplayCombat` forwards
     `HitCount`/`TickDeltaSeconds` unchanged (it does — no bridge-side
     transformation exists). If you judge a full bridge-callback test
     infeasible in EditMode without PlayMode-only infra, document that
     decision plainly in your final report rather than inventing new
     production test hooks.

5. **Fixture clarity pass** (optional/cosmetic): where existing fixtures in
   the touched files construct `TargetFaction { Value = X }` directly or call
   `CombatTargetProxy.Create(entityManager, target, someFaction)`, you may
   leave them as-is (the compat overload already means hostile-default) or
   switch a few to `TargetFaction.Hostile(X)` explicitly for readability. Do
   not do this broadly — only where you're already touching the surrounding
   code for a new test.

## Relevant Global Context
- Agent must NOT run tests. After finishing, tell the user exactly which
  suites to run and where to export XML, per task 004's own "User-Run
  Verification" section:
  - EditMode → `Logs/TestResults-EditMode-EcsHitEligibility.xml`
  - PlayMode → `Logs/TestResults-PlayMode-EcsHitEligibility.xml`
- No summon/firing-proxy fixture or GameObject behavior may be introduced —
  stay within existing ECS-proxy-based test fixtures.
- Each distinct consumer of faction eligibility (projectile discrete,
  projectile continuous, AOE impact, AOE lingering, targeted root, targeted
  chain, launch aim, tracking acquire, tracking refresh) needs at least one
  behavioral test proving it honors `AllowedFactionOnly` policy, per
  `004-regression-tests.md`'s acceptance criteria — treat this as the
  completeness bar, not every file needing many new tests.

## Dependencies Confirmed
- Tasks 001–003 complete and verified by the orchestrator:
  - `TargetFaction.FilterMode`/`AllowedAttackerFaction`/`Hostile`/`AllowedFrom`
    exist (task 001).
  - `TargetFaction.CanHit` exists and is used by
    `CombatTargetAcquisition.TrySelectNthNearest`/`TryNearestEligible`,
    `ProjectileDiscreteCollisionSystem`, `ProjectileContinuousCollisionSystem`,
    `AoeCollisionCore`, `ProjectileTrackingSystem` (task 002).
  - `CombatTickResult.TickDeltaSeconds` exists; `HitCount` increments for
    every accepted hit including status-only (task 003).

## Step-By-Step Instructions
1. Read `CombatHitDamageScaleEditModeTests.cs` fully first (pattern source for
   item 4).
2. Read each of the 5 other files-to-modify's existing fixture/helper code to
   learn their established proxy-creation and world-setup patterns.
3. Create the new eligibility truth-table EditMode test file (item 1).
4. Add the collision integration test methods (item 2) to the 3 named
   PlayMode files.
5. Add the target-selection test methods (item 3) to the 3 named files.
6. Add the result/finalizer test methods (item 4) to
   `CombatHitDamageScaleEditModeTests.cs`.
7. Optionally apply the cosmetic fixture pass (item 5) only where natural.
8. Re-grep all 7 touched/created files for obviously broken references (e.g.
   confirm every new call to `TargetFaction.AllowedFrom`/`CanHit`/
   `CombatTargetProxy.Create(..., TargetFaction)` matches the real signatures
   read in step 1–2).

## Acceptance Criteria
(From `004-regression-tests.md` — authoritative; summarized)
- Each distinct consumer of faction eligibility has behavioral coverage.
- Existing same-faction rejection tests continue passing under hostile-default
  mode (i.e., you did not alter their meaning).
- New tests prove same-faction acceptance occurs only under selected-faction
  mode.
- Result tests distinguish accepted-hit count from damage and crit counts.
- A test observes final `HitCount`/`TickDeltaSeconds` values consistent with
  what the finalizer produced (via bridge callback if feasible, else via
  direct result assertion plus documented code-reading confirmation of
  bridge pass-through, per item 4's fallback above).
- Tests do not depend on unordered queue iteration.
- No concrete summon/firing-proxy fixture or GameObject behavior is
  introduced.

## Validation Required
- Agent must NOT run tests (Unity EditMode/PlayMode runner unavailable to this
  agent, and task 004 explicitly forbids it regardless).
- Static validation only: grep each new/modified test file for
  compile-plausible API usage against the verified signatures in "Files
  Likely Needed For Reading" above; confirm no test references a symbol that
  doesn't exist per those verified shapes.
- In your final report, explicitly list the exact suites/classes/methods the
  user should run and the two XML export paths from task 004's "User-Run
  Verification" section.

## Hard Boundaries
- Do not modify any non-test file (no production code changes — tasks
  001–003 already made every production change this task depends on).
- Do not modify test files outside the 7 explicitly listed/allowed.
- Do not add documentation (task 005).
- Do not run tests or claim you ran them.
- Do not add production-only test hooks/backdoors.
- Do not introduce new abstractions beyond ordinary test helper methods
  matching each file's existing style.
- Do not combine this task with task 005.
- Do not reopen index-level decisions.
- Stop and report if a file's actual fixture pattern differs enough from this
  packet's description that the intended test can't be added without
  inventing new test infrastructure — report the gap rather than improvising
  a new pattern silently.
