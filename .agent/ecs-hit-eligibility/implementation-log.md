# Implementation Log

## Status
Complete

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-target-faction-contract.md | Complete | TargetFaction expanded with FilterMode/AllowedAttackerFaction; hostile-default behavior preserved via compat overload |
| 002-unify-hit-eligibility.md | Complete | Added `TargetFaction.CanHit` helper; replaced all 5 direct faction comparisons; renamed `TryNearestHostile` to `TryNearestEligible` |
| 003-hit-count-and-tick-delta.md | Complete | Added `TickDeltaSeconds`; `HitCount++` moved outside `DirectDamageEnabled` branch |
| 004-regression-tests.md | Complete | Added 1 new EditMode truth-table file + regression methods across 6 existing test files; agent did not run tests (forbidden) |
| 005-document-contracts.md | Complete | Fixed all 8 stale passages + 2 light clarifying additions + 1 extra hostile-wording fix found by grep; 3 files needed no change (verified, not assumed) |

## Completed Tasks

### 001-target-faction-contract.md
- Files changed:
  - `Assets/Scripts/System/Targets/CombatTargetProxy.cs` — added
    `TargetFactionFilterMode` enum (`HostileOnly = 0`, `AllowedFactionOnly = 1`);
    expanded `TargetFaction` with `FilterMode` and `AllowedAttackerFaction`
    fields plus `Hostile(...)`/`AllowedFrom(...)` static factories and an
    `// ECS Lifecycle:` comment; added new `Create(EntityManager, ICombatTarget,
    TargetFaction policy)` overload; reimplemented the existing
    `Create(EntityManager, ICombatTarget, CombatFaction faction)` overload as a
    one-line delegate to the new overload via `TargetFaction.Hostile(faction)`.
  - `Assets/Scripts/System/Targets/TargetProxyEvents.cs` — renamed
    `TargetProxyCreateEvent.Faction : CombatFaction` to
    `FactionPolicy : TargetFaction` (still unmanaged/blittable, creation-only).
  - `Assets/Scripts/System/Targets/TargetProxyCreateApplySystem.cs` — replaced
    `new TargetFaction { Value = createEvent.Faction }` with
    `EntityManager.SetComponentData(proxy, createEvent.FactionPolicy);`.
  - `Assets/Scripts/System/Targets/CombatTargetRegistry.cs` — no edit made.
    `TryCreateProxy` still calls
    `CombatTargetProxy.Create(entityManager, target, target.CombatFaction);`;
    `target.CombatFaction` is `CombatFaction`-typed (see `ICombatTarget.cs:70`),
    so it resolves unambiguously to the preserved compat overload, giving
    identical hostile-default behavior with zero changes to this file. Per the
    packet's explicit preference ("leave this file's call site untouched if the
    compat overload makes it unnecessary"), left untouched.
- Deviations: none. Field was renamed to `FactionPolicy` (packet allowed either
  keeping the name or renaming; `FactionPolicy` reads clearer since it now
  carries a full policy struct, not a bare enum).
- Validation performed (static/grep only — agent did not run Unity or tests):
  - Grepped `Assets` for `createEvent\.Faction\b|\.Faction\s*=\s*|TargetProxyCreateEvent\s*\{` —
    all `.Faction` hits belong to unrelated types (spawn identity/template/command
    structs with their own `Faction : CombatFaction` field), none reference the
    removed `TargetProxyCreateEvent.Faction` member.
  - Grepped for `new TargetProxyCreateEvent` — only one construction site
    (`CombatTargetProxy.cs`), already updated to use `FactionPolicy`.
  - Grepped for `TargetFaction` across `Assets` — all other read sites use
    `.Value` (unchanged in meaning/position) via
    `TargetSpatialHashSystem`'s existing `NativeList<TargetFaction>`/
    `NativeArray<TargetFaction>` gather; no changes needed there, confirmed by
    reading `TargetSpatialHashSystem.cs`.
  - Grepped for `CombatTargetProxy.Create(` — all call sites (production
    `CombatTargetRegistry.cs` and test files
    `ProjectileCollisionSimulationTests.cs`, `AoeSimulationTests.cs`) pass a bare
    `CombatFaction` argument and resolve to the preserved compat overload.
  - Did not run PlayMode/EditMode tests or the Unity compiler (per rulebook and
    packet instructions — agent cannot run these).

