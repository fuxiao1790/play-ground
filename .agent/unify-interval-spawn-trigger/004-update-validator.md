# 004 — Update SkillLoadoutValidator: fold pulse-AOE rejection into the generic tag check, make it Error

## Scope

`Assets/Scripts/Skills/SkillLoadoutValidator.cs`,
`Assets/Scripts/Skills/SkillDefinitionTags.cs`

## Change

### Remove the ad-hoc pulse-AOE check, rely on the `Interval` tag instead

With `IntervalSpawnTrigger.SourceSkillTags = Interval` (from
[002](./002-merge-trigger-class.md)) and `Interval` only present on
`ProjectileSkill`/`LingeringAoeSkill` (from [001](./001-add-interval-tag.md)),
the generic tag-mismatch check at the bottom of `ValidateTriggerLink` already
correctly rejects a pulse `AoeSkill` source (tagged `Aoe` only) — it no longer
needs the separate `ValidateIntervalSpawnSource` method to catch that case via
`Definition`-type inspection. Delete:

- `ValidateIntervalSpawnSource` (the whole method).
- Its call site in `ValidateTriggerLink`:
  ```csharp
  if (IsIntervalSpawnTrigger(link))
      ValidateIntervalSpawnSource(link, slotIndex, causeSkill, warnings);
  ```
- `IsIntervalSpawnTrigger` — only used by the call site just removed; confirm
  no other reference remains before deleting the method itself.

### Make the generic source-tag-mismatch check Error for `IntervalSpawnTrigger`

The generic check:

```csharp
if (!SkillDefinitionTagUtility.HasAny(causeSkill.Tags, link.SourceSkillTags))
{
    AddWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerSource, slotIndex,
        $"Trigger '{link.name}' expects {SkillDefinitionTagUtility.Format(link.SourceSkillTags)} source, but source skill '{causeSkill.name}' is {SkillDefinitionTagUtility.Format(causeSkill.Tags)}. Link will do nothing.");
}
```

becomes severity-aware, Error only for the interval trigger (every other
trigger type's source-tag mismatch stays Warning — this is not a general
severity change):

```csharp
if (!SkillDefinitionTagUtility.HasAny(causeSkill.Tags, link.SourceSkillTags))
{
    SkillValidationSeverity severity = link is IntervalSpawnTrigger
        ? SkillValidationSeverity.Error
        : SkillValidationSeverity.Warning;
    AddWarning(warnings, SkillValidationWarningCode.UnsupportedTriggerSource, slotIndex,
        $"Trigger '{link.name}' expects {SkillDefinitionTagUtility.Format(link.SourceSkillTags)} source, but source skill '{causeSkill.name}' is {SkillDefinitionTagUtility.Format(causeSkill.Tags)}. Link will do nothing.",
        severity);
}
```

### Give `Interval` a readable `Format`

`SkillDefinitionTagUtility.Format` only special-cases `None` and `Any` today;
everything else falls through to `tags.ToString()`, which would render the
message above as `"...expects Interval source, but source skill 'Pulse AOE
Skill' is Aoe..."` — technically correct but not designer-friendly. Add a case
so the generic message reads naturally without needing a bespoke message
string:

```csharp
public static string Format(SkillDefinitionTags tags)
{
    if (tags == SkillDefinitionTags.None) return "none";
    if (tags == SkillDefinitionTags.Any) return "projectile, AOE, or targeted";
    if (tags == SkillDefinitionTags.Interval) return "projectile or lingering AOE";
    return tags.ToString();
}
```

With this, the generic message for a pulse-AOE source reads: `"Trigger 'X'
expects projectile or lingering AOE source, but source skill 'Pulse AOE
Skill' is Aoe. Link will do nothing."` This is the new message text — the
previous bespoke wording ("Pulse AOEs have no duration to tick...") is gone.
See [005](./005-update-tests.md) for the two tests whose message-substring
assertions need updating to match.

### `ValidateTargetedIntervalEnergyReachability` type update

Still needs updating from `TargetedIntervalSpawnTrigger` to
`IntervalSpawnTrigger` per [002](./002-merge-trigger-class.md):

```csharp
if (link is IntervalSpawnTrigger intervalTrigger
    && effectSkill.Definition is TargetedDefinition targetedEffect)
{
    ValidateTargetedIntervalEnergyReachability(
        intervalTrigger, causeSkill.Definition, targetedEffect, slotIndex, warnings);
}
```

and the method's parameter type changes from `TargetedIntervalSpawnTrigger
trigger` to `IntervalSpawnTrigger trigger` (body unchanged — both methods it
calls are inherited base-class methods already).

No other validator logic changes — the generic target-tag-mismatch check
(`UnsupportedTriggerTarget`) is untouched and simply never fires for this
trigger now that `TargetSkillTags = Any`.

## Acceptance Criteria

- `SkillLoadoutValidator` compiles with no references to the deleted
  subclasses, `ValidateIntervalSpawnSource`, or `IsIntervalSpawnTrigger`.
- A pulse-AOE source now produces `UnsupportedTriggerSource` at
  `SkillValidationSeverity.Error` (previously `Warning`, via the separate
  ad-hoc check that no longer exists).
- A `TargetedSkill` used as an interval-trigger source (already rejected
  before this task, since `Targeted` never overlapped `Projectile | Aoe`)
  continues to be rejected, now also at Error severity, through the same
  single code path as the pulse-AOE case — one mechanism handles both wrong-
  source cases instead of two.
- Every other trigger type's `UnsupportedTriggerSource`/`UnsupportedTriggerTarget`
  warnings are unaffected — still `Warning` severity, unchanged messages.
- A targeted effect whose source can't accrue enough energy over its lifetime
  still produces `TargetedIntervalWarning` (unchanged behavior, just reached
  through the merged type).

## Dependencies

Requires [001](./001-add-interval-tag.md) and
[002](./002-merge-trigger-class.md). Independent of
[003](./003-collapse-compiler-dispatch.md) (can be done in either order or in
parallel), but both must land before [005](./005-update-tests.md).

## Scope/Complexity

Small–medium. Deletes one method and one helper, adds one severity branch and
one `Format` case, updates one method signature.
