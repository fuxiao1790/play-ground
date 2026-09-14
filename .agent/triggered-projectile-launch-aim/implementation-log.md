# Implementation Log

## Status
Complete

## Superseded Note

User revised the behavior after `001`-`006` completed: successful acquisition
now reorients the whole radial nova from the acquired direction
(`direction(i) = Rotate(aimDirection, 360 * i / count)`), not just shot index
`0`. Tasks `004`-`006` below describe and were completed against the
now-obsolete shot-0-replacement behavior; `007`-`009` are the follow-up tasks
that implement, test, and document the corrected behavior. `001`-`003` and
`005`'s EditMode compiler-side coverage remain valid (they concern
authoring/compile/template plumbing, unaffected by the nova-shape change).

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-refactor-combat-target-acquisition.md | Complete | Renamed/moved TargetedAcquisition -> CombatTargetAcquisition; all callers updated |
| 002-author-trigger-launch-aim.md | Complete | Added `ProjectileLaunchAimMode` enum + `TriggerLink` authoring fields/accessor |
| 003-compile-and-template-launch-aim.md | Complete | Stamped launch-aim mode/range from `TriggerLink` onto compiled `RuntimeProjectileDefinition` at the three trigger call sites; threaded through `ProjectileSpawnCommand` template |
| 004-expand-aimed-projectile-waves.md | Superseded | Implemented shot-index-0-only replacement; behavior superseded by `007` (aim-oriented full nova) |
| 005-launch-aim-tests.md | Superseded | Compiler/hash coverage (001-003 plumbing) remains valid; PlayMode shot-0/RNG-preservation tests are obsolete and replaced by `008` |
| 006-update-launch-aim-docs.md | Superseded | Docs described shot-0-only replacement; corrected by `009` |
| 007-aim-oriented-nova.md | Complete | Replaced shot-index-0-only velocity override with a full aim-oriented radial nova computed from the acquired direction; stored pattern now runs only on the fallback path |
| 008-aim-oriented-nova-tests.md | Complete | Replaced the two obsolete shot-0/RNG-preservation tests with two aim-oriented-nova shape tests |
| 009-update-aim-oriented-nova-docs.md | Complete | Corrected the three docs task 006 wrote against the now-superseded shot-0-only behavior to describe the aim-oriented full radial nova (task 007); verified the other two task-006-touched docs already had no stale claim and left them unedited |

## Completed Tasks

### 001-refactor-combat-target-acquisition.md
- Moved `Assets/Scripts/System/Targeted/TargetedAcquisition.cs` to
  `Assets/Scripts/System/Combat/CombatTargetAcquisition.cs`, renaming the type
  `TargetedAcquisition` -> `CombatTargetAcquisition` and its namespace
  `PlayGround.System.Combat.Targeted` -> `PlayGround.System.Combat` (domain-neutral
  root, sibling to `.Targeted`/`.Projectiles`/etc.). No members, signatures, or logic
  changed — pure rename/move.
- Files changed:
  - Created `Assets/Scripts/System/Combat/CombatTargetAcquisition.cs`
  - Deleted `Assets/Scripts/System/Targeted/TargetedAcquisition.cs` (old `.meta` left
    untouched per hard boundary on hand-editing Unity meta files; user to resolve in
    editor)
  - `Assets/Scripts/System/Targeted/TargetedResolveSystem.cs` — updated references
    to `CombatTargetAcquisition`
  - `Assets/Scripts/System/Spawning/ExternalSpawnGateSystem.cs` — updated references
    to `CombatTargetAcquisition`
  - `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs` —
    updated single `TargetKey` call site (only use of the old type in this file);
    removed now-unused `PlayGround.System.Combat.Targeted` using
  - `Assets/Tests/EditMode/TargetedResolveEditModeTests.cs` — updated references to
    `CombatTargetAcquisition`
- `Assets/Tests/EditMode/TargetedRoutingEditModeTests.cs` and
  `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs` were checked via grep
  and do not reference the old type directly — no changes needed.

### 002-author-trigger-launch-aim.md
- Added new shared enum `Assets/Scripts/System/Projectiles/ProjectileLaunchAimMode.cs`
  in namespace `PlayGround.System.Combat.Projectiles` (matching the
  `ProjectileTrackingConfig` precedent): `public enum ProjectileLaunchAimMode : byte
  { None = 0, NearestHostile = 1 }`.
- `Assets/Scripts/Skills/Trigger/TriggerLink.cs`:
  - Added `using PlayGround.System.Combat.Projectiles;`.
  - Added `[SerializeField] private ProjectileLaunchAimMode projectileLaunchAimMode;`
    with public read accessor `ProjectileLaunchAimMode => projectileLaunchAimMode`
    (defaults to enum zero `None`, preserving existing-asset behavior).
  - Added `[SerializeField, Min(0f)] private float projectileLaunchAimRange;` with
    public resolve method `ResolveProjectileLaunchAimRange() => Mathf.Max(0f,
    projectileLaunchAimRange)` (mirrors `ResolveManaCostFactor()` pattern; `[Min(0f)]`
    plus the defensive `Mathf.Max` guarantees a negative serialized value (e.g. from
    an old asset or external edit) still resolves to zero at read time).
  - Both new fields grouped under a new `[Header("Projectile Launch Aim")]` and each
    carry a `[Tooltip(...)]` stating the policy affects projectile effects spawned
    through this trigger only; root/player cast aim is unaffected.
  - No other members of `TriggerLink` touched; `TriggerChain`, `LoadoutSlot`,
    `SkillSetSlot`, `TriggerLinkSlot` untouched.
- Files changed:
  - Created `Assets/Scripts/System/Projectiles/ProjectileLaunchAimMode.cs`
  - Modified `Assets/Scripts/Skills/Trigger/TriggerLink.cs`
- Not touched (per hard boundaries): `SkillDefinition.cs`/`ProjectileDefinition`,
  `ProjectileTrackingConfig.cs`, `SkillSetCompiler.cs`,
  `RuntimeProjectileDefinition`, `ProjectileSpawnCommand`/template code, and all four
  `TriggerLink` subclasses (`IntervalSpawnTrigger.cs`, `OnHitTrigger.cs`,
  `StackTrigger.cs`, `OnExpireTrigger.cs`) — read-only, confirmed no conflicting
  member names, no edits made.

### 003-compile-and-template-launch-aim.md
- `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`: added
  `public ProjectileLaunchAimMode ProjectileLaunchAimMode { get; set; }` and
  `public float ProjectileLaunchAimRange { get; set; }` next to `Tracking`
  (both default to enum-zero/`0f`; `using PlayGround.System.Combat.Projectiles;`
  was already present).