### 002-unify-hit-eligibility.md
- Files changed:
  - `Assets/Scripts/System/Targets/CombatTargetProxy.cs` — added static
    `TargetFaction.CanHit(CombatFaction attacker, in TargetFaction target)`
    helper (rejects `CombatFaction.None` first; `HostileOnly` ->
    `attacker != target.Value`; `AllowedFactionOnly` ->
    `attacker == target.AllowedAttackerFaction`; `default: return false;`).
  - `Assets/Scripts/System/Combat/CombatTargetAcquisition.cs` — replaced the
    `snapshot.TargetFactions[targetIndex].Value == faction` skip check in
    `TrySelectNthNearest` with
    `!TargetFaction.CanHit(faction, in snapshot.TargetFactions[targetIndex])`;
    renamed `TryNearestHostile` to `TryNearestEligible` (same signature/
    delegation to `TrySelectNthNearest` with `rank: 0`).
  - `Assets/Scripts/System/Spawning/ExternalSpawnGateSystem.cs` — updated its
    sole call site from `TryNearestHostile` to `TryNearestEligible`.
  - `Assets/Tests/EditMode/TargetedResolveEditModeTests.cs` — updated its call
    site from `TryNearestHostile` to `TryNearestEligible` (name-only change,
    same args/assertions); required for the rename to compile and for the
    "zero remaining references" validation the packet specifies.
  - `Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs` —
    replaced `TargetFactions[targetIdx].Value == identity.Faction` continue-gate
    with `!TargetFaction.CanHit(identity.Faction, in TargetFactions[targetIdx])`.
  - `Assets/Scripts/System/Projectiles/ProjectileContinuousCollisionSystem.cs` —
    same pattern, `targetIndex` variant.
  - `Assets/Scripts/System/Aoes/AoeCollisionCore.cs` — replaced
    `targetFactions[i].Value == identity.Faction` with
    `!TargetFaction.CanHit(identity.Faction, in targetFactions[i])`.
  - `Assets/Scripts/System/Projectiles/ProjectileTrackingSystem.cs` — replaced
    both `TryRefreshTrackedTarget` cached-index/mapped-index
    `TargetFactions[...].Value != identity.Faction` conditions with
    `TargetFaction.CanHit(identity.Faction, in TargetFactions[...])`, and the
    `TrySelectRandomTargetInCell` `TargetFactions[targetIndex].Value ==
    projectileFaction` continue-gate with
    `!TargetFaction.CanHit(projectileFaction, in TargetFactions[targetIndex])`.
- Deviations: none. `TargetedResolveSystem.cs` and
  `ProjectileSpawnExpansionSystem.cs` needed no changes — both call
  `TrySelectNthNearest` directly (already covered by the `CombatTargetAcquisition.cs`
  fix) and contain no direct `.Value ==`/`.Value !=` faction comparisons of
  their own (confirmed by grep). `ProjectileLaunchAimMode.NearestHostile` enum
  member left unrenamed per hard boundary; no adjacent comment described it as
  "hostile"/"different faction" in prose, so no comment update was needed there.
- Validation performed (static/grep only — agent did not run Unity or tests):
  - Grepped the 5 touched production files for `\.Value ==|\.Value !=` — zero
    matches remain.
  - Grepped the whole project for `TryNearestHostile` — zero remaining
    references (only the plan/packet markdown files, which describe the
    rename, still mention the old name as history/spec text).
  - Grepped `TargetedResolveSystem.cs` and `ProjectileSpawnExpansionSystem.cs`
    for `\.Value ==|\.Value !=` — zero matches, confirming no code change
    needed in either file.
  - Confirmed the `CanHit` `switch` covers both `TargetFactionFilterMode`
    values (`HostileOnly`, `AllowedFactionOnly`) plus a `default: return
    false;` defensive case.
  - Did not run PlayMode/EditMode tests or the Unity compiler (per rulebook
    and packet instructions — agent cannot run these).

