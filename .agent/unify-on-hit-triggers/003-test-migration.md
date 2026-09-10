# 003 — Test migration

**Depends on:** [001-onhittrigger-collapse.md](001-onhittrigger-collapse.md).
**Must land in the same commit as 001** — the EditMode and PlayMode assemblies
reference the deleted types directly, so a split leaves them uncompilable.
**Scope:** Small. Ten call sites, all type substitutions, plus two new cases.

## Existing sites

Every current use constructs the trigger and wires it into nodes; none asserts
on the trigger's subtype, and — checked site by site — none sets or reads
`spawnCount` / `spreadDegrees`. So removing those fields breaks no existing
assertion, and all ten sites are mechanical replacements with `OnHitTrigger`.

### `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`

| Line | Test | Change |
|---|---|---|
| 80 | `CompilerAddsTriggeredManaCostToActiveSkill` | `OnImpactAoeTrigger` → `OnHitTrigger`. Asserted `ManaCost == 5f` is unaffected (I2 preserves the stamp). |
| 107 | `CompilerAddsChainedTriggeredManaCostsToActiveSkill` | `firstTrigger`: `OnImpactAoeTrigger` → `OnHitTrigger`. |
| 108 | same test | `secondTrigger`: `OnImpactProjectileTrigger` → `OnHitTrigger`. Both keep their `manaCostMultiplier` assignments; chained aggregation is unchanged. |
| 243 | `CompilerAppliesTriggerManaCostIncreased` | `OnImpactAoeTrigger` → `OnHitTrigger`; keeps `manaCostMultiplier` / `manaCostIncreased`; still expects `14f`. |
| 695 | `ValidatorDoesNotWarnForProjectileToAoeImpactLink` | `OnImpactAoeTrigger` → `OnHitTrigger`. Still expects `warnings` empty — this is the direct check that `OnHitTrigger`'s tags pass generic validation for Projectile→Aoe. Rename to `ValidatorDoesNotWarnForProjectileToAoeOnHitLink`. |
| 764-765 | `CompilerAttachesStackTriggerAfterNormalImpactLinks` | both impact triggers → `OnHitTrigger` (two separate instances; keep them distinct so the two chains stay independent). `StackTrigger` usage in this test is untouched. |
| 850-851 | `ClearSkillKeepsItsAdjacentTriggers` | both → `OnHitTrigger`. This test only checks node/trigger retention across an edit; any `TriggerLink` would do. |

### `Assets/Tests/EditMode/ProjectileContinuousAuthoringEditModeTests.cs:136`

`CreateAsset<OnImpactProjectileTrigger>()` → `CreateAsset<OnHitTrigger>()`. The
assertion reads `impactRoot.ImpactProjectileDefinition.ContinuousCollision`,
which the unified projectile-target arm still fills; the test never touched
count or spread.

### `Assets/Tests/PlayMode/AoePlayModeTests.cs:712`

`ScriptableObject.CreateInstance<OnImpactAoeTrigger>()` →
`CreateInstance<OnHitTrigger>()` in
`ProjectileImpactAoeApplicatorStackTriggerDetonatesStackSet`. Nothing else in
that fixture changes; the `StackTrigger` half is untouched.

## New coverage

The current suite exercises only projectile-source cells. Task 001 makes the
AOE-source cells reachable through the same type, and behavior change #3 in the
index is a real fix worth pinning. Add to `SkillValidationEditModeTests`:

1. **`CompilerAttachesOnHitAoeSourceToProjectileTarget`** — AOE set → `OnHitTrigger`
   → projectile set. Assert the compiled root is `RuntimeAoeDefinition` and that
   `OnHitProjectileSpawnDefinition` is non-null. This is the cell the old
   `OnAoeHitSpawnTrigger` mis-assigned to the AOE slot.
2. **`CompilerLeavesOnHitProjectileTargetBurstToTheEffectSet`** — author the
   effect `ProjectileSkill` with `count = 3` and `spreadDegrees = 45f`, wire
   projectile → `OnHitTrigger` → that set, and assert
   `ImpactProjectileDefinition.Count == 3` and `SpreadDegrees == 45f`. This pins
   the attribute removal (index I4): the trigger contributes nothing, and the
   child matches what the effect set compiles to on its own. Without this test
   a future re-addition of trigger-side count/spread would pass silently.

   Optional strengthening: compile the same effect set as a root in the same
   test and assert the two `Count` / `SpreadDegrees` pairs are equal, which
   states the invariant directly rather than restating the authored numbers.

Both follow the existing `CreateAsset` / `CreateSkillSet` / `SkillSetCompiler.Compile`
shape used at `:80-100`.

## Acceptance criteria

1. A project-wide search over `Assets/Tests/` for the four deleted type names
   returns nothing.
2. Both test assemblies compile.
3. Every migrated test keeps its original assertions and expected values.
4. The two new tests exist and pass.
5. EditMode and PlayMode suites are run in Unity by the user; the resulting
   `Logs/TestResults-EditMode.xml` and `Logs/TestResults-PlayMode.xml` are
   inspected before reporting tests as passing. Do not claim a pass without
   reading the result files.