- `Assets/Scripts/Skills/SkillSetCompiler.cs`:
  - Added private static helper `ApplyIncomingTriggerLaunchAim(RuntimeSkillDefinition,
    TriggerLink)` mirroring `ApplyIncomingTriggerManaCostMultiplier`'s pattern: no-ops
    unless the target is a `RuntimeProjectileDefinition` and the link is non-null,
    then stamps `link.ProjectileLaunchAimMode` / `link.ResolveProjectileLaunchAimRange()`.
  - Called at exactly three sites, alongside (never replacing) the existing
    `ApplyIncomingTriggerManaCostMultiplier` calls:
    - `ApplyIntervalSpawn`'s `case RuntimeProjectileDefinition childDef:` branch —
      `ApplyIncomingTriggerLaunchAim(childDef, trigger)`.
    - `CompileInternal`'s `else if (link is OnHitTrigger)` branch, inside
      `if (AttachOnHitTarget(runtime, compiledTarget))` — `ApplyIncomingTriggerLaunchAim(compiledTarget, link)`.
    - `CompileInternal`'s `else if (link is StackTrigger stackTrigger)` branch, inside
      `if (compiledTarget != null)` — `ApplyIncomingTriggerLaunchAim(compiledTarget, link)`
      (stamps `compiledTarget`, the actual `RuntimeProjectileDefinition`, not the
      `RuntimeStackingDetonation` wrapper).
  - Root/top-level compiled definition is never passed through these three sites, so
    it naturally keeps the enum-zero/`0f` default — no explicit "is root" branch
    needed or added.
- `Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs`: added
  `public ProjectileLaunchAimMode LaunchAimMode;` and `public float LaunchAimRange;`
  to `ProjectileSpawnCommand` (next to `SpawnPatternType`; file already lives in
  namespace `PlayGround.System.Combat.Projectiles`, same as the enum, so no new
  `using` was needed). `ProjectileSpawnEvent` was not touched.
- `Assets/Scripts/Skills/SkillDriver.cs`: in
  `SkillIntervalTemplateBuilder.BuildProjectileTemplate`, added
  `LaunchAimMode = child.ProjectileLaunchAimMode,` and
  `LaunchAimRange = child.ProjectileLaunchAimRange,` to the returned
  `ProjectileSpawnCommand` initializer. No other method in this file was touched.
- Files changed:
  - `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`
  - `Assets/Scripts/Skills/SkillSetCompiler.cs`
  - `Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs`
  - `Assets/Scripts/Skills/SkillDriver.cs`
- Not touched (per hard boundaries): `CombatRoot.cs` (`SpawnTemplateFor`,
  `ProjectileCommandFor` — read-only, confirmed unmodified and thus new fields stay
  at enum-zero/`0f` default on the root/manual-cast path), `ProjectileSpawnEvent`,
  `AoeSpawnCommand`/`TargetedSpawnCommand` and their builders, any
  expansion/apply/ECS system.