### 003-hit-count-and-tick-delta.md
- Files changed:
  - `Assets/Scripts/System/Application/CombatApplyResults.cs` — added
    `public float TickDeltaSeconds;` to `CombatTickResult`; added doc comments
    documenting `HitCount` as accepted-`CombatHitEvent`-count-including-
    non-damaging/status-only-hits, `DamageTaken`/`CritCount` as direct-damage-
    only aggregates, and `TickDeltaSeconds` as the finalizer's
    `SystemAPI.Time.DeltaTime` snapshot for the producing update.
  - `Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs` —
    added `public float DeltaTime;` to `FinalizeCombatSingleJob`; wired
    `DeltaTime = SystemAPI.Time.DeltaTime` into the job-construction site in
    `OnUpdate` (alongside `Now`/`FrameCount`); moved `acc.HitCount++;` out of
    the `if (payload.DirectDamageEnabled)` block so it runs once per dequeued
    hit reaching that point in the loop (immediately before the existing
    `acc.HitIndex++;`), while `acc.DamageTaken +=` and the crit-count
    increment/crit-roll math stayed inside that branch unchanged; added
    `TickDeltaSeconds = DeltaTime` to the `CombatTickResult` object initializer
    in the post-loop `for` over `accums`.
- Deviations: none.
- Validation performed (static/code-reading only — agent did not run Unity or
  tests):
  - Re-read the full edited `Execute()` method: confirmed `acc.HitCount++` now
    sits outside the `DirectDamageEnabled` block (runs unconditionally per
    dequeued hit), while `acc.DamageTaken +=`, the crit-roll/random/seed logic,
    and `if (isCrit) { acc.CritCount++; }` remain strictly inside it; confirmed
    the `Health` snapshot block is unchanged and still runs unconditionally.
  - Confirmed `TickDeltaSeconds = DeltaTime` is set on every `CombatTickResult`
    built in the post-loop `for` loop (the only place results are constructed
    in this job), so every result from one `Execute()` call shares the same
    finalizer-update delta.
  - Grepped the whole project for `new CombatTickResult|CombatTickResult\s*\{`
    — the only struct-construction site is inside `FinalizeCombatSingleJob.
    Execute()` (already updated); the two other hits
    (`Assets/Tests/PlayMode/AoeSimulationTests.cs:1803` and
    `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs:795`) are
    `new CombatTickResult[results.Length]` array allocations, not struct
    constructions, so they need no field wiring.
  - Did not modify `CombatApplyBridge.cs` or `ICombatTarget.cs` per the hard
    boundary; did not run PlayMode/EditMode tests or the Unity compiler (agent
    cannot run these).

### 004-regression-tests.md
- Files created:
  - `Assets/Tests/EditMode/TargetFactionEligibilityEditModeTests.cs` — new
    pure-unit truth-table file for `TargetFaction.CanHit` (no ECS `World`
    needed): hostile-mode same/different faction, `AllowedFactionOnly`
    accepting the selected faction (including same-as-own-faction and
    different-from-own-faction cases) and rejecting a non-selected faction,
    `CombatFaction.None` attacker always rejected under both modes, and an
    out-of-range `TargetFactionFilterMode` value rejected.
  - `Assets/Tests/EditMode/TargetFactionEligibilityEditModeTests.cs.meta` —
    hand-authored (guid `7422f198de2049fcab37c2874f8efe53`); other test files
    in this directory carry committed `.meta` companions, so one was added
    for the new file. Minor cosmetic deviation: written with LF line endings
    and a trailing newline, whereas existing committed test `.meta` files use
    CRLF with no trailing newline; functionally identical (`fileFormatVersion`
    + `guid` only) and Unity will not alter the guid on next import.
