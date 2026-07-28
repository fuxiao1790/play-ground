---
name: trigger-link-mana-cost-fold
description: Add manaCostIncreasedPercent to TriggerLink and fold it with manaCostMultiplier at every existing trigger-cost call site
---

# 002 — Trigger Link Mana Cost Fold

## Depends On

None. Independent of 001 (touches different files).

## Scope

Extend `TriggerLink` with one new field and fold it into the three places
that currently read `manaCostMultiplier` alone. Rename the runtime field that
stores the resolved per-edge factor so it no longer implies "just a
multiplier."

## Files

- `Assets/Scripts/Skills/Trigger/TriggerLink.cs`
- `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeSkillDefinition.cs`

## Implementation

### `TriggerLink.cs`

Add the new field right next to `manaCostMultiplier`
([TriggerLink.cs:38-43](../../Assets/Scripts/Skills/Trigger/TriggerLink.cs#L38-L43)):

```csharp
// One multiplier for this link: interval-child energy, the initial
// active skill chain cost, and this link's triggered skill cost.
[FormerlySerializedAs("manaToEnergyCostMultiplier")]
[FormerlySerializedAs("triggerLinkManaCostMultiplier")]
[FormerlySerializedAs("manaToEnergyRatio")]
[Min(0f)] public float manaCostMultiplier = 1f;

// Applied before manaCostMultiplier: resolvedCost = childCost * (1 + manaCostIncreasedPercent) * manaCostMultiplier.
[Min(0f)] public float manaCostIncreasedPercent = 0f;
```

Add a small internal helper so the `(1 + increased) * multiplier` formula
lives in exactly one place instead of being copy-pasted at every call site:

```csharp
public float ResolveManaCostFactor() => (1f + manaCostIncreasedPercent) * manaCostMultiplier;
```

### `IntervalSpawnTrigger.cs`

```csharp
public float ManaToEnergyCost(float manaCost) =>
    Mathf.Max(1e-3f, manaCost * ResolveManaCostFactor());
```

(was `manaCost * manaCostMultiplier` at
[IntervalSpawnTrigger.cs:10-11](../../Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs#L10-L11))

### `RuntimeSkillDefinition.cs`

Rename the field
([RuntimeSkillDefinition.cs:19](../../Assets/Scripts/Skills/Runtime/RuntimeSkillDefinition.cs#L19)):

```csharp
// Set only when this definition is attached through a valid trigger link.
// Combines the link's manaCostIncreasedPercent and manaCostMultiplier.
// Feeds the initial active skill's one-time mana calculation.
public float IncomingManaCostFactor { get; set; } = 1f;
```

### `SkillSetCompiler.cs`

Rename every `IncomingManaCostMultiplier` reference to `IncomingManaCostFactor`
and change what gets assigned in
`ApplyIncomingTriggerManaCostMultiplier`
([SkillSetCompiler.cs:447-456](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L447-L456)):

```csharp
private static void ApplyIncomingTriggerManaCostMultiplier(
    RuntimeSkillDefinition triggeredDefinition,
    TriggerLink triggerLink)
{
    if (triggeredDefinition == null || triggerLink == null)
        return;

    triggeredDefinition.IncomingManaCostFactor =
        Mathf.Max(0f, triggerLink.ResolveManaCostFactor());
}
```

`GetManaCostMultiplier`
([SkillSetCompiler.cs:476-514](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L476-L514))
and `SumTriggeredSkillManaCosts`
([SkillSetCompiler.cs:516-557](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L516-L557))
only need the property rename — their multiplication logic (`multiplier *
GetManaCostMultiplier(..., includeCurrent: true)` composition down the tree)
is unchanged, since `IncomingManaCostFactor` is still a single resolved
`float` per definition, just computed from two authored fields instead of one.
Consider renaming `GetManaCostMultiplier` → `GetManaCostFactor` for the same
clarity reason, but only if it doesn't bloat the diff — not required for
correctness.

## Acceptance Criteria

- `manaCostIncreasedPercent` defaults to `0f` on every existing `TriggerLink`
  asset (new serialized field, no `FormerlySerializedAs` needed — it has no
  prior name).
- With `manaCostIncreasedPercent = 0`, every existing
  `SkillValidationEditModeTests` assertion referencing `manaCostMultiplier`
  ([SkillValidationEditModeTests.cs:114-146](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L114-L146),
  [:229-251](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L229-L251))
  still passes unmodified — `ResolveManaCostFactor()` reduces to
  `manaCostMultiplier` exactly.
- No remaining reference to `IncomingManaCostMultiplier` anywhere in
  `Assets/Scripts/` (renamed, not duplicated).
- `manaCostMultiplier = 2f, manaCostIncreasedPercent = 0.5f` on a trigger
  whose child resolves to `ManaCost = 4` yields energy threshold /
  triggered-cost contribution of `4 * (1 + 0.5) * 2 = 12`, not `4 * 2 = 8`.
  Verified in task 003.

## Estimated Scope

Small-to-medium — one new field, one small helper method, a rename touching
~6 call sites across 2 files. No new types, no change to trigger tree
composition logic.
