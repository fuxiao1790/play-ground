# 002 — Increased Rate support

## Goal
Rename the recovery-speed support to a rate support authored as an increased percent.

## Changes

### Rename `Assets/Scripts/Skills/Support/IncreasedRecoverySpeedSupport.cs` → `IncreasedRateSupport.cs`
Preserve the script GUID: rename the `.cs` **and** its `.cs.meta` together (keep the
same `guid:` in the `.meta`). ScriptableObject assets reference the script by GUID,
not by class name, so the support `.asset` keeps resolving after the class rename
(there is exactly one class in the file).

New content:

```csharp
using PlayGround.Skills.Modifiers;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Increased Skill Speed", fileName = "IncreasedRateSupport")]
    public sealed class IncreasedRateSupport : StatModifierSupport, IIncreasedModifier
    {
        [SerializeField] private float increasedRatePercent = 0.5f; // 0.5 = +50% rate

        public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;

        public void CollectIncreases(IncreasedSink sink)
        {
            sink.Add(SkillStat.Rate, increasedRatePercent);
        }
    }
}
```

Notes:
- Field switches from a multiplier (`recoverySpeedMultiplier = 1.5`, contributed as
  `mul - 1`) to a direct increased percent (`increasedRatePercent = 0.5`), matching
  the "scale a base amount by a percentage" instruction. The fold already sums
  increases across supports, so stacking is unchanged.
- Menu label "Increased Skill Speed"; `fileName` matches the class.

## Acceptance
- Class `IncreasedRateSupport` contributes `increasedRatePercent` to `SkillStat.Rate`
  via `IIncreasedModifier`.
- Script GUID unchanged; existing support `.asset` still binds (value migrated in 004).

## Dependencies
- 001 (needs `SkillStat.Rate`).

## Scope
Small. One renamed file (+ its `.meta`).