- Files changed:
  - `Assets/Tests/EditMode/CombatHitDamageScaleEditModeTests.cs` — added 4 new
    `[Test]` methods extending the existing `FinalizeDirectHits`-driven
    finalizer pattern: `StatusOnlyHit_IncrementsHitCountButLeavesDamageAndCritUntouched`,
    `MixedDirectAndStatusEvents_AggregateIntoOneResultWithTotalHitCountAndDirectDamageOnlyTotals`,
    `FinalizerUpdate_StampsTickDeltaSecondsFromWorldTime` (uses
    `testWorld.SetTime(new TimeData(...))`, the project's established pattern
    for driving `SystemAPI.Time` in EditMode `World` fixtures), and
    `BridgeCallback_ObservesFinalizerHitCountAndTickDeltaSecondsExactlyOnce`.
    The bridge-callback test **was implemented as a full, non-fallback test**
    (not the packet's documented fallback): it creates a target entity with
    `Health` + `TargetCompanion` pointing at a new test-local
    `CapturingCombatTarget : ICombatTarget` that overrides the interface's
    default `ReceiveCombatTick` to record call count and the last
    `CombatTickResult`, enqueues 2 raw hits, runs
    `finalizeSystem.Update()`, then directly calls
    `testWorld.GetOrCreateSystemManaged<CombatApplyBridge>().Update()`
    (no `PresentationSystemGroup` membership needed — `SystemBase.Update()`
    can be invoked directly, the same way this file's `finalizeSystem` is
    already driven) and asserts the listener was called exactly once with the
    finalizer's `HitCount`/`TickDeltaSeconds`. Added 3 new private helpers
    (`EnabledStackEffect`, `CreateStackableTarget`, `FinalizeHits` — a sibling
    to `FinalizeDirectHits` for the multi-distinct-payload cases that don't
    fit its one-payload/many-damage-scales signature) and the
    `CapturingCombatTarget` test-local class. Added `elapsedTime` field and
    `using System.Collections.Generic; PlayGround.System.Combat.Collision;
    PlayGround.System.Combat.Status; Unity.Core; UnityEngine;`.
  - `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs` — added
    `AddTarget(float2, float, TargetFaction)` overload and 2 new `[Test]`
    methods (`SelectedFactionSameFactionTargetIsHit`,
    `SelectedFactionUnselectedAttackerIsNotHit`) covering the discrete
    collision path.
  - `Assets/Tests/PlayMode/ProjectileContinuousSimulationTests.cs` — split
    `AddTarget(float2, float)` into a thin wrapper over a new
    `AddTarget(float2, float, TargetFaction)` overload (behaviorally identical
    default: `TargetFaction.Hostile(CombatFaction.Mob)`), plus 2 new `[Test]`
    methods for the continuous collision path.
  - `Assets/Tests/PlayMode/AoeSimulationTests.cs` — added 3 new `[Test]`
    methods mirroring the existing `SameFactionTargetIsNotHit` inline
    `TestCombatTarget`-construction style: one impact-AOE same-faction-hit
    case, one impact-AOE unselected-faction-rejection case, and one
    lingering-AOE case (mirrors `LingeringHitsImmediatelyThenRepeatsAfterCooldown`'s
    exact tick sequence/assertion) proving the repeat-hit tick gate still
    applies under `AllowedFactionOnly` policy.
  - `Assets/Tests/EditMode/TargetedResolveEditModeTests.cs` — added
    `AddCircleTarget(float2, float, TargetFaction)` overload and 2 new
    `[Test]` methods mirroring `LinkZero_SkipsNearerSameFactionProxy` and
    `ChainHop_SkipsNearerSameFactionProxy` but under selected-faction policy,
    covering both targeted-root (link 0) and targeted-chain (hop) selection.
  - `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs` — added
    `CreateTargetProxy(float2, float, TargetFaction)` overload (instance +
    static, mirroring the existing `CombatFaction` overload pair) and 2 new
    `[Test]` methods covering launch-aim: a same-faction-eligible-target case
    and a nearer-ineligible-vs-farther-eligible case.
  - `Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs` — split
    `AddTarget(float2, float, int, CombatFaction)` into a thin wrapper over a
    new `AddTarget(float2, float, int, TargetFaction)` overload (behaviorally
    identical default), plus 2 new `[Test]` methods:
    `SelectedFactionEligibleTargetIsAcquiredOverCloserIneligibleTarget`
    (acquisition path) and
    `RefreshRejectsAlreadyTrackedPolicyIneligibleTargetAndReacquiresEligibleTarget`
    (refresh path — seeds `trackedTargetId`/`trackedTargetIndex` to an already
    -ineligible target so both `TryRefreshTrackedTarget` `CanHit` branches
    reject it, falling through to reacquisition of the eligible target).
- Deviations:
  - Bridge-callback coverage (task 004 item 4, last bullet) used the full,
    non-fallback approach — the packet explicitly allowed falling back to a
    direct `CombatApplyResultSingleton` assertion plus code-reading if a full
    bridge test proved infeasible, but it proved feasible in this fixture
    (`CombatApplyBridge` needs only a `TargetCompanion`-bearing proxy entity
    and a direct `.Update()` call, no `PresentationSystemGroup` wiring), so
    the stronger test was written instead of the documented fallback.
  - New `.meta` file line-ending/trailing-newline cosmetic mismatch vs.
    existing committed test `.meta` files (see above); does not affect the
    guid or Unity's ability to import the file.
  - No other deviations from the execution packet or task file.
- Validation performed (static/grep only — agent did not run Unity, the
  compiler, or any test runner; this is an explicit project and task rule):
  - Re-read every verified production API shape (`TargetFaction` fields,
    `Hostile`/`AllowedFrom`/`CanHit`, `CombatTargetProxy.Create` overloads,
    `CombatTickResult` fields, `CombatApplyFinalizeSingleSystem`,
    `CombatApplyBridge.ReplayCombat`, `ICombatTarget`, `CombatHitEvent`,
    `CombatHitPayload`, `StackEffectSnapshot.Enabled`) directly from the
    current source files before writing any test, confirming every field/
    method name and signature used below matches exactly.
  - Grepped all 7 touched/created files for every
    `TargetFaction.AllowedFrom(`/`TargetFaction.Hostile(`/`TargetFaction.CanHit(`/
    `CombatTargetProxy.Create(` call site and manually checked each against
    the verified signatures — all matched (see tool output retained in this
    session; no mismatched arities or types found).
  - Counted `{`/`}` in all 7 touched/created `.cs` files — balanced in every
    file (no truncated edits).
  - Confirmed every new file's namespace usings cover every type referenced
    (`CombatFaction`, `TargetFaction`, `TargetFactionFilterMode`,
    `CombatShapeType`, `StackEffectSnapshot`, `StackDetonationKind`,
    `TimeData`, `Vector2`, `IReadOnlyList<>`) by cross-checking each file's
    existing `using` block against what was already available vs. what had
    to be added.
  - Manually traced `ProjectileTrackingSystem.TryRefreshTrackedTarget`/
    `TryAcquireTrackedTarget`/`TrySelectRandomTargetInCell` and
    `CombatApplyFinalizeSingleSystem.FinalizeCombatSingleJob.Execute()`
    against each new test's expected control flow (acquisition cone
    forward-then-backward search order, refresh cached-index/mapped-index
    `CanHit` gates, `HitCount++` unconditional vs. `DamageTaken`/`CritCount`
    gated on `DirectDamageEnabled`) to confirm the assertions match actual
    production behavior, since no compiler/test run was possible.
  - Did not run PlayMode/EditMode tests or the Unity compiler/Editor (agent
    is not permitted to; this is an explicit hard boundary in the task,
    packet, and project rules).