### 004-expand-aimed-projectile-waves.md
- `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs`:
  - Added `using PlayGround.System.Combat;` and
    `using PlayGround.System.Combat.Collision.Broadphase;`.
  - Added `[UpdateAfter(typeof(TargetSpatialHashSystem))]` alongside the class's
    existing `[UpdateAfter]`/`[UpdateBefore]` attributes.
  - In `OnUpdate`, resolved `TargetSpatialHashSingleton` via
    `SystemAPI.TryGetSingleton`, built a `CombatTargetAcquisition.Snapshot` from its
    target arrays/`AoeOccupiedCells` when present, combined the job's schedule
    dependency with `targetHash.BuildHandle` (mirroring
    `TargetedResolveSystem`'s `state.Dependency`/`hash.BuildHandle` pattern, adapted
    to `SystemBase`'s `Dependency` property), passed `HasTargetHash`/`TargetSnapshot`
    into the job, and — after scheduling and disposing `events` — published the
    job's handle into `targetHashRw.ValueRW.ConsumerHandle` via
    `JobHandle.CombineDependencies`. The existing
    `singleton.DiscreteCommands`/`ContinuousCommands`/`PendingHandle` assignments
    were left exactly as-is, after this new block.
  - Added `public bool HasTargetHash;` and
    `public CombatTargetAcquisition.Snapshot TargetSnapshot;` fields to
    `ProjectileExpansionJob`.
  - Added `TryResolveAimedVelocity(in ProjectileSpawnCommand, out float2)`: returns
    `false` (velocity left `default`) unless `HasTargetHash` is true,
    `command.LaunchAimMode == ProjectileLaunchAimMode.NearestHostile`,
    `command.LaunchAimRange > 0f`, and `command.Faction != CombatFaction.None`; then
    calls `CombatTargetAcquisition.TrySelectNthNearest` (rank 0, exclude
    `command.SeedContactGateTargetId`); on a hit, guards against a near-zero
    direction (`lengthsq <= 0.0001f`) before returning
    `math.normalize(targetPosition - command.Position) * command.Speed`.
  - `Execute()` now calls `TryResolveAimedVelocity` once per event, immediately
    after `Stamp` and before `count`/the root-cast branch/the pattern `switch`, and
    threads `(hasAim, aimedVelocity)` into all four `CreateForwardPattern`/
    `CreateSideSprayPattern`/`CreateRadialPattern` call sites.
  - `CreateSideSprayPattern`, `CreateRadialPattern`, `CreateForwardPattern`, and
    `WriteForwardWave` each gained trailing `bool hasAim, float2 aimedVelocity`
    parameters. Side-spray and forward-wave compute their normal velocity
    (consuming RNG where applicable) unconditionally, then overwrite only when
    `i == 0 && hasAim`; radial (RNG-free) uses a ternary at `i == 0`, per the
    packet's explicitly allowed exception. `Stamp`, `WriteCommand`, `SpreadAngle`,
    `IntervalWaveSeed`, `IntervalSideSprayVelocity`, `RadialDirection`,
    `ProjectileIdFor`, and `Rotate` were not touched.
- Files changed:
  - `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs`

### 005-launch-aim-tests.md
- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`:
  - Discovered that the file's existing `SetField(object target, string fieldName,
    object value)` reflection helper (`target.GetType().GetField(fieldName,
    BindingFlags.Instance | BindingFlags.NonPublic)`) cannot find
    `projectileLaunchAimMode`/`projectileLaunchAimRange` when `target` is an
    `IntervalSpawnTrigger`/`OnHitTrigger`/`StackTrigger` instance, because those
    fields are `private` and declared on the abstract `TriggerLink` base class —
    verified with a standalone reflection repro (`derived.GetType().GetField(...)`
    returns `null` for a base-private field; `typeof(Base).GetField(...)` finds it).
    Added a second, narrowly-scoped helper `SetTriggerLinkField(TriggerLink target,
    string fieldName, object value)` that queries `typeof(TriggerLink)` directly,
    instead of widening the shared `SetField` helper used by ~20 unrelated tests in
    this file. See Deviations.
  - Added 5 new `[Test]` methods:
    - `CompilerCopiesLaunchAimPolicyToIntervalProjectileChildOnly` — interval trigger
      copies mode/range onto the compiled child only; root asserted `None`.
    - `CompilerCopiesLaunchAimPolicyToOnHitProjectileTargetOnly` — on-hit trigger
      copies policy onto `ImpactProjectileDefinition` only; root asserted `None`.
    - `CompilerCopiesLaunchAimPolicyToStackProjectileDetonationOnly` — stack trigger
      copies policy onto `StackingDetonation.Detonation` (cast to
      `RuntimeProjectileDefinition`) only; applicator/root asserted `None`.
    - `CompilerRetainsContinuousCollisionAlongsideLaunchAimWithoutImpliedTracking` —
      continuous child (`continuousCollision = true`) reached through an
      aim-enabled interval trigger keeps `ContinuousCollision == true` and the
      copied aim policy, with `Tracking.Enabled == false` (independence check).
    - `CompilerLeavesLaunchAimInertForNonProjectileOnHitTarget` — aim-enabled
      `OnHitTrigger` targeting an `AoeSkill` compiles normally to
      `RuntimeAoeDefinition` (root stays `None`; AOE type has no launch-aim fields
      to assert against, so the type check alone proves inertness).
- `Assets/Tests/PlayMode/SpawnCommandUnificationTests.cs`:
  - Added `MakeProjectileTemplate(ProjectileLaunchAimMode, float launchAimRange, int
    typeId = 1)` private helper mirroring `MakeAoeTemplate`, with the minimal valid
    `ProjectileSpawnCommand` field set (`TypeId`, `RenderTypeId`, `Count`,
    `SpawnPatternType`, `Speed`, `Lifetime`, `Radius`, `HalfExtents`, `ShapeType`,
    `HitPayload`) per the execution packet.
  - Added 3 new `[Test]` methods:
    `SpawnTemplateHash_DifferentLaunchAimModeProducesDistinctKey`,
    `SpawnTemplateHash_DifferentLaunchAimRangeProducesDistinctKey`,
    `SpawnTemplateHash_IdenticalLaunchAimContentProducesSameKey` — all call
    `SpawnTemplateHash.Of` directly on two `ProjectileSpawnCommand` structs, no ECS
    world involved, exactly mirroring the existing AOE hash tests.
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs` (the large integration
  file):
  - Added usings: `PlayGround.System.Combat` (for `CombatTargetAcquisition`),
    `PlayGround.System.Combat.Collision.Broadphase` (for `TargetSpatialHashSystem`),
    `PlayGround.System.Combat.Collision.Narrowphase` (for `CombatCollisionMath`).
  - Factored `SetUp`'s world/system/entity bootstrap into a new private
    `ProjectileHarness` struct + `CreateHarness(string worldName)` /
    `DisposeHarness(ref ProjectileHarness)` static helper pair, so the RNG-preservation
    tests (below) can stand up a second, fully independent world with identical
    parameters without duplicating the whole bootstrap inline. `SetUp` now just calls
    `CreateHarness` and copies the fields into the existing instance fields — byte-for-
    byte identical behavior for every pre-existing test.
  - Added `TargetSpatialHashSystem` (an `ISystem`, added via `GetOrCreateSystem`, not
    `GetOrCreateSystemManaged`) to `CreateHarness`'s system list, per the execution
    packet — required so `ProjectileSpawnExpansionSystem`'s
    `[UpdateAfter(TargetSpatialHashSystem)]` constraint (from task 004) is satisfiable
    and the launch-aim acquisition query has a real `TargetSpatialHashSingleton` to
    read.
  - Added `CreateTargetProxy` (instance wrapper + a `static` overload taking
    `EntityManager` explicitly, so the isolated-harness helper below can reuse it)
    creating a raw `TargetProxyTag`/`TargetPosition`/`TargetCollisionShape`/
    `TargetFaction` entity with bounds computed via
    `CombatCollisionMath.ComputeWorldBounds`, per the execution packet's exact
    snippet.
  - Extended `MakeEvent` with four new optional parameters — `launchAimMode =
    ProjectileLaunchAimMode.None`, `launchAimRange = 0f`, `faction =
    CombatFaction.Player`, `contactGateSeedTargetId = 0` — all defaulting to the
    template's/event's prior implicit values, so every existing call site is
    unaffected. Wired `LaunchAimMode`/`LaunchAimRange` onto the built
    `ProjectileSpawnCommand` and `faction`/`contactGateSeedTargetId` onto the
    returned event's `Faction`/`ContactGateSeedTargetId`.
  - Added `RunSingleWaveAndGetVelocitiesByShotIndex` + `FindProjectileById` static
    helpers: build one ad hoc `ProjectileSpawnCommand` template plus one
    `ProjectileSpawnEvent` against a caller-supplied `ProjectileHarness`, tick once,
    and return velocities indexed by the (reused, unmodified) `ExpectedChildId`
    formula — used only by the two RNG-preservation tests below, which need two
    fully independent worlds ticked with identical jitter seeds to compare shots
    1..N-1 for exact equality.
  - Added 10 new `[Test]` methods:
    - `NearestHostileInRangeAimsSingleShotVelocity` — also covers "single-shot wave
      aims directly at target" (identical scenario, one test per the packet's own
      allowance).
    - `SameFactionNearerTargetIsSkippedInFavorOfHostile`
    - `ContactGateSeedTargetIsExcludedAndNextNearestHostileIsSelected`
    - `NoHostileInRangePreservesFallbackPattern`
    - `MissingOrEmptyTargetHashFallsBackToNormalPattern` — see Deviations (covers
      the "missing hash singleton" criterion via the zero-targets path rather than a
      literally-absent singleton component).
    - `DisabledLaunchAimPreservesFallbackPatternWithHostilePresent`
    - `LaunchAimDoesNotAddProjectilesOrAlterDeterministicIds`
    - `ContinuousCommandReceivesLaunchAimWithoutTracking`
    - `RadialWaveReplacesOnlyShotZeroVelocityRelativeToDisabledControl`
    - `SideSprayWaveReplacesOnlyShotZeroVelocityRelativeToDisabledControl` (its
      shot-1 comparison loop also directly covers "RNG sequence for non-aimed
      jitter/side-spray shots is unchanged by replacement")
  - Retained all pre-existing tests unmodified (directionless side-spray, radial
    fan-out, default heading, discrete/continuous pool separation, continuous
    no-tracking, manual forward volley, etc.) — confirmed via diff review, only
    additive changes were made to shared helpers.

### 006-update-launch-aim-docs.md
- Inserted the six exact prose blocks given in
  `runtime/006-execution-packet.md` at their described anchor points, after
  re-reading each target file to confirm the anchor text still matched
  verbatim (none had shifted):
  - `Docs/reference/game-logic/skill-gameplay-system.md`: new `### Projectile
    Launch Aim` subsection under `## Trigger Semantics`, after the
    `StackTrigger` bullet and before the "Root cast spends mana..." paragraph.
  - `Docs/contracts/skill-runtime-snapshots.md`: expanded the "runtime
    projectile definitions" bullet under `## Fields / Shape` with the
    launch-aim parenthetical and cross-link.
  - `Docs/contracts/spawn-events-and-commands.md`: inserted the
    `ProjectileSpawnCommand` launch-aim paragraph immediately after the
    "Commands carry the resolved spawn data..." paragraph.
  - `Docs/reference/simulation/projectile-system.md`: inserted a new `##
    Launch Aim` section between `## Timed Child Spawns` and `## Tracking And
    Movement`, and added one new bullet to `## Authoring Notes` after the
    `SkillDefinition projectile entries...` bullet.
  - `Docs/reference/simulation/spawn-template-registry.md`: added the
    `LaunchAimMode` / `LaunchAimRange` bullet to the `## Projectile Runtime
    Snapshot` list, matching the file's existing mangled-encoding `鈥?`
    separator character used by the immediately preceding bullets (preserved
    verbatim, not "fixed").
  - `Docs/flows/spawn-event-to-entity.md`: changed sequence step 3 to add
    "launch-aim acquisition" to the list of what expansion owns.
- No other content in any of the six files was changed.
- Files changed:
  - `Docs/reference/game-logic/skill-gameplay-system.md`
  - `Docs/contracts/skill-runtime-snapshots.md`
  - `Docs/contracts/spawn-events-and-commands.md`
  - `Docs/reference/simulation/projectile-system.md`
  - `Docs/reference/simulation/spawn-template-registry.md`
  - `Docs/flows/spawn-event-to-entity.md`

### 007-aim-oriented-nova.md
- `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs`:
  - In `ProjectileExpansionJob.Execute()`, replaced the `hasAim`/`aimedVelocity`
    call sequence (task 004) with a single `TryResolveAimedDirection` call,
    immediately after `Stamp`. On success, calls the new `CreateAimedNovaPattern`
    and `continue`s to the next event, skipping the root-cast check and the
    pattern `switch` entirely (acquisition success now overrides all pattern
    selection, including the root forward-volley special case). On failure, falls
    through to the root-cast check and pattern `switch`, calling the
    now-parameterless `CreateForwardPattern`/`CreateSideSprayPattern`/
    `CreateRadialPattern`.
  - Renamed `TryResolveAimedVelocity` to `TryResolveAimedDirection`: same
    preconditions (`HasTargetHash`, `LaunchAimMode == NearestHostile`,
    `LaunchAimRange > 0f`, `Faction != None`, a hostile found via
    `CombatTargetAcquisition.TrySelectNthNearest` rank 0 excluding
    `SeedContactGateTargetId`, non-coincident target), but now returns the
    normalized aim direction (`math.normalize(diff)`) instead of a
    speed-scaled velocity.
  - Added `CreateAimedNovaPattern(in ProjectileSpawnCommand command, int count,
    float2 aimedDirection)`: writes every shot `i` in `[0, count)` with velocity
    `Rotate(aimedDirection, 360f * i / count) * command.Speed`, using the
    existing `Rotate` helper unchanged. No RNG, no spread, no jitter.
  - Restored `CreateSideSprayPattern`, `CreateRadialPattern`,
    `CreateForwardPattern`, and `WriteForwardWave` to their pre-task-004
    parameterless-aim signatures and bodies — removed the trailing
    `bool hasAim, float2 aimedVelocity` parameters and every `if (i == 0 &&
    hasAim)` / ternary override line. These four methods now run only on the
    fallback path (acquisition failed or disabled) and are pattern-pure again.
  - `Stamp`, `WriteCommand`, `SpreadAngle`, `IntervalWaveSeed`,
    `IntervalSideSprayVelocity`, `RadialDirection`, `ProjectileIdFor`, `Rotate`,
    `OnCreate`/`OnDestroy`/`OnUpdate`, the class attributes (including the
    `[UpdateAfter(typeof(TargetSpatialHashSystem))]` added by task 004 and its
    `hasTargetHash`/`targetSnapshot`/`expansionScheduleDependency` wiring in
    `OnUpdate`), and `ProjectileSpawnEventSingleton` were not touched.
- Files changed:
  - `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs`

### 008-aim-oriented-nova-tests.md
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`:
  - Deleted the two obsolete `[Test]` methods
    `RadialWaveReplacesOnlyShotZeroVelocityRelativeToDisabledControl` and
    `SideSprayWaveReplacesOnlyShotZeroVelocityRelativeToDisabledControl`, which
    asserted the now-false claim (superseded by task 007) that non-zero shots
    in an aim-enabled wave must match a disabled-policy control wave exactly.
  - Deleted the two now-dead helpers `RunSingleWaveAndGetVelocitiesByShotIndex`
    and `DisposeHarness` (used only by the two deleted tests). `CreateHarness`
    and `ProjectileHarness` were left untouched — `SetUp` still uses them.
  - Added `VelocitiesByShotIndex(int count, int baseProjectileId, uint
    jitterSeed, int tickIndex)` immediately after `FindProjectileById`,
    resolving each shot's velocity by its deterministic id (index-safe, unlike
    `ActiveProjectileVelocities()`'s query-iteration order).
  - Added two new `[Test]` methods in place of the deleted ones:
    - `AimedNovaBypassesStoredSideSprayPatternWithCount4UpLeftDownRightOrder` —
      target above origin, `count = 4`, authored `SpawnPatternType.SideSpray` +
      nonzero `spreadDegrees`; asserts deterministic shot order up/left/down/right.
    - `AimedNovaBypassesStoredForwardPatternWithCount3EvenAngularSpacing` —
      target at an arbitrary non-axis offset, `count = 3`, authored
      `SpawnPatternType.Forward` + nonzero `spreadDegrees`; asserts shot 0
      matches the normalized target direction and all three shots are spaced
      120 degrees apart.
  - All 8 previously-valid launch-aim tests and every unrelated pre-existing
    test in the file were left byte-for-byte untouched.
- Files changed:
  - `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`

### 009-update-aim-oriented-nova-docs.md
- Re-read `ProjectileSpawnExpansionSystem.cs`'s `CreateAimedNovaPattern`/
  `TryResolveAimedDirection` directly during this task and confirmed the
  formula (`Rotate(aimedDirection, 360f * i / count) * command.Speed`, every
  shot including `i == 0` written by the same unconditional loop, fallback
  path unchanged pattern methods) matches every replacement snippet below —
  no mismatch, no blocker.
- Applied the three exact replacements from `runtime/009-execution-packet.md`,
  after re-reading each anchor's current exact text (none had shifted from
  the packet's assumed text):
  - `Docs/reference/simulation/projectile-system.md`'s `## Launch Aim`
    section — replaced the second and third paragraphs (the ones describing
    "replaces only deterministic shot index 0's velocity" and "the same
    one-shot replacement") with the full aim-oriented-nova prose; left the
    first paragraph and the `## Authoring Notes` bullet untouched as
    instructed (verified on read that the latter mentions no shot count/nova
    shape).
  - `Docs/contracts/spawn-events-and-commands.md` — replaced the middle
    sentence ("...redirects only one deterministic shot's velocity.") of the
    `ProjectileSpawnCommand` launch-aim paragraph with the nova-formula
    sentence; left the paragraph's first and trailing-link sentences
    unchanged.
  - `Docs/reference/game-logic/skill-gameplay-system.md`'s `### Projectile
    Launch Aim` subsection — replaced the second paragraph (independent of
    homing) with the nova-shape version; left the first paragraph untouched.
- Verified the two "verify only" files per the packet's instructions and made
  no edit to either:
  - `Docs/reference/simulation/spawn-template-registry.md` — re-read the
    `LaunchAimMode`/`LaunchAimRange` bullet in `## Projectile Runtime
    Snapshot`; it states template-level policy participating in
    `SpawnTemplateHash`, with no single-shot or RNG-preservation claim.
    Grepped the file for "shot" (case-insensitive): every hit is a
    `Snapshot`/`snapshot` substring, none is the word "shot". No stale claim
    found; left unedited.
  - `Docs/flows/spawn-event-to-entity.md` — re-read sequence step 3
    ("Expansion owns count, spread, jitter, bounds, deterministic id,
    launch-aim acquisition, and command production."); it is generic and
    makes no single-shot claim. Grepped the file for "shot": both hits are
    the `Snapshot` substring in a link filename, not the word "shot". No
    stale claim found; left unedited.
- No other content in any of the five files was changed — no reformatting,
  no unrelated cleanup, no link paths altered.
- Files changed:
  - `Docs/reference/simulation/projectile-system.md`
  - `Docs/contracts/spawn-events-and-commands.md`
  - `Docs/reference/game-logic/skill-gameplay-system.md`

## Blockers
- None.

## Validation Summary

### 001-refactor-combat-target-acquisition.md
- Grepped `Assets/` for `TargetedAcquisition`: zero remaining code references (only
  the orphaned `TargetedAcquisition.cs.meta` filename remains on disk, which is
  Unity metadata, not a code reference — left for the user per the no-hand-edit-meta
  boundary).
- Code review confirms byte-for-byte identical logic: `Snapshot` fields,
  `TryNearestHostile`, `TrySelectNthNearest` (both overloads), `TargetKey`,
  `InsertNearest`, and `Candidate` were copied verbatim, only the type name and
  namespace changed.
- Unity compile check and test execution were not run (no Unity CLI/editor
  invocation available/permitted in this environment; test execution is deferred to
  the user per project policy). Validation here is static/search-based plus manual
  code-equivalence review only.

### 002-author-trigger-launch-aim.md
- Grepped `Assets/` for `ProjectileLaunchAimMode`, `projectileLaunchAimMode`,
  `projectileLaunchAimRange`, `ResolveProjectileLaunchAimRange`: only the two
  intended files match (`ProjectileLaunchAimMode.cs`, `TriggerLink.cs`) — no
  pre-existing conflicting declarations anywhere else in the codebase.
- Confirmed enum has exactly two members with the specified values (`None = 0`,
  `NearestHostile = 1`) and `: byte` backing, matching `CombatFaction`'s style
  precedent.
- Read all four `TriggerLink` subclasses (`IntervalSpawnTrigger`, `OnHitTrigger`,
  `StackTrigger`, `OnExpireTrigger`): none declare a member with the same name as
  the new fields/accessor/resolve method, so they inherit the new members without
  conflict.
- Confirmed `Assets/Scripts/Skills/SkillDefinition.cs` (containing
  `ProjectileDefinition`) was not modified — grep shows no launch-aim references
  there.
- Confirmed `ProjectileTrackingConfig.cs` and `SkillSetCompiler.cs` were not
  modified (untouched by this task's edits).
- Unity compile check and test execution were not run — no Unity CLI/editor
  invocation available/permitted in this environment; test execution is deferred to
  the user per project policy. Validation here is static/search-based (grep, code
  review, manual pattern comparison against `ResolveManaCostFactor()` and
  `CombatFaction`) only.

### 003-compile-and-template-launch-aim.md
- Grepped `Assets/` for `new ProjectileSpawnCommand`: 9 files match, but only
  `SkillDriver.cs` (`BuildProjectileTemplate`) and `CombatRoot.cs`
  (`ProjectileCommandFor`) are production construction sites; the remaining 7 are
  test fixtures (`ProjectileSpawnPipelineTests.cs`, `AoeSimulationTests.cs`,
  `CombatPoolCleanupSystemTests.cs` x2, `ProjectileCollisionSimulationTests.cs`,
  `AoePlayModeTests.cs`, `ProjectileAuthoringEditModeTests.cs`) which implicitly get
  the new fields' default values (`None`/`0f`) and were left untouched per the
  allowed-files list.
- Read `CombatRoot.cs`'s `ProjectileCommandFor` (~line 653) in full: confirmed it
  does not set `LaunchAimMode`/`LaunchAimRange`, so the root/manual
  `ProjectileSpawnRequest` cast path implicitly produces `None`/`0f` — matches
  "root projectile runtime copy remains None" acceptance criterion. Not modified.
- Confirmed `ProjectileSpawnEvent` struct in `ProjectileSpawnPipeline.cs` is
  byte-for-byte unchanged (only `ProjectileSpawnCommand` was edited in this file).
- Code-reviewed all three `SkillSetCompiler.cs` call sites against the execution
  packet's exact expected diff (interval child, on-hit target inside the successful
  `AttachOnHitTarget` branch, stack trigger's `compiledTarget` inside
  `if (compiledTarget != null)`): all three match, `ApplyIncomingTriggerLaunchAim` is
  called with the correct `TriggerLink` instance (`trigger`/`link`) alongside the
  existing mana-cost-multiplier call, never replacing it.
- Confirmed via the helper's own type guard
  (`triggeredDefinition is not RuntimeProjectileDefinition ... return;`) that AOE and
  Targeted compiled targets at the same three call sites are structurally unaffected
  (no-op) — satisfies "trigger links targeting non-projectile effects do not alter
  those definitions."
- `git diff --stat` after edits confirms only the four allowed files changed by this
  task's edits (other pending diffs in the working tree belong to already-complete
  tasks 001/002, not touched again here).
- Unity compile check and test execution were not run — no Unity CLI/editor
  invocation available/permitted in this environment; test execution (including
  `SkillValidationEditModeTests` / `SpawnCommandUnificationTests`) is deferred to the
  user per project policy. Validation here is static/search-based (grep, code
  review, `git diff` review) only.

### 004-expand-aimed-projectile-waves.md
- Grepped the modified file for every call site of `CreateForwardPattern`,
  `CreateSideSprayPattern`, `CreateRadialPattern`, and `WriteForwardWave`: all 4
  call sites (2x forward, 1x side-spray, 1x radial) and the 1 internal
  `WriteForwardWave` call from `CreateForwardPattern` pass `hasAim, aimedVelocity`;
  no stale 2-arg/3-arg call sites remain (confirmed via a second grep specifically
  for the old parameter-count signatures — zero matches).
- Hand-traced side-spray with `count = 3`, aim enabled: shot 0 calls
  `IntervalSideSprayVelocity` (consuming `rng.NextFloat`) before its result is
  discarded and replaced by `aimedVelocity`; shots 1 and 2 then draw from the same
  RNG state as they would have if shot 0's draw had not been overridden — the RNG
  sequence for shots 1..count-1 is unaffected by enabling launch aim. Same
  reasoning applies to `WriteForwardWave`'s jitter draw. Radial has no RNG, so its
  ternary at `i == 0` is equivalent in effect and explicitly allowed by the packet.
- Confirmed (via grep of the modified file for `targetHash.`/`TargetSnapshot`/
  `BuildHandle`/`ConsumerHandle`) that `TargetSpatialHashSingleton`'s
  `TargetEntities`/`TargetPositions`/`TargetShapes`/`TargetFactions`/
  `AoeOccupiedCells`/`BuildHandle` are only ever read (via the local `targetHash`
  copy and the read-only `Snapshot` struct), and only `ConsumerHandle` is written,
  via `targetHashRw.ValueRW.ConsumerHandle = JobHandle.CombineDependencies(...)` —
  matching `TargetedResolveSystem`'s read-only precedent exactly.
- Confirmed dependency wiring: schedule dependency combines `Dependency` with
  `targetHash.BuildHandle` only when `hasTargetHash` is true (else falls back to
  plain `Dependency`, matching the missing-singleton/stripped-test-world fallback
  requirement); consumer-handle publication is likewise gated on `hasTargetHash`.
- Confirmed acquisition preconditions (`HasTargetHash`, `LaunchAimMode ==
  NearestHostile`, `LaunchAimRange > 0f`, `Faction != None`, target found,
  non-coincident target) exactly match the packet and the plan's global invariant
  that missing/failed preconditions fall through to unmodified pattern output —
  `TryResolveAimedVelocity` returns `false` (velocity `default`, unused by any
  caller when `hasAim` is `false`) on every failure branch, never throwing.
- Confirmed no new entity component, archetype, system, or allocation was added;
  the two new job fields (`bool`, a snapshot of already-persistent `NativeArray`/
  `NativeParallelMultiHashMap` handles) are the only additions to
  `ProjectileExpansionJob`'s data, consistent with "allocation-free, one query per
  event/wave."
- `Stamp`, `WriteCommand`, `SpreadAngle`, `IntervalWaveSeed`,
  `IntervalSideSprayVelocity`, `RadialDirection`, `ProjectileIdFor`, and `Rotate`
  confirmed byte-for-byte unchanged (grep diff of each method body against the
  pre-edit file read).
- Unity/Burst compile check was not run — no Unity CLI/editor invocation
  available/permitted in this environment (consistent with tasks 001-003). Unity
  tests were not run per project policy; task 005 (separate, later) adds the actual
  EditMode/PlayMode coverage for this behavior, and the user runs and exports XML
  results per project policy. Validation here is static/search-based (grep,
  manual code review, hand-traced RNG-consumption example) only.

### 005-launch-aim-tests.md
- Confirmed dependency: tasks 001-004 all show `Complete` in this log and their
  production surfaces (`CombatTargetAcquisition`, `ProjectileLaunchAimMode`,
  `TriggerLink.ProjectileLaunchAimMode`/`ResolveProjectileLaunchAimRange()`,
  `RuntimeProjectileDefinition.ProjectileLaunchAimMode`/`ProjectileLaunchAimRange`,
  `SkillSetCompiler.ApplyIncomingTriggerLaunchAim` at its three call sites,
  `ProjectileSpawnCommand.LaunchAimMode`/`LaunchAimRange`,
  `ProjectileSpawnExpansionSystem.TryResolveAimedVelocity` and its
  `TargetSpatialHashSingleton` wiring) were all read directly from source during this
  task and match this log's task 001-004 descriptions exactly — no discrepancy
  between promised and actual behavior was found, so no blocker was raised.
- Verified via a standalone `dotnet run` reflection repro (not part of the shipped
  test suite) that `Type.GetField` called on a derived type's runtime type does not
  surface a private field declared on an ancestor type — confirming the
  `SetTriggerLinkField` addition (see Completed Tasks) is necessary, not
  speculative.
- Grepped each of the three modified files for duplicate `[Test]` method names:
  none found (verified via `grep -oP '(?<=public void )\w+' <file> | sort | uniq -d`
  producing empty output for all three files).
- Verified brace balance (`{` vs `}` counts) in all three modified files after
  editing: `SkillValidationEditModeTests.cs` 105/105,
  `SpawnCommandUnificationTests.cs` 28/28, `ProjectileSpawnPipelineTests.cs`
  114/114.
- Verified each new helper (`CreateHarness`, `DisposeHarness`, `ProjectileHarness`,
  `RunSingleWaveAndGetVelocitiesByShotIndex`, `FindProjectileById`,
  `CreateTargetProxy` overloads, `MakeProjectileTemplate`, `SetTriggerLinkField`) is
  declared exactly once per file.
- Caught and fixed (during self-review, before finishing) a C# argument-ordering bug
  introduced across 9 call sites: `CreateTargetProxy(position, radius: 0.5f,
  CombatFaction.X)` is invalid because a positional argument cannot follow a named
  one — fixed by naming the trailing `faction:` argument at every call site.
- Code-reviewed every new test against its acceptance-criteria bullet (see Completed
  Tasks above for the full mapping) and against the exact preconditions/formulas in
  `ProjectileSpawnExpansionSystem.TryResolveAimedVelocity`/`ProjectileIdFor`/
  `CombatTargetAcquisition.TrySelectNthNearest` read during this task, including the
  faction-exclusion, contact-gate-exclusion, and RNG-preservation semantics.
- Did not modify any file under `Assets/Scripts/` — confirmed via review of every
  edit made in this task; all changes are confined to the three allowed test files
  plus this log.
- Unity EditMode/PlayMode test execution and XML export were NOT run — no Unity
  CLI/editor invocation is available in this environment. Per project policy and the
  execution packet, the user must run:
  - EditMode: `SkillValidationEditModeTests`
  - PlayMode: `SpawnCommandUnificationTests`
  - PlayMode: `ProjectileSpawnPipelineTests`
  - PlayMode regression: `ProjectileContinuousSimulationTests`
  - PlayMode regression: `ProjectileTrackingSimulationTests`
  and export XML under `Logs/` for review. No pass/fail claim is made here.

### 006-update-launch-aim-docs.md
- Confirmed dependency: tasks 001-005 all show `Complete` in this log; read
  `Assets/Scripts/System/Projectiles/ProjectileLaunchAimMode.cs`,
  `Assets/Scripts/Skills/Trigger/TriggerLink.cs`, and
  `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs`
  directly during this task and confirmed every claim in the execution
  packet's prose (trigger-only authoring, root/player-cast exclusion,
  independence from homing, shot-index-0-only redirect, RNG-sequence
  preservation for other shots, contact-gate-seed exclusion, reuse of
  `TargetSpatialHashSingleton`/`CombatTargetAcquisition.TrySelectNthNearest`
  with `BuildHandle`/`ConsumerHandle` synchronization, no new
  component/archetype/pool) matches the actual shipped code exactly — no
  discrepancy found, no blocker raised.
- Static: grepped `Docs/` for `Projectile Launch Aim` (1 match, in
  `skill-gameplay-system.md`), `## Launch Aim` (1 match, in
  `projectile-system.md`), and `LaunchAimMode` (1 match in each of
  `spawn-events-and-commands.md`, `spawn-template-registry.md`,
  `projectile-system.md`, plus `ProjectileLaunchAimMode` in
  `skill-gameplay-system.md`) — each new heading/bullet landed exactly once,
  in the intended file. A broader grep for `launch-aim` (case-sensitive,
  hyphenated form) confirmed all six target files were touched and no
  seventh file was.
- Read back the changed region of every edited file after editing to confirm
  Markdown renders sensibly: matched code-fence pairs (none added/removed),
  correct list nesting/indentation, and correct relative link targets
  (`../simulation/projectile-system.md#launch-aim` from `Docs/contracts/`,
  `../reference/game-logic/skill-gameplay-system.md#projectile-launch-aim`
  from `Docs/contracts/`, `#launch-aim`/`#tracking-and-movement` same-file
  anchors and `(#launch-aim)` from `Docs/reference/simulation/`), each
  matching the relative-path convention already used by sibling links in the
  same file.
- Specifically preserved (did not "fix") the pre-existing mangled-encoding
  separator character (`鈥?`) in `spawn-template-registry.md`'s bullet list,
  matching it exactly in the one new bullet added there, per the packet's
  explicit instruction.
- Confirmed via `git diff --stat`-equivalent review that only the six allowed
  `Docs/` files changed by this task's edits, plus this log; no file under
  `Assets/` was touched.
- No code compiles here and there is nothing to build or run — this is a
  documentation-only task. Unity EditMode/PlayMode test execution is not
  applicable to this task and was not attempted; the still-outstanding test
  runs named in task 005's validation summary above are unrelated to this
  task and remain the user's responsibility.
### 007-aim-oriented-nova.md
- Confirmed dependency: read
  `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs` in full
  before editing and confirmed it exactly matched the execution packet's
  assumed starting state (task 004's output, as described in this log's
  004 entry) — `hasAim`/`aimedVelocity` threaded into `Execute()` and all four
  pattern methods, `TryResolveAimedVelocity` present, `HasTargetHash`/
  `TargetSnapshot` job fields and `[UpdateAfter(typeof(TargetSpatialHashSystem))]`
  present. No mismatch found, no blocker raised.
- Grepped the modified file for `hasAim` and `aimedVelocity`: zero matches —
  both fully removed (not just renamed), confirming `CreateSideSprayPattern`,
  `CreateRadialPattern`, `CreateForwardPattern`, and `WriteForwardWave` are
  parameterless-aim again.
- Grepped the whole repo for `TryResolveAimedVelocity`, `CreateForwardPattern`,
  `CreateSideSprayPattern`, `CreateRadialPattern`, `WriteForwardWave`, and
  `CreateAimedNovaPattern`: only `ProjectileSpawnExpansionSystem.cs` itself and
  `.agent/` plan docs reference these names — no external caller (test or
  otherwise) depends on the old signatures, so no compile fix was needed
  outside the one allowed file.
- Hand-traced `count = 4`, `aimedDirection = (0, 1)` (up) through
  `CreateAimedNovaPattern`/`Rotate` (`Rotate(v, degrees)` returns
  `(c*v.x - s*v.y, s*v.x + c*v.y)`): `i=0` → `Rotate(up, 0°)` = `(0,1)` up;
  `i=1` → `Rotate(up, 90°)` = `(-1,0)` left; `i=2` → `Rotate(up, 180°)` =
  `(0,-1)` down; `i=3` → `Rotate(up, 270°)` = `(1,0)` right. Matches the
  packet's worked example and task 008's expected test order (up, left, down,
  right) exactly — confirms no sign/winding adjustment was needed.
- Confirmed `count = 1` needs no special case:
  `Rotate(aimedDirection, 360 * 0 / 1) = Rotate(aimedDirection, 0) =
  aimedDirection` unchanged, matching `CreateAimedNovaPattern`'s
  unconditional per-shot loop.
- Confirmed the fallback path (`TryResolveAimedDirection` returns `false`)
  reaches code identical to the file's pre-task-004 state: compared the
  restored `CreateSideSprayPattern`/`CreateRadialPattern`/`CreateForwardPattern`/
  `WriteForwardWave` bodies against the packet's Step 5 snippets — byte-for-byte
  match.
- Confirmed `Stamp`, `WriteCommand`, `SpreadAngle`, `IntervalWaveSeed`,
  `IntervalSideSprayVelocity`, `RadialDirection`, `ProjectileIdFor`, `Rotate`,
  `OnCreate`/`OnDestroy`/`OnUpdate`, class attributes, and
  `ProjectileSpawnEventSingleton` are byte-for-byte unchanged (diff review of
  the full file against the pre-edit read).
- Confirmed no new entity component, archetype, system, pool, or allocation
  was added; `CreateAimedNovaPattern` is the only new method, matching the
  packet's hard boundary.
- Unity/Burst compile check was not run — no Unity CLI/editor invocation
  available/permitted in this environment (consistent with tasks 001-004,
  006). Unity tests were not run per project policy; task 008 (separate,
  later) replaces the now-obsolete PlayMode test coverage (shot-0/RNG-
  preservation tests from task 005), and the user runs and exports XML
  results per project policy. Validation here is static/search-based (grep,
  manual code review, hand-traced rotation math) only.

### 008-aim-oriented-nova-tests.md
- Confirmed dependency: task 007 shows `Complete` in this log; read
  `ProjectileSpawnExpansionSystem.cs`'s `CreateAimedNovaPattern` and
  `TryResolveAimedDirection` directly during this task and confirmed the
  formula (`Rotate(aimedDirection, 360f * i / count) * command.Speed`, all
  shots including `i == 0` written by the same loop, no RNG) matches the
  packet's assumptions exactly. No mismatch found, no blocker raised.
- Grepped the whole `Assets/` tree for `RunSingleWaveAndGetVelocitiesByShotIndex`
  and `DisposeHarness` after deleting them: zero remaining references anywhere,
  confirming both were dead code once the two obsolete tests were removed and no
  other file depended on them.
- Grepped the edited file's `[Test]`-annotated method names for duplicates:
  none found — all 31 test methods (29 pre-existing/untouched + 2 new) have
  unique names.
- Confirmed `CreateHarness`/`ProjectileHarness` still appear exactly where they
  did before (only used by `SetUp`), untouched by this task's deletions.
- Hand-verified the count-4 worked example against `Rotate(v, degrees) =
  (c*v.x - s*v.y, s*v.x + c*v.y)` for `aimedDirection = (0, 1)`: `i=0` → 0° →
  `(0,1)` up; `i=1` → 90° → `(-1,0)` left; `i=2` → 180° → `(0,-1)` down; `i=3`
  → 270° → `(1,0)` right — matches the new test's `expected` array and task
  007's already-verified worked example exactly.
- Hand-verified the count-3 test: `direction(i) = Rotate(aimedDirection, 120°
  * i)` for any starting `aimedDirection` produces three unit vectors evenly
  spaced 120° apart by construction (each is the previous rotated by exactly
  120°, and 3 * 120° = 360° closes the circle), and shot 0 (`i = 0`, rotation
  0°) equals `aimedDirection` unrotated, i.e. `math.normalize(targetOffset)` —
  matches the test's assertions.
- Read the full file after editing to confirm brace balance and that no other
  test body, helper, or using directive was altered.
- Did not modify any file under `Assets/Scripts/` (production code) or either
  of the two explicitly-excluded test files
  (`SkillValidationEditModeTests.cs`, `SpawnCommandUnificationTests.cs`) —
  confirmed via review of every edit made in this task; the only file touched
  is `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`, plus this log.
- Unity PlayMode test execution and XML export were NOT run — no Unity
  CLI/editor invocation is available in this environment. Per project policy
  and the task file, the user must run PlayMode `ProjectileSpawnPipelineTests`
  plus regression suites `ProjectileContinuousSimulationTests` and
  `ProjectileTrackingSimulationTests`, and export
  `Logs/TestResults-PlayMode-TriggeredProjectileAimOrientedNova.xml` for
  review. No pass/fail claim is made here.

### 009-update-aim-oriented-nova-docs.md
- Confirmed dependency: tasks 007 and 008 show `Complete` in this log; read
  `ProjectileSpawnExpansionSystem.cs`'s `CreateAimedNovaPattern`/
  `TryResolveAimedDirection` in full during this task and confirmed every
  claim written into the docs (whole-wave nova, `Rotate(aimDirection, 360 *
  i / count)` formula, shot `0` points at target, remaining shots spaced
  `360/count` apart, stored pattern/spread/jitter/RNG bypassed entirely on
  success, unchanged fallback on failure, both lanes share one acquisition,
  no tracking/steering added) matches the shipped code exactly. No
  discrepancy found, no blocker raised.
- Static: grepped each of the three edited files for the new nova-formula
  phrasing (`radial nova`, `Rotate(aimDirection, 360 degrees`) — each landed
  exactly once, in the intended file/paragraph.
- Grepped all five files in this task's scope for `shot` and `RNG`/`random`
  (case-insensitive): the only remaining `shot`-word matches are the new,
  correct prose (`shot `i`'s velocity`, `shot `0` points`, `each shot's
  initial velocity`, etc.); every other hit across all five files is a
  `Snapshot`/`snapshot` substring. The only remaining `RNG` occurrence is the
  new sentence describing that a successful aim bypasses
  "spread/jitter/RNG entirely" — this is the corrected claim, not a stale
  one. No file states that only shot `0` changes or that later-shot RNG
  matches the disabled-policy control.
- Read back each changed region after editing in each of the three files to
  confirm Markdown renders sensibly: no code fences were added (plain
  prose/markdown code-span replacements only), list nesting is unaffected,
  and the one pre-existing relative link
  (`../reference/simulation/projectile-system.md#launch-aim` from
  `spawn-events-and-commands.md`; `../simulation/projectile-system.md#launch-aim`
  from `skill-gameplay-system.md`) was left byte-for-byte unchanged in both
  files, per the packet's instruction not to touch link paths.
- Confirmed via re-read that `Docs/reference/simulation/spawn-template-registry.md`
  and `Docs/flows/spawn-event-to-entity.md` needed no edit (see Completed
  Tasks above for the specific grep evidence) — both verify-only files were
  left unedited.
- No code compiles here and there is nothing to build or run — this is a
  documentation-only task, same as task 006. Unity EditMode/PlayMode test
  execution is not applicable and was not attempted. This is the final task
  in the revised plan (`007`-`009` supersede `004`-`006`); no further tasks
  remain.

## Plan Revision After Completed Task 006

- User superseded shot-index-0-only velocity replacement: aimed direction must now
  orient full radial nova spread.
- Existing production code/tests/docs still implement old behavior at revision time.
- Follow-up tasks `007`-`009` added. No production code changed during plan revision.
