# 005 — Update test call sites

## Scope

- `Assets/Tests/PlayMode/AoePlayModeTests.cs`
- `Assets/Tests/EditMode/ModifierFoldEditModeTests.cs`
- `Assets/Tests/EditMode/ProjectileContinuousAuthoringEditModeTests.cs`
- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
- `Assets/Tests/EditMode/TargetedValidationEditModeTests.cs`

## Change

### Mechanical substitutions

Every `ScriptableObject.CreateInstance<ProjectileIntervalSpawnTrigger>()` /
`<AoeIntervalSpawnTrigger>()` / `<TargetedIntervalSpawnTrigger>()` and every
`CreateAsset<...>(...)` call using those three type names becomes
`CreateAsset<IntervalSpawnTrigger>(...)` (or `CreateInstance<IntervalSpawnTrigger>()`
in `AoePlayModeTests.cs`, which doesn't use the `CreateAsset` helper). Field
sets (`trigger.energyPerSecond`, `trigger.echoCount`, `trigger.scatterRadius`,
`trigger.projectileCount`, `trigger.manaCostMultiplier`, `trigger.manaCostIncreased`)
are unchanged — all four extra fields now live on the one type.

This covers, without behavior changes:
- `AoePlayModeTests.cs`: 3 call sites (lines ~315-316, 409-410, 502-503),
  including two that already construct one `ProjectileIntervalSpawnTrigger`
  and one `AoeIntervalSpawnTrigger` side by side for the same test — both
  become `IntervalSpawnTrigger` instances (still two separate instances, one
  per link, just same C# type now).
- `ModifierFoldEditModeTests.IntervalEnergyGainFoldAppliesBaseIncreaseAndMultiplier`
  (line ~204).
- `ProjectileContinuousAuthoringEditModeTests.cs` (line ~127).
- `TargetedValidationEditModeTests.ValidatorWarnsWhenTargetedIntervalChildCannotAccrueEnoughEnergy`
  (line ~83).
- `SkillValidationEditModeTests.cs`: all remaining `ProjectileIntervalSpawnTrigger`/
  `AoeIntervalSpawnTrigger` call sites except the ones called out below, which
  need a real rewrite, not a substitution — e.g. `CompilerAppliesIntervalManaCostIncreased`,
  `CompilerPopulatesAoeIntervalScatterRadius`,
  `CompilerPopulatesProjectileIntervalSetupOnLingeringAoeSource`,
  `CompilerPopulatesAoeIntervalSetupOnLingeringAoeSource`,
  `CompilerAppliesEnergyThresholdFromChildManaCost` (or equivalent — the one
  asserting `EnergyThreshold` at line ~444), and the `StackTrigger` +
  `ProjectileIntervalSpawnTrigger` combination test (~774) are plain
  substitutions (type name only); the ones below need real content changes.

### Tests that need rewriting, not just renaming

**`ValidatorWarnsWhenProjectileIntervalTargetsAoeSkill`** (line ~50) currently
asserts that a `ProjectileIntervalSpawnTrigger` pointed at an AOE effect
produces an `UnsupportedTriggerTarget` warning ("will do nothing"). That
scenario no longer exists — the merged trigger's `TargetSkillTags` is `Any`,
so a projectile source → AOE effect is now a supported combination. Delete
this test; it has no post-merge equivalent to assert (there's no "wrong
variant" left to warn about for interval-spawn links).

**`CompilerIgnoresProjectileIntervalTargetAoeSkill`** (line ~68) currently
asserts the same combination compiles to `RuntimeProjectileDefinition` with
`ChildSpawnSetup == null` (silent no-op). Rewrite it to assert the new,
correct behavior — the merged trigger now populates `AoeIntervalSpawnSetup`:

```csharp
[Test]
public void CompilerPopulatesAoeIntervalSetupWhenProjectileSourceTargetsAoeSkill()
{
    ProjectileSkill sourceSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
    AoeSkill targetSkill = CreateAsset<AoeSkill>("Aoe Skill");
    SkillSet sourceSet = CreateSkillSet("Projectile Set", sourceSkill);
    SkillSet targetSet = CreateSkillSet("Aoe Set", targetSkill);
    IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");
    var nodes = new[]
    {
        new SkillLoadoutNode(sourceSet, trigger),
        new SkillLoadoutNode(targetSet),
    };

    RuntimeSkillDefinition runtime = SkillSetCompiler.Compile(nodes, 0, SkillStatSnapshot.Identity);

    Assert.That(runtime, Is.TypeOf<RuntimeProjectileDefinition>());
    var projectile = (RuntimeProjectileDefinition)runtime;
    Assert.That(projectile.ChildSpawnSetup, Is.Null);
    Assert.That(projectile.AoeIntervalSpawnSetup, Is.Not.Null);
}
```

This mirrors the already-existing `CompilerPopulatesAoeIntervalScatterRadius`
(~474) and `CompilerPopulatesProjectileIntervalSetupOnLingeringAoeSource` (~498)
— those two already exercise a single trigger against its "correct" effect
type and just need the type-name substitution; this new/rewritten test is the
one that specifically covers the case that used to be a warning and now isn't.

**`ValidatorWarnsWhenIntervalSpawnSourceIsPulseAoe`** (line ~566) and
**`ValidatorWarnsWhenAoeIntervalSpawnSourceIsPulseAoe`** (line ~582) both
assert `HasWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerSource,
0, "Pulse AOEs have no duration")`. Per [004](./004-update-validator.md), the
bespoke `ValidateIntervalSpawnSource` message is gone — the generic
`UnsupportedTriggerSource` message now reads `"...expects projectile or
lingering AOE source..."` via the new `Format(Interval)` case, and severity is
now `Error`, not `Warning`. Since these two tests are otherwise identical
scenarios (only trigger construction differs, and both collapse to the same
`IntervalSpawnTrigger` type post-merge), collapse them into one test and
update both the message substring and the severity assertion:

```csharp
[Test]
public void ValidatorErrorsWhenIntervalSpawnSourceIsPulseAoe()
{
    AoeSkill sourceSkill = CreateAsset<AoeSkill>("Pulse AOE Skill");
    ProjectileSkill targetSkill = CreateAsset<ProjectileSkill>("Projectile Skill");
    SkillSet sourceSet = CreateSkillSet("Pulse AOE Set", sourceSkill);
    SkillSet targetSet = CreateSkillSet("Projectile Set", targetSkill);
    IntervalSpawnTrigger trigger = CreateAsset<IntervalSpawnTrigger>("Interval Spawn");

    SkillValidationWarning[] warnings = Validate(
        new SkillLoadoutNode(sourceSet, trigger),
        new SkillLoadoutNode(targetSet));

    SkillValidationWarning warning = warnings.Single(w =>
        w.Code == SkillValidationWarningCode.UnsupportedTriggerSource && w.SlotIndex == 0);
    Assert.That(warning.Message, Does.Contain("projectile or lingering AOE"));
    Assert.That(warning.Severity, Is.EqualTo(SkillValidationSeverity.Error));
}
```

(`HasWarning` doesn't expose severity — either extend it to take an expected
severity, or assert directly against the matched warning as above; either is
fine, pick whichever reads more consistently with the rest of the file. If
`System.Linq` isn't already imported in this file, add it or use a manual loop
matching `HasWarning`'s existing pattern instead of `.Single(...)`.)

If keeping two separate tests is preferred for parity with the "one test per
trigger flavor" style used elsewhere in this file, that's fine too — the
important content change is the message substring and the severity
assertion, not the test count.

**`CompilerLeavesPulseAoeIntervalSourceAsNoOp`** (line ~544) needs only the
mechanical type substitution — the compiler's own no-op guard
(`parent is RuntimeAoeDefinition { LifetimeSeconds: <= 0f }`) is untouched by
this plan and still produces `ChildSpawnSetup == null` /
`AoeIntervalSpawnSetup == null` regardless of validation severity, since
validation `Error` severity is advisory only (nothing currently reads
`.Severity` to gate `SkillSetCompiler.Compile` — confirmed by grep, no
existing call site outside `SkillLoadoutValidator.cs`/`SkillLoadoutCompiler.cs`
consumes it as a gate). This test's assertions don't need to change, just the
trigger type it constructs.

Search `SkillValidationEditModeTests.cs` for any other assertion keyed to
`UnsupportedTriggerTarget`/`UnsupportedTriggerSource` involving an interval
trigger before starting — the ones above are what was found during planning,
but re-check after 001-004 land in case grep missed a variant.

## Acceptance Criteria

- All five test files compile against the merged `IntervalSpawnTrigger`.
- No test still expects an interval-spawn trigger to warn or no-op purely
  because it was "the wrong variant" for its effect's type.
- The rewritten target-mismatch test explicitly covers a source/effect
  combination that used to be blocked and now isn't, per the new
  dispatch-on-compiled-type behavior from [003](./003-collapse-compiler-dispatch.md).
- The pulse-AOE source test(s) assert `Error` severity and the new generic
  message text, per [004](./004-update-validator.md).

## Dependencies

Requires [001](./001-add-interval-tag.md), [002](./002-merge-trigger-class.md),
[003](./003-collapse-compiler-dispatch.md), and
[004](./004-update-validator.md).

## Scope/Complexity

Medium. Mostly mechanical, but three tests (the target-mismatch rewrite and
the two pulse-AOE source tests) need care to assert the *new* correct
behavior rather than just deleting coverage.

## Test Execution

Per project rules, I do not run these tests. After this task, hand back the
exact command for the user to run and export XML results, e.g.:

```
-testResults "<project-path>/TestResults/unify-interval-spawn-trigger-results.xml"
```

covering both the EditMode and PlayMode suites touched here.