### 005-document-contracts.md
- Files changed (documentation only, no `.cs` touched):
  - `Docs/contracts/target-proxy.md` — Fields/Shape `TargetFaction` bullet
    rewritten to describe `Value`/`FilterMode`/`AllowedAttackerFaction` and the
    shared `CanHit` rule (replacing the unconditional same-faction-inequality
    claim); Notes/TODOs paragraph rewritten to describe the two-mode policy,
    the shared `CanHit` predicate (still per-candidate, not per-faction hash
    buckets — preserved), creation-time-immutable-in-this-scope phrasing, and
    "selects a faction, not an individual source actor."
  - `Docs/contracts/combat-hit-and-tick-results.md` — Fields/Shape bullet
    replaced with explicit per-field semantics for `HitCount` (accepted-hit
    count including non-damaging/status-only hits), `DamageTaken`/`CritCount`
    (direct-damage-only aggregates), `TickDeltaSeconds`, `Health`,
    `StatusStart`/`StatusCount`; Guarantees section gained a paragraph on
    callback-only-when-`HitCount > 0 || StatusCount > 0` and the
    now-status-only-hits-also-reach-callback behavior change, with the
    existing "compact results, not one callback per raw hit" sentence kept
    intact.
  - `Docs/flows/collision-to-combat-result.md` — sequence step 2 "faction"
    gate reworded to "target-policy eligibility."
  - `Docs/layers/ecs-simulation.md` — "Owns" bullet for `CombatTickResult`
    finalization gained a one-clause note on `TickDeltaSeconds`/accepted-hit-
    inclusive `HitCount`.
  - `Docs/layers/presentation-and-feedback.md` — "Inputs" bullet for
    `CombatTickResult` gained a one-clause note on `TickDeltaSeconds`.
  - `Docs/reference/simulation/projectile-system.md` — "Tracking And
    Movement" paragraph reworded from "sees only targets registered to the
    player-faction combat root" to describe the per-candidate
    `TargetFaction.CanHit` evaluation; "Collision And Consequences" bullet
    reworded from "skip cells whose targets match the projectile's own
    `TargetFaction`" to "reject any candidate that fails `TargetFaction.CanHit`
    against the projectile's own faction"; "Launch Aim" section prose reworded
    ("nearest hostile" -> "nearest eligible target under target policy",
    "nearest-hostile selection" -> "nearest-eligible-target selection", "no
    hostile in range" -> "no eligible target in range") while leaving
    `LaunchAimMode == NearestHostile` as a literal code identifier untouched.
  - `Docs/reference/simulation/targeted-system.md` — "Spawn And Resolve"
    section: "acquires the nearest hostile proxy" -> "acquires the nearest
    policy-eligible proxy"; "searches hostile target proxies only" ->
    "searches policy-eligible target proxies only."
  - `Docs/contracts/spawn-events-and-commands.md` — launch-aim paragraph:
    "Expansion resolves nearest-hostile acquisition" -> "Expansion resolves
    nearest-eligible-target acquisition (under target policy)"; also fixed an
    additional occurrence found by the required grep pass (not one of the
    packet's 8 numbered passages, but the same stale invariant, in an
    in-scope file) in the "Sibling"/targeted-events paragraph: "the gate
    replaces it with the nearest hostile target position" -> "...nearest
    policy-eligible target position."
- Files read but left unchanged (per packet step 3, since no explicit
  same-faction-only or old-`HitCount`-meaning claim was found):
  - `Docs/flows/runtime-frame.md` — ordering/sequence doc; contains no
    eligibility wording at all.
  - `Docs/reference/simulation/project-ecs-implementation.md` — contains
    pre-existing unrelated legacy naming (`CombatApplyFinalizeSystem`,
    `DamageFinalizeSystem`) left alone per Scope Discipline; its `HitCount ==
    N` sentence states a count identity that holds under both old and new
    semantics (does not assert direct-damage-only), so it is not stale.
  - `Docs/reference/simulation/spawn-template-registry.md` — no
    same-faction/hostile-selection wording found.
- Deviations: none from the packet's required edits. One additional
  in-scope-file fix beyond the packet's 8 numbered passages (the
  `spawn-events-and-commands.md` "nearest hostile target position" sentence),
  made under the packet's own step-4 instruction to grep for and fix missed
  occurrences of the same invariant.
