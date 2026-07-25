# 003 — Interval-spawn trigger conversion; compiler uses folded child mana cost

## Goal
Put the **mana → energy conversion function on the trigger link** (user
directive) and make the compiler derive `EnergyThreshold` from the *compiled*
child's folded `ManaCost` instead of the raw SO `spawnEnergyCost`.

## Design
Introduce an abstract base for the two interval triggers that hoists their
duplicated energy fields and owns the conversion:

```csharp
public abstract class IntervalSpawnTrigger : TriggerLink
{
    [Min(0.01f)] public float energyPerSecond = 2f;
    [FormerlySerializedAs("intervalJitterPercent")]
    [Range(0f, 100f)] public float energyJitterPercent;
    [Min(0.0001f)] public float manaToEnergyRatio = 1f;

    // Conversion function lives on the trigger link.
    public float ManaToEnergyCost(float manaCost) =>
        Mathf.Max(1e-3f, manaCost * manaToEnergyRatio);
}
```

- `ProjectileIntervalSpawnTrigger` and `AoeIntervalSpawnTrigger` extend
  `IntervalSpawnTrigger`, dropping their local `energyPerSecond` /
  `energyJitterPercent` (now inherited) and keeping their own burst fields
  (`projectileCount`/`sideSpreadDegrees`, `echoCount`/`scatterRadius`) and tag
  overrides. Field names are unchanged, so existing trigger assets keep their
  serialized values (Unity serializes SO fields by name regardless of declaring
  class). `manaToEnergyRatio` defaults to `1f` → identity conversion, preserving
  current numeric behavior when mana cost equals the old `spawnEnergyCost`.

## Changes

1. **New `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs`** — base class
   above.
2. **`ProjectileIntervalSpawnTrigger.cs`** — extend `IntervalSpawnTrigger`;
   remove the two hoisted fields.
3. **`AoeIntervalSpawnTrigger.cs`** — same.
4. **`SkillSetCompiler.cs`**
   - `ApplyChildSpawn` (L332): replace
     `float energyThreshold = Mathf.Max(1e-3f, childSkillDefinition.spawnEnergyCost);`
     with `float energyThreshold = trigger.ManaToEnergyCost(childDef.ManaCost);`
     where `childDef` is the already-compiled `RuntimeProjectileDefinition`. The
     raw `childSkillDefinition` fetch (the `GetSkillSet(...).Definition is not
     ProjectileDefinition` guard used only for the cost read) can be dropped;
     `compiledChild is not RuntimeProjectileDefinition` already guards type.
   - `ApplyAoeIntervalSpawn` (L371): same, using the compiled
     `RuntimeAoeDefinition childDef` and `childDef.ManaCost`.
   - `EnergyThresholdJitter` keeps its formula but multiplies the new
     `energyThreshold`.

## Acceptance Criteria
- Existing EditMode tests updated:
  - `SkillValidationEditModeTests.CompilerMapsProjectileEnergyRateCostAndThresholdJitter`
    (L88) — set `((ProjectileDefinition)targetSkill.Definition).manaCost = 4f`;
    with `manaToEnergyRatio = 1`, `EnergyThreshold` still `4f`,
    `EnergyThresholdJitter` still `1f`.
  - `SkillValidationEditModeTests.CompilerMapsAoeEnergyRateCostAndThresholdJitter`
    (L115) — same with `manaCost = 3f`.
- New test: a child set with a mana-cost support (from 002) yields a proportional
  increase in `ChildSpawnSetup.EnergyThreshold`.
- New test: a trigger with `manaToEnergyRatio = 2` doubles the resulting
  `EnergyThreshold` for the same child mana cost.
- `ProjectileSpawnPipelineTests` is unaffected — its `spawnEnergyCost` is a local
  test-helper parameter that already sets `EnergyThreshold` directly, not the
  production field.

## Dependencies
Depends on 001 (folded `ManaCost` on runtime defs). Best landed with 002 so the
support-driven threshold change is demonstrable.

## Scope
Medium (type hierarchy touch + compiler edit + tests).
