# Task Execution Packet

## Task
005-launch-aim-tests.md

## Goal
Add test coverage for the full triggered-projectile-launch-aim feature (tasks
001-004, all already implemented and verified) across three existing test files.
This is a test-authoring task only — the feature code itself
(`TriggerLink`, `ProjectileLaunchAimMode`, `RuntimeProjectileDefinition`,
`SkillSetCompiler`, `ProjectileSpawnCommand`, `ProjectileSpawnExpansionSystem`) is
already complete; do not modify any of it. Mirror each file's existing test
patterns exactly rather than inventing new fixture styles.

You will NOT run these tests — Unity test execution is not available in this
environment. Your job is to write correct, compilable, well-targeted test code
matching existing conventions; the user runs the suites afterward and exports XML.

## Files Allowed To Modify
- Assets/Tests/EditMode/SkillValidationEditModeTests.cs
- Assets/Tests/PlayMode/SpawnCommandUnificationTests.cs
- Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs

## Files Allowed To Create
- None (all target files already exist).

## Files Likely Needed For Reading
- Assets/Tests/EditMode/SkillValidationEditModeTests.cs (full file, 1132 lines — in
  particular lines 1-135 for the `CreateAsset<T>`/`SkillLoadoutNode`/
  `SkillSetCompiler.Compile` pattern used by every compiler test, and lines
  1095-1131 for the existing `SetField(object target, string fieldName, object
  value)` reflection helper — already present in this file — which you MUST reuse
  to set the private serialized `projectileLaunchAimMode`/`projectileLaunchAimRange`
  fields on `TriggerLink` subclasses, since `TriggerLink` only exposes a get-only
  `ProjectileLaunchAimMode` property and a `ResolveProjectileLaunchAimRange()`
  method, not public setters. Also read the `StackTrigger`/`OnHitTrigger` test
  examples (~lines 688-930) for how stack/on-hit chains are built with
  `SkillLoadoutNode` arrays.
- Assets/Scripts/Skills/Trigger/TriggerLink.cs (confirms field names
  `projectileLaunchAimMode`, `projectileLaunchAimRange` for use with `SetField`, and
  the read-side `ProjectileLaunchAimMode` property / `ResolveProjectileLaunchAimRange()`)
- Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs (confirms
  `ProjectileLaunchAimMode`/`ProjectileLaunchAimRange` properties to assert against)
- Assets/Tests/PlayMode/SpawnCommandUnificationTests.cs (full file, 247 lines — read
  `MakeAoeTemplate` (~line 234) and the `SpawnTemplateHash_DifferentEchoCountProducesDistinctKey`
  /`SpawnTemplateHash_IdenticalAoeCommandsProduceSameKey` tests (~lines 101-138) as
  the exact pattern to mirror for a new `MakeProjectileTemplate` helper and
  corresponding launch-aim hash tests. Note this file already has
  `projectileExpansion = testWorld.GetOrCreateSystemManaged<ProjectileSpawnExpansionSystem>();`
  wired into its sim group and a `ProjectileSpawnTemplate` registry set up in
  `SetUp()` — the hash tests themselves don't need the ECS world at all, they call
  `SpawnTemplateHash.Of` directly on two `ProjectileSpawnCommand` structs, exactly
  like the AOE hash tests do.)
- Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs (full file, 890 lines — this
  is the big one. Read in particular: `SetUp`/`TearDown` (~lines 34-78),
  `EnqueueEvent`/`MakeEvent`/`RegisterChildProjectileTemplate` (~lines 549-653),
  `ActiveProjectileCount`/`ActiveProjectileVelocities` (~lines 771-890 area), and at
  least 2-3 existing `[Test]` methods that use `deterministicIdTickIndex` > 0 with
  `spawnPatternType: ProjectileChildSpawnPatternType.SideSpray` or `.Radial` — grep
  this file for `spawnPatternType: ProjectileChildSpawnPatternType.SideSpray` and
  `.Radial` to find them; they show exactly how a "child wave" event (as opposed to
  a root cast) is constructed, which is the kind of event launch-aim applies to.)
- Assets/Scripts/System/Targets/CombatTargetProxy.cs (component shapes:
  `TargetProxyTag : IComponentData {}`, `TargetPosition : IComponentData { float2
  Value; }`, `TargetFaction : IComponentData { CombatFaction Value; }`)
- Assets/Scripts/System/Targets/TargetCollisionShape.cs (`TargetCollisionShape :
  IComponentData { CombatShapeType ShapeType; float Radius; float2 HalfExtents;
  float RotationRadians; float2 BoundsMin; float2 BoundsMax; int Mask; }` — for a
  simple circle target proxy in tests, set `ShapeType = CombatShapeType.Circle`,
  `Radius = <value>`, and compute `BoundsMin`/`BoundsMax` via
  `PlayGround.System.Combat.Collision.CombatCollisionMath.ComputeWorldBounds(position, radius, halfExtents, rotationRadians, shapeType, out boundsMin, out boundsMax)` —
  the spatial hash's occupied-cell query relies on `BoundsMin`/`BoundsMax` being
  correct, so do not leave them at `default`.)
- Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs (the
  system you must add to the `ProjectileSpawnPipelineTests` sim group so
  `TargetSpatialHashSingleton` gets built each tick from raw target-proxy entities
  you create directly via `entityManager.CreateEntity(typeof(TargetProxyTag),
  typeof(TargetPosition), typeof(TargetCollisionShape), typeof(TargetFaction))` +
  `SetComponentData` — you do NOT need the managed `CombatTargetProxy.Create`/
  `ICombatTarget` machinery; a raw entity with these four components is exactly what
  the system's query matches.)
- Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs (read-only —
  confirms the exact acquisition preconditions your tests must exercise:
  `LaunchAimMode == NearestHostile`, `LaunchAimRange > 0f`, `Faction !=
  CombatFaction.None`, hash singleton present, a hostile found, and non-coincident
  target position; and confirms `SeedContactGateTargetId`/event's
  `ContactGateSeedTargetId` is the exclusion key, matching
  `CombatTargetAcquisition.TargetKey(entity)`).

## Behavior To Preserve
- Do not modify any production code (already complete from tasks 001-004).
- Do not remove or alter any existing test — only add new `[Test]` methods and, where
  necessary, small new private helper methods, following each file's existing
  helper-naming conventions.
- Existing tests in all three files must remain passing (i.e., don't change shared
  helpers like `MakeEvent`/`RegisterChildProjectileTemplate`/`MakeAoeTemplate` in a
  way that changes their existing default behavior — only ADD new optional
  parameters with defaults that preserve current call sites' behavior, or add new
  sibling helpers).

## Behavior To Change (What To Add)

### 1. `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
Add compiler test cases (using `CreateAsset<T>`, `SetField`, `SkillLoadoutNode[]`,
`SkillSetCompiler.Compile`, matching the existing style exactly) proving:
- An `IntervalSpawnTrigger` with `projectileLaunchAimMode = NearestHostile` and a
  positive range, linking a projectile source to a projectile child target, causes
  the compiled child's `RuntimeProjectileDefinition.ProjectileLaunchAimMode` /
  `ProjectileLaunchAimRange` to match the trigger's authored values, while the
  top-level/root compiled `RuntimeProjectileDefinition` (`runtime` itself, not the
  child) remains `ProjectileLaunchAimMode.None`.
- An `OnHitTrigger` with the same authored policy, linking a projectile source to a
  projectile on-hit target, copies policy onto `ImpactProjectileDefinition` only
  (root stays `None`).
- A `StackTrigger` with the same authored policy, linking to a projectile detonation
  target, copies policy onto the compiled detonation's `RuntimeProjectileDefinition`
  (i.e. `((RuntimeProjectileDefinition)runtime).StackingDetonation.Detonation`) only.
- The top-level/root projectile (the one passed as the active/source skill, not a
  triggered child) always stays `ProjectileLaunchAimMode.None` regardless of any
  trigger authored elsewhere in the chain — assert this directly on `runtime`, not
  just implicitly.
- A continuous projectile child (`((ProjectileDefinition)childSkill.Definition).continuousCollision
  = true`) reached through a trigger with launch aim enabled retains both
  `ContinuousCollision = true` (or the compiled equivalent) AND the copied launch-aim
  policy, with no implied tracking change (`Tracking.Enabled` still false unless
  separately authored) — proves the two are independent.
- A trigger with launch aim enabled that targets a **non-projectile** effect (e.g. an
  `AoeSkill` or `TargetedSkill` target) compiles normally and the resulting
  `RuntimeAoeDefinition`/`RuntimeTargetedDefinition` is unaffected (no exception, no
  spurious field — these types don't even have launch-aim fields, so simply assert
  the compile succeeds and produces the expected type, proving the policy is inert
  for non-projectile targets).

Use `SetField(trigger, "projectileLaunchAimMode", ProjectileLaunchAimMode.NearestHostile);`
and `SetField(trigger, "projectileLaunchAimRange", 5f);` (add
`using PlayGround.System.Combat.Projectiles;` if not already imported — check first,
this file already imports several `PlayGround.System.Combat.*` namespaces at the top
but confirm `Projectiles` is present since `ProjectileChildSpawnPatternType` is
already used in this file, so it likely already is).

### 2. `Assets/Tests/PlayMode/SpawnCommandUnificationTests.cs`
Add a `MakeProjectileTemplate` private helper mirroring `MakeAoeTemplate` (~line
234), e.g. accepting `launchAimMode` and `launchAimRange` parameters (plus whatever
minimal fields `ProjectileSpawnCommand` needs for a valid template — mirror
`RegisterChildProjectileTemplate` in `ProjectileSpawnPipelineTests.cs` for a minimal
valid set: `TypeId`, `RenderTypeId`, `Count`, `SpawnPatternType`, `Speed`,
`Lifetime`, `Radius`, `HalfExtents`, `ShapeType`, `HitPayload`). Add three `[Test]`
methods mirroring `SpawnTemplateHash_DifferentEchoCountProducesDistinctKey`/
`SpawnTemplateHash_IdenticalAoeCommandsProduceSameKey` (~lines 101-138):
- Template hash differs when only `LaunchAimMode` differs (`None` vs
  `NearestHostile`, same range).
- Template hash differs when only `LaunchAimRange` differs (same mode, different
  range).
- Template hash matches for two templates built with identical launch-aim mode and
  range (plus identical other fields).

### 3. `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
This is the integration test file and needs the most setup work:

a. In `SetUp()`, add `TargetSpatialHashSystem` to the sim group (add the using
   `PlayGround.System.Combat.Collision.Broadphase;` if missing) — e.g.
   `simGroup.AddSystemToUpdateList(testWorld.GetOrCreateSystemManaged<TargetSpatialHashSystem>());`
   — placed so `simGroup.SortSystems()` (already called at the end of `SetUp`) can
   satisfy `ProjectileSpawnExpansionSystem`'s `[UpdateAfter(typeof(TargetSpatialHashSystem))]`
   constraint (added in task 004). `TargetSpatialHashSystem` is `ISystem`
   (struct) — use `GetOrCreateSystem<TargetSpatialHashSystem>()` (no `Managed`
   suffix) matching how this file already creates other `ISystem` systems (see
   `CombatArmingSystem`/`CombatLifetimeSystem`/`ProjectileMovementSystem` at the top
   of `SetUp`).

b. Add a small helper to create a raw target-proxy entity directly (no scope/managed
   machinery needed), e.g.:
   ```csharp
   private Entity CreateTargetProxy(float2 position, float radius, CombatFaction faction)
   {
       Entity entity = entityManager.CreateEntity(
           typeof(TargetProxyTag),
           typeof(TargetPosition),
           typeof(TargetCollisionShape),
           typeof(TargetFaction));
       entityManager.SetComponentData(entity, new TargetPosition { Value = position });
       CombatCollisionMath.ComputeWorldBounds(
           position, radius, float2.zero, 0f, CombatShapeType.Circle,
           out float2 boundsMin, out float2 boundsMax);
       entityManager.SetComponentData(entity, new TargetCollisionShape
       {
           ShapeType = CombatShapeType.Circle,
           Radius = radius,
           BoundsMin = boundsMin,
           BoundsMax = boundsMax
       });
       entityManager.SetComponentData(entity, new TargetFaction { Value = faction });
       return entity;
   }
   ```
   Add `using PlayGround.System.Combat.Targets;` if not already present (it likely
   already is, since this file already imports `PlayGround.System.Combat.Targets`
   per its top-of-file usings — confirm).

c. Extend `MakeEvent` (~line 554) with new optional parameters (defaults preserving
   every existing call site's current behavior):
   `ProjectileLaunchAimMode launchAimMode = ProjectileLaunchAimMode.None`,
   `float launchAimRange = 0f`, `CombatFaction faction = CombatFaction.Player`
   (this file's `MakeEvent` currently hard-codes `Faction = CombatFaction.Player` on
   the returned event — generalize both the template's implicit faction-neutrality
   and the event's `Faction` field to use the new `faction` parameter, since
   launch-aim tests need to control shooter faction independently of target
   faction). Set `LaunchAimMode = launchAimMode, LaunchAimRange = launchAimRange` on
   the `ProjectileSpawnCommand` built inside `MakeEvent`.

d. Add `[Test]` methods proving (use `deterministicIdTickIndex: 1` with an explicit
   `spawnPatternType` for wave-style tests, matching how existing SideSpray/Radial
   tests in this file already construct "child wave" events — root casts, i.e.
   `deterministicIdTickIndex: 0`, always use forward pattern regardless of
   `spawnPatternType`, per the existing `ProjectileExpansionJob.Execute` logic):
   - Nearest hostile in range determines exactly one straight launch velocity
     (single-shot wave: `count: 1`, aim enabled, one target proxy placed off-axis —
     assert the resulting projectile's velocity direction matches the normalized
     direction to the target times `Speed`, not the authored `baseDirection`).
   - Same-faction nearer target is skipped (place a same-faction proxy closer than a
     hostile proxy; assert the aimed shot targets the hostile one, not the nearer
     ally — or if no hostile exists, assert normal fallback pattern is used).
   - Contact-gate seed target is excluded and next nearest hostile is selected
     (place two hostile proxies; pass the nearer one's `CombatTargetAcquisition.TargetKey(entity)`
     — note: use the entity returned by your `CreateTargetProxy` helper — as the
     event's/command's contact-gate seed value via whatever `MakeEvent` parameter
     threads through to `ContactGateSeedTargetId`/`SeedContactGateTargetId`; check
     `MakeEvent`'s existing parameters for one, or add a
     `int contactGateSeedTargetId = 0` optional parameter to `MakeEvent` if none
     exists, wiring it to the event's `ContactGateSeedTargetId` field — confirm by
     reading `MakeEvent` fully first); assert the farther hostile is targeted
     instead.
   - No hostile in range preserves existing fallback pattern (no target proxies, or
     only out-of-range/same-faction ones; aim enabled; assert velocity matches the
     normal disabled-policy pattern for that shot).
   - Disabled policy (`launchAimMode: ProjectileLaunchAimMode.None`) preserves
     existing fallback pattern even with a hostile present in range.
   - Single-shot wave aims directly at target (may overlap with the first case
     above — keep as a distinct explicitly-named test if the task's own wording
     calls it out separately, per the task file).
   - Multi-shot radial wave (`spawnPatternType: .Radial`, `count: 4+`) replaces only
     deterministic shot `0`; all remaining nova velocities match a disabled-policy
     control wave with identical `jitterSeed`/`deterministicIdTickIndex` (build both
     an aim-enabled and an aim-disabled event with otherwise-identical parameters,
     tick each in isolation — e.g. separate `[Test]` methods or separate `SetUp`
     instances per assertion — and compare shots 1..N-1's velocities for equality,
     shot 0's velocity for inequality/aimed-value equality).
   - Multi-shot side-spray/forward wave (`spawnPatternType: .SideSpray` or
     `.Forward`, `count: 3+`) replaces only deterministic shot `0`; all remaining
     velocities match a disabled-policy control wave built with the same
     `jitterSeed`.
   - RNG sequence for non-aimed jitter/side-spray shots is unchanged by replacement
     (this follows from the above control-wave comparison — an explicit test
     comparing shot 1's velocity between an aim-enabled and aim-disabled run with
     identical seeds is sufficient).
   - Launch aim does not add a projectile or alter deterministic projectile IDs
     (assert `ActiveProjectileCount()` equals `count` in both aim-enabled and
     aim-disabled runs, and that the set of `ProjectileIdentityComponent.ProjectileId`
     values is identical between the two runs for the same seed/tick-index).
   - Continuous command receives launch aim and materializes in the continuous
     archetype without tracking (`continuousCollision: true` + aim enabled; assert
     the resulting entity has no enabled `ProjectileTrackingComponent`/is queryable
     as a continuous projectile per this file's existing continuous-projectile
     assertions elsewhere — grep for `continuousCollision: true` in this file for
     the existing pattern to mirror).
   - Missing hash singleton does not fail and uses fallback (a variant of the setup
     that does NOT add `TargetSpatialHashSystem` to the sim group, or removes/never
     creates the singleton — simplest: a `[Test]` using a `World` built without
     `TargetSpatialHashSystem` added; if that's awkward given a shared `SetUp`,
     alternatively verify by simply never creating any target proxies AND confirming
     no exception is thrown and normal pattern velocity results — combined with the
     "no hostile in range" case above, this may already be covered without a
     separate world; use judgment and prefer the simpler approach, noting the choice
     in your final report as a minor implementation-detail deviation, not an
     architectural one).

Retain existing tests for directionless side-spray, radial fan-out, default heading,
discrete/continuous pool separation, continuous no-tracking, and manual forward
volley — do not modify or remove them (per the task file).

## Relevant Global Context
- All production code for this feature is complete and verified correct (tasks
  001-004). This task only adds coverage; if while writing tests you discover the
  production code does NOT behave as tasks 001-004's acceptance criteria describe,
  STOP and report it as a blocker rather than writing a test that papers over or
  "adjusts for" unexpected behavior — that would indicate a real bug in the
  already-implemented feature, which is outside this task's mandate to fix.
- `CombatTargetAcquisition.TrySelectNthNearest` filters out same-faction candidates
  and the excluded key internally — your tests exercise this through the public
  event/template surface, not by calling `CombatTargetAcquisition` directly.
- Determinism: `ProjectileIdFor` hashes `ProjectileId`/`JitterSeed`/
  `DeterministicIdTickIndex`/shot-index — this is unaffected by launch aim (velocity
  is the only thing that changes), so any "IDs unchanged" assertion is really
  checking that enabling aim doesn't perturb the existing ID-hash inputs.

## Dependencies Confirmed
- Tasks 001-004 complete and verified: `CombatTargetAcquisition` (renamed/moved),
  `ProjectileLaunchAimMode` enum + `TriggerLink` authoring fields, `RuntimeProjectileDefinition`/
  `ProjectileSpawnCommand` policy fields wired through `SkillSetCompiler`/
  `SkillIntervalTemplateBuilder`, and `ProjectileSpawnExpansionSystem`'s shot-0
  redirect + `TargetSpatialHashSingleton` dependency wiring — all confirmed present
  by direct file reads during this plan's execution.

## Step-By-Step Instructions
Work through the three files in the order listed above (EditMode compiler tests
first, then the hash tests, then the larger integration test file), since the
integration file is the most involved and benefits from having the simpler two done
first as a warm-up on the codebase's test idioms. For each file:
1. Read the file sections pointed to above.
2. Add new private helpers only where explicitly justified above.
3. Add new `[Test]` methods matching the acceptance criteria, following the file's
   existing naming convention (`CompilerXxx` for compiler tests, `SpawnTemplateHash_Xxx`
   for hash tests, descriptive `PascalCase` sentence-like names for pipeline tests —
   match whatever each file already does).
4. Do not touch unrelated existing tests or helpers beyond additive optional
   parameters as described.

## Acceptance Criteria
(Reproduced from the task file — treat each bullet as one or more test cases across
the three files as scoped above.)

### EditMode - `SkillValidationEditModeTests`
- interval trigger copies nearest-hostile mode/range to projectile child only
- on-hit trigger copies policy to projectile child only
- stack trigger copies policy to projectile detonation only
- top-level/root projectile remains launch-aim disabled
- continuous projectile child retains continuous collision plus launch aim, with no implied tracking change
- non-projectile triggered target ignores projectile launch-aim policy

### PlayMode - `SpawnCommandUnificationTests`
- template hash differs by launch-aim mode
- differs by launch-aim range
- matches for identical launch-aim content

### PlayMode - `ProjectileSpawnPipelineTests`
- nearest hostile in range determines exactly one straight launch velocity
- same-faction nearer target is skipped
- contact-gate seed target is excluded and next nearest hostile selected
- no hostile in range preserves existing fallback pattern
- disabled policy preserves existing fallback pattern
- single-shot wave aims directly at target
- multi-shot radial wave replaces only deterministic shot 0; all remaining nova velocities match disabled-policy control wave
- multi-shot side-spray/forward wave replaces only deterministic shot 0; all remaining velocities match disabled-policy control wave
- RNG sequence for non-aimed jitter/side-spray shots is unchanged by replacement
- launch aim does not add a projectile or alter deterministic projectile IDs
- continuous command receives launch aim and materializes in continuous archetype without tracking
- missing hash singleton does not fail and uses fallback
- retain existing tests for directionless side-spray, radial fan-out, default heading, discrete/continuous pool separation, continuous no-tracking, and manual forward volley (i.e. do not remove/break them)

## Validation Required
- Static/search-based: grep each modified file to confirm every new `[Test]` method
  has a unique name and compiles conceptually (correct types, correct existing
  helper signatures used).
- Code review: re-read each new test against the acceptance criteria bullet it's
  meant to cover.
- You must NOT run Unity EditMode/PlayMode tests — no Unity CLI/editor invocation is
  available in this environment. Explicitly state this in your report. Per the
  task file, the user runs:
  - EditMode: `SkillValidationEditModeTests`
  - PlayMode: `SpawnCommandUnificationTests`
  - PlayMode: `ProjectileSpawnPipelineTests`
  - PlayMode regression: `ProjectileContinuousSimulationTests`
  - PlayMode regression: `ProjectileTrackingSimulationTests`
  and exports XML under `Logs/` (e.g. `Logs/TestResults-EditMode-TriggeredProjectileLaunchAim.xml`,
  `Logs/TestResults-PlayMode-TriggeredProjectileLaunchAim.xml`) for later review.

## Hard Boundaries
- Do not modify any production code file (everything under `Assets/Scripts/`).
- Do not change architecture or introduce new production abstractions.
- Do not modify or delete any existing test method.
- Do not combine this task with task 006 (no doc changes).
- Do not reopen index-level decisions.
- Stop and report as a blocker (not a test workaround) if production behavior
  doesn't match what tasks 001-004's acceptance criteria promised.
- Do not hand-edit Unity `.meta` files.