- Out-of-scope finding (not fixed, not in the 11 allowed files): grep turned
  up `Docs/reference/game-logic/skill-gameplay-system.md:138` — `` `ProjectileLaunchAimMode` (`None` or `NearestHostile`) `` — this
  describes the literal enum member name, not an eligibility-invariant claim,
  and the file is outside the packet's "Files Allowed To Modify" list, so it
  was left untouched and is reported here rather than edited.
- Validation performed (static/text-review only, per packet — no build/test
  tooling available to this task):
  - Grepped `Docs/` (case-insensitive) for `same faction|same-faction|
    different faction|hostile` before and after edits. Remaining matches
    after edits: `Docs/visual/style-reference.md` (unrelated art-style
    "Hostile forms" text), `Docs/reference/simulation/projectile-system.md`
    (the literal `LaunchAimMode == NearestHostile` identifier, and the newly
    added correct phrase "hostile-default target still behaves as before"),
    `Docs/reference/game-logic/skill-gameplay-system.md` (out-of-scope file,
    literal enum-name mention), and `Docs/contracts/target-proxy.md` (matches
    only because `HostileOnly` contains the substring "hostile" — the literal,
    correct enum member name). No remaining match asserts the old
    unconditional same-faction/hostile-only invariant in an in-scope file.
  - Grepped `Docs/` for `NearestHostile` specifically: the two remaining
    hits are both literal code-identifier uses (`LaunchAimMode ==
    NearestHostile` in projectile-system.md, and the enum-value mention in
    the out-of-scope skill-gameplay-system.md), confirming no literal
    identifier was altered in prose.
  - Re-read every edited file's surrounding markdown link syntax
    (`[text](path)`) — no edit touched link brackets/paths, only prose
    between them.
  - Cross-checked every new claim (`TargetFaction.Value`/`FilterMode`/
    `AllowedAttackerFaction`, `CanHit` branch behavior, `CombatTickResult`
    field semantics) against the currently-read source in
    `Assets/Scripts/System/Targets/CombatTargetProxy.cs` and
    `Assets/Scripts/System/Application/CombatApplyResults.cs`.

