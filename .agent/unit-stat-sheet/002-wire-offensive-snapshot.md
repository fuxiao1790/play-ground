# 002 — Wire offensive snapshot to the sheet

## Change
Make the caster's `UnitStatSheet` the source of the offensive `SkillStatSnapshot`, replacing the
Identity stub.

### `Assets/Scripts/Skills/SkillStatSnapshot.cs`
Change the aggregator to read a sheet (keep `loadout` for the future item/buff/level path named in
the skill-system doc — unused today):

```csharp
public static SkillStatSnapshot Aggregate(SkillLoadout loadout, UnitStatSheet sheet) =>
    sheet == null
        ? SkillStatSnapshot.Identity
        : new SkillStatSnapshot(
            sheet.IncreasedRatePercent, sheet.DamageMultiplier,
            sheet.CritChance, sheet.CritMultiplier, sheet.AreaSizeMultiplier);
```

### `Assets/Scripts/Skills/SkillDriver.cs`
- Add `[SerializeField] private UnitStatSheet statSheet;` (the caster's sheet; `SkillDriver` is the
  generic caster component — player now, mobs later).
- At the call site in `CompileAndRegister()` (~line 101):
  `SkillStatSnapshot snapshot = SkillStatAggregator.Aggregate(loadout, statSheet);`

## Ownership / constraints honored
- `SkillStatSnapshot` shape and the `StatModifierAccumulator` fold are **unchanged** — fields map 1:1,
  so `SkillSetCompiler` needs no edits (no second offensive data path).
- Aggregation still runs only from `CompileAndRegister` (equip/stat change), never per-frame.
- Null sheet → `Identity`, so a driver without a sheet remains a valid no-augment caster.

## Acceptance criteria
- Compiles; the one existing caller is updated.
- With a sheet whose `damageMultiplier`/`critChance` differ from Identity, compiled runtime defs
  reflect the sheet (verified in PlayMode, see 005).
- With no sheet assigned, behavior is identical to the current Identity baseline.

## Dependencies
001 (needs `UnitStatSheet`).

## Scope
Small — two files, signature + one call site + one serialized field.
