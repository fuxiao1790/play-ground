# Task Execution Packet

## Task
008-aim-oriented-nova-tests.md

## Goal
Task 007 replaced "redirect only shot index 0" with "successful acquisition
recomputes the entire wave as one radial nova oriented from the acquired
direction" (`direction(i) = Rotate(aimDirection, 360 * i / count)`), completely
bypassing the stored forward/side-spray/radial pattern (and its RNG) on success.
Two existing tests in `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
assert the now-false claim that non-zero shots keep matching a disabled-policy
control wave. Replace those two tests with tests that assert the actual
aim-oriented-nova shape. Everything else in this file (compiler-unrelated
acquisition/faction/contact-gate/fallback/ID tests) is unaffected by the
nova-shape change and needs no edits — task 007 did not change acquisition
target-selection, only what happens with the direction once found.

## Files Allowed To Modify
- Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs

## Files Allowed To Create
- None.

## Files Likely Needed For Reading
- Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs — read the full file, but
  in particular:
  - Lines ~587-794 (all the still-valid launch-aim tests: `NearestHostileInRangeAimsSingleShotVelocity`,
    `SameFactionNearerTargetIsSkippedInFavorOfHostile`,
    `ContactGateSeedTargetIsExcludedAndNextNearestHostileIsSelected`,
    `NoHostileInRangePreservesFallbackPattern`,
    `MissingOrEmptyTargetHashFallsBackToNormalPattern`,
    `DisabledLaunchAimPreservesFallbackPatternWithHostilePresent`,
    `LaunchAimDoesNotAddProjectilesOrAlterDeterministicIds`,
    `ContinuousCommandReceivesLaunchAimWithoutTracking`) — confirm these are
    exactly as described below and leave every one of them untouched.
  - Lines ~797-896: the two OBSOLETE tests to remove —
    `RadialWaveReplacesOnlyShotZeroVelocityRelativeToDisabledControl` and
    `SideSprayWaveReplacesOnlyShotZeroVelocityRelativeToDisabledControl`.
  - Lines ~1049-1132: `RunSingleWaveAndGetVelocitiesByShotIndex` and
    `FindProjectileById` — the first is used ONLY by the two obsolete tests above
    (confirmed via repo-wide grep before writing this packet) and must be removed
    with them; `FindProjectileById` is a small, still-useful-by-you helper, keep
    it (your new tests will call it directly, described below).
  - `CreateHarness`/`DisposeHarness`/`ProjectileHarness` (~lines 40, 74, 116):
    `CreateHarness`/`ProjectileHarness` are used by `SetUp()` itself — DO NOT
    remove them. `DisposeHarness` is used ONLY by the two obsolete tests
    (confirmed via grep) — remove it along with them.
  - `MakeEvent` (~line 910): already has `spreadDegrees`, `spawnPatternType`,
    `launchAimMode`, `launchAimRange`, `faction`, `contactGateSeedTargetId`,
    `jitterSeed`, `deterministicIdTickIndex`, `baseProjectileId`, `speed`,
    `position` parameters — everything the new tests need already exists; no
    `MakeEvent` changes required.
  - `ExpectedChildId` (~line 1332) and `CreateTargetProxy` (~line 1015) — reused
    by the new tests exactly as-is.
- Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs (read-only —
  confirms `CreateAimedNovaPattern`'s exact formula: `Rotate(aimedDirection, 360f *
  i / count) * command.Speed`, and that `Rotate`'s convention for
  `aimedDirection = (0,1)`, `count = 4` yields `i=0`→up, `i=1`→left, `i=2`→down,
  `i=3`→right — the worked example your new count-4 test must match).

## Behavior To Preserve
- Do not modify `SkillValidationEditModeTests.cs` or `SpawnCommandUnificationTests.cs`
  — neither file's tests describe pattern shape; both remain valid as-is (compiler
  policy-copying and template-hash tests are unaffected by how expansion turns a
  resolved direction into per-shot velocities).
- Do not modify any of the 8 still-valid tests listed above in
  `ProjectileSpawnPipelineTests.cs`, or any pre-existing test unrelated to launch
  aim (directionless side-spray, radial fan-out, default heading, discrete/continuous
  pool separation, continuous no-tracking, manual forward volley, etc.).
- `CreateHarness`, `ProjectileHarness`, `SetUp`, `TearDown` stay exactly as they are.

## Behavior To Change
1. Delete `RadialWaveReplacesOnlyShotZeroVelocityRelativeToDisabledControl` and
   `SideSprayWaveReplacesOnlyShotZeroVelocityRelativeToDisabledControl` in full
   (both `[Test]` methods).
2. Delete `RunSingleWaveAndGetVelocitiesByShotIndex` and `DisposeHarness` (both now
   dead code once the two tests above are gone — confirm via a fresh grep after
   deleting the tests that nothing else references them before removing).
3. Add one small helper (place it near `FindProjectileById`):
   ```csharp
   private float2[] VelocitiesByShotIndex(int count, int baseProjectileId, uint jitterSeed, int tickIndex)
   {
       var velocities = new float2[count];
       for (int shotIndex = 0; shotIndex < count; shotIndex++)
       {
           int id = ExpectedChildId(baseProjectileId, (int)jitterSeed, tickIndex, shotIndex);
           Entity entity = FindProjectileById(entityManager, id);
           velocities[shotIndex] = entityManager.GetComponentData<CombatKinematicsComponent>(entity).Velocity;
       }
       return velocities;
   }
   ```
   This resolves each shot's velocity by its deterministic id (via the existing
   `ExpectedChildId` formula and `FindProjectileById`), which is index-safe —
   `ActiveProjectileVelocities()` is not, since it returns query-iteration order,
   not shot order.
4. Add two new `[Test]` methods (placed where the two deleted tests were):

   ```csharp
   [Test]
   public void AimedNovaBypassesStoredSideSprayPatternWithCount4UpLeftDownRightOrder()
   {
       const float speed = 5f;
       const uint seed = 33u;
       const int tickIndex = 1;
       const int baseId = 800;
       const int count = 4;

       CreateTargetProxy(new float2(0f, 10f), radius: 0.5f, faction: CombatFaction.Mob);
       EnqueueEvent(MakeEvent(
           count: count,
           position: float2.zero,
           speed: speed,
           spreadDegrees: 45f,
           jitterSeed: seed,
           deterministicIdTickIndex: tickIndex,
           baseProjectileId: baseId,
           spawnPatternType: ProjectileChildSpawnPatternType.SideSpray,
           launchAimMode: ProjectileLaunchAimMode.NearestHostile,
           launchAimRange: 20f));

       Tick(0.01f);

       float2[] velocities = VelocitiesByShotIndex(count, baseId, seed, tickIndex);
       float2[] expected =
       {
           new(0f, speed),   // shot 0: directly at target (up)
           new(-speed, 0f),  // shot 1: 90 degrees around (left)
           new(0f, -speed),  // shot 2: 180 degrees around (down)
           new(speed, 0f),   // shot 3: 270 degrees around (right)
       };
       for (int i = 0; i < count; i++)
       {
           Assert.That(velocities[i].x, Is.EqualTo(expected[i].x).Within(0.001f), $"shot {i} x");
           Assert.That(velocities[i].y, Is.EqualTo(expected[i].y).Within(0.001f), $"shot {i} y");
       }
   }

   [Test]
   public void AimedNovaBypassesStoredForwardPatternWithCount3EvenAngularSpacing()
   {
       const float speed = 5f;
       const uint seed = 71u;
       const int tickIndex = 1;
       const int baseId = 900;
       const int count = 3;
       var targetOffset = new float2(7f, 3f); // arbitrary non-axis direction

       CreateTargetProxy(targetOffset, radius: 0.5f, faction: CombatFaction.Mob);
       EnqueueEvent(MakeEvent(
           count: count,
           position: float2.zero,
           speed: speed,
           spreadDegrees: 45f,
           jitterSeed: seed,
           deterministicIdTickIndex: tickIndex,
           baseProjectileId: baseId,
           spawnPatternType: ProjectileChildSpawnPatternType.Forward,
           launchAimMode: ProjectileLaunchAimMode.NearestHostile,
           launchAimRange: 20f));

       Tick(0.01f);

       float2[] velocities = VelocitiesByShotIndex(count, baseId, seed, tickIndex);
       float2 expectedShot0 = math.normalize(targetOffset) * speed;
       Assert.That(velocities[0].x, Is.EqualTo(expectedShot0.x).Within(0.001f));
       Assert.That(velocities[0].y, Is.EqualTo(expectedShot0.y).Within(0.001f));

       for (int i = 0; i < count; i++)
       {
           int j = (i + 1) % count;
           float cosAngle = math.dot(math.normalize(velocities[i]), math.normalize(velocities[j]));
           float angleDegrees = math.degrees(math.acos(math.clamp(cosAngle, -1f, 1f)));
           Assert.That(angleDegrees, Is.EqualTo(120f).Within(0.5f),
               $"angle between shot {i} and shot {j}");
       }
   }
   ```

## Relevant Global Context
- These two new tests together satisfy 4 of task 008's acceptance-criteria
  bullets: the count-4 up/left/down/right order, the count-3 120-degree spacing,
  AND (by authoring nonzero `spreadDegrees` with `SpawnPatternType.SideSpray` /
  `.Forward` respectively) proof that a successful aim bypasses the stored
  fallback pattern entirely rather than blending with it.
- `ActiveProjectileVelocities()` (used by the still-valid single-shot tests) is
  fine for `count == 1` since there is only one entity to find, but is NOT
  reliable for multi-shot ordering — that is why the new multi-shot tests use
  `VelocitiesByShotIndex`/`FindProjectileById`/`ExpectedChildId` instead, exactly
  like the deleted tests did for their `aimedVelocities`/`controlVelocities`
  arrays.
- `Rotate`'s rotation-matrix convention was hand-verified against this exact
  scenario during task 007: for `aimedDirection = (0, 1)` and `count = 4`, the
  four directions are up, left, down, right in that order — this is not a new
  derivation, it is restating task 007's already-verified worked example as a
  test.

## Dependencies Confirmed
- Task 007 complete (see `implementation-log.md`): `ProjectileSpawnExpansionSystem`'s
  `CreateAimedNovaPattern`/`TryResolveAimedDirection` implement exactly the formula
  this task's tests assert against — confirmed by direct file read at the start of
  this task.

## Step-By-Step Instructions
1. Read the current file in full.
2. Delete the two obsolete `[Test]` methods and the two now-dead helpers
   (`RunSingleWaveAndGetVelocitiesByShotIndex`, `DisposeHarness`), confirming via
   grep that nothing else references them before deleting.
3. Add `VelocitiesByShotIndex` near `FindProjectileById`.
4. Add the two new `[Test]` methods in place of the deleted ones (or anywhere
   sensible near the other launch-aim tests — keep them grouped together).
5. Do not touch anything else in the file.

## Acceptance Criteria
- Count `1`: only projectile points at nearest hostile. (already covered by
  existing `NearestHostileInRangeAimsSingleShotVelocity` — no change needed)
- Count `4`, target above origin: deterministic shot order is up, left, down,
  right, within float tolerance. (new test)
- Count `3`, target at arbitrary non-axis direction: shot `0` matches target and
  pairwise angular spacing is `120` degrees. (new test)
- Stored fallback pattern `SideSpray` plus successful acquisition still produces
  aim-oriented radial nova. (covered by the new count-4 test's authored
  `spreadDegrees` + `SpawnPatternType.SideSpray`)
- Stored fallback pattern `Forward` plus successful acquisition still produces
  aim-oriented radial nova. (covered by the new count-3 test's authored
  `spreadDegrees` + `SpawnPatternType.Forward`)
- Disabled policy and failed acquisition retain existing pattern outputs.
  (already covered by `DisabledLaunchAimPreservesFallbackPatternWithHostilePresent`,
  `NoHostileInRangePreservesFallbackPattern`,
  `MissingOrEmptyTargetHashFallsBackToNormalPattern` — no change needed)
- Same-faction and contact-gate exclusions still select correct target. (already
  covered by `SameFactionNearerTargetIsSkippedInFavorOfHostile`,
  `ContactGateSeedTargetIsExcludedAndNextNearestHostileIsSelected` — no change
  needed)
- Continuous template produces same oriented nova and remains no-tracking.
  (already covered by `ContinuousCommandReceivesLaunchAimWithoutTracking` for the
  `count == 1` case, where oriented-nova and old single-shot-aim behavior
  coincide — no change needed)
- Count and deterministic projectile IDs unchanged. (already covered by
  `LaunchAimDoesNotAddProjectilesOrAlterDeterministicIds` — no change needed)
- Remove/rename tests asserting non-aimed shots or RNG sequence match
  disabled-policy control, because those assertions encode superseded behavior.
  (done via deleting the two obsolete tests)

## Validation Required
- Static/search-based: grep the file for `RunSingleWaveAndGetVelocitiesByShotIndex`
  and `DisposeHarness` after editing to confirm zero remaining references.
- Grep for duplicate `[Test]` method names across the file to confirm none.
- Code review: hand-verify the count-4 expected array and the count-3 120-degree
  math against `CreateAimedNovaPattern`'s formula (already done in this packet's
  "Relevant Global Context" — re-confirm, don't just trust it blindly).
- Do not run Unity tests — no Unity CLI/editor invocation is available in this
  environment. State this plainly. The user runs `ProjectileSpawnPipelineTests`
  (plus regression suites `ProjectileContinuousSimulationTests`,
  `ProjectileTrackingSimulationTests`) and exports
  `Logs/TestResults-PlayMode-TriggeredProjectileAimOrientedNova.xml` per the task
  file.

## Hard Boundaries
- Do not modify any file under `Assets/Scripts/` (production code).
- Do not modify `SkillValidationEditModeTests.cs` or `SpawnCommandUnificationTests.cs`.
- Do not remove or modify any of the 8 still-valid launch-aim tests, or any
  unrelated pre-existing test.
- Do not change architecture or introduce new production abstractions.
- Do not combine this task with task 009 (no doc changes).
- Do not reopen index-level decisions.
- Stop on architectural ambiguity or if production behavior doesn't match what
  task 007 promised (report as blocker, don't paper over it with a workaround
  test).
- Do not hand-edit Unity `.meta` files.