## Post-Completion Fix
- User-reported compiler errors (CS8156 "An expression cannot be used in this
  context because it may not be passed or returned by reference") at the 7
  `TargetFaction.CanHit(faction, in TargetFactions[index])`-style call sites
  added by task 002: `NativeArray<T>`'s indexer returns by value (a
  get/set property, not a ref-returning one), so its result cannot be passed
  directly to an `in` parameter — the compiler cannot take its address inline.
  Fixed in `AoeCollisionCore.cs`, `CombatTargetAcquisition.cs`,
  `ProjectileDiscreteCollisionSystem.cs`,
  `ProjectileContinuousCollisionSystem.cs`, and `ProjectileTrackingSystem.cs`
  (3 sites) by copying the indexed `TargetFaction` to a local variable first,
  then passing `in` that local. `ProjectileTrackingSystem.cs`'s 3 sites were
  additionally factored through one new private `CanHitCandidate(CombatFaction,
  int)` helper on the job struct to avoid repeating the copy-then-call pattern
  three times. `TargetFaction.CanHit`'s public signature and the eligibility
  logic itself are unchanged; this was a call-site-only fix. The new
  EditMode truth-table test file was unaffected (it already called `CanHit`
  against local variables, not array indexers).

## Blockers
- None.

## Validation Summary
- Agent did not run tests (forbidden by task 004 and the project rule). User
  must run the following and review the XML before treating any of this as
  passing:
  - **EditMode** — run `PlayGround.Tests.EditMode.TargetFactionEligibilityEditModeTests`
    (new file, all methods), `PlayGround.Tests.EditMode.TargetedResolveEditModeTests`
    (all methods, including new `LinkZero_SkipsNearerPolicyIneligibleProxy` /
    `ChainHop_SkipsNearerPolicyIneligibleProxy`), and
    `PlayGround.Tests.EditMode.CombatHitDamageScaleEditModeTests` (all
    methods, including the 4 new ones). Export XML to
    `Logs/TestResults-EditMode-EcsHitEligibility.xml`.
  - **PlayMode** — run `ProjectileCollisionSimulationTests`,
    `ProjectileContinuousSimulationTests`, `AoeSimulationTests` (at least the
    new `SelectedFaction*` methods plus existing `SameFactionTargetIsNotHit`),
    `ProjectileTrackingSimulationTests` (at least the new
    `SelectedFactionEligibleTargetIsAcquiredOverCloserIneligibleTarget` /
    `RefreshRejectsAlreadyTrackedPolicyIneligibleTargetAndReacquiresEligibleTarget`
    plus existing `SameFactionTargetIsNotAcquired`), and
    `ProjectileSpawnPipelineTests` (at least the new
    `SelectedFactionSameFactionTargetIsAimedAt` /
    `SelectedFactionNearerIneligibleTargetIsSkippedInFavorOfEligibleTarget`
    plus existing `SameFactionNearerTargetIsSkippedInFavorOfHostile`). Export
    XML to `Logs/TestResults-PlayMode-EcsHitEligibility.xml`.
  - Review both XML files before treating any new or existing test in this
    task's scope as passing.
