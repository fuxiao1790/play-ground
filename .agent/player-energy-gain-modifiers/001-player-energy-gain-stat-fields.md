---
name: player-energy-gain-stat-fields
description: Add base/increased/multiplying energy-gain fields to UnitStatSheet and thread them through SkillStatSnapshot/SkillStatAggregator
---

# 001 — Player Energy Gain Stat Fields

## Scope

Add the three new player-level energy-gain terms to `UnitStatSheet`, carry
them through `SkillStatSnapshot`, and wire `SkillStatAggregator.Aggregate` to
read them. No trigger/compiler changes in this task — see
[002-interval-trigger-energy-gain-fold.md](002-interval-trigger-energy-gain-fold.md).

## Changes

### `Assets/Scripts/Common/Stats/UnitStatSheet.cs`

Add a new header/field group after the existing `Offense` block
([UnitStatSheet.cs:17-23](../../Assets/Scripts/Common/Stats/UnitStatSheet.cs#L17-L23)):

```csharp
[Header("Duration Skill Energy")]
[SerializeField, Min(0f)] private float baseEnergyGain = 0f;
[SerializeField, Min(0f), Tooltip("Authored as percent points. 15 means +15% energy gain.")]
private float increasedEnergyGainPercent = 0f;
[SerializeField] private float energyGainMultiplier = 1f;
```

Add matching accessor properties next to the existing `IncreasedRatePercent`
et al. ([UnitStatSheet.cs:30](../../Assets/Scripts/Common/Stats/UnitStatSheet.cs#L30)),
following that exact clamp convention:

```csharp
public float BaseEnergyGain => Mathf.Max(0f, baseEnergyGain);
public float IncreasedEnergyGainPercent => Mathf.Max(0f, increasedEnergyGainPercent) * 0.01f;
public float EnergyGainMultiplier => Mathf.Max(0f, energyGainMultiplier);
```

`energyGainMultiplier`'s field initializer must be `1f` (not left at C#'s
`0f` default) — a mob `UnitStatSheet` created via
`ScriptableObject.CreateInstance<UnitStatSheet>()`
([MobRoot.cs:177](../../Assets/Scripts/Mob/MobRoot.cs#L177)) never calls a
constructor, so Unity's serialized-field default applies. Match the existing
`damageMultiplier`/`areaSizeMultiplier` field-initializer pattern exactly
([UnitStatSheet.cs:20](../../Assets/Scripts/Common/Stats/UnitStatSheet.cs#L20),
[:23](../../Assets/Scripts/Common/Stats/UnitStatSheet.cs#L23)) so this is
correct by construction, not by runtime guard.

### `Assets/Scripts/Skills/SkillStatSnapshot.cs`

Add 3 trailing constructor parameters with neutral defaults (after the
existing `areaSizeMultiplier = 1f` parameter
[SkillStatSnapshot.cs:14](../../Assets/Scripts/Skills/SkillStatSnapshot.cs#L14)):

```csharp
float baseEnergyGain = 0f,
float increasedEnergyGainPercent = 0f,
float energyGainMultiplier = 1f)
```

Assign to 3 new public properties (`BaseEnergyGain`, `IncreasedEnergyGainPercent`,
`EnergyGainMultiplier`), same style as the existing 5.

Do **not** change `SkillStatSnapshot.Identity`'s constructor call
([SkillStatSnapshot.cs:7](../../Assets/Scripts/Skills/SkillStatSnapshot.cs#L7))
— the new trailing parameters' defaults already produce the neutral values
`Identity` needs, so the existing 5-arg call keeps working unmodified.

### `SkillStatAggregator.Aggregate`

([SkillStatSnapshot.cs:32-40](../../Assets/Scripts/Skills/SkillStatSnapshot.cs#L32-L40))
Add the 3 new arguments reading from `sheet`:

```csharp
sheet.BaseEnergyGain,
sheet.IncreasedEnergyGainPercent,
sheet.EnergyGainMultiplier);
```

## Acceptance Criteria

- `UnitStatSheet` compiles with the 3 new serialized fields and accessor
  properties; existing fields/properties untouched.
- `SkillStatSnapshot`'s existing 5-arg positional/named constructor call
  sites across the codebase (tests) compile unmodified — verify by grepping
  `new SkillStatSnapshot(` and confirming no call site needs edits.
- `SkillStatAggregator.Aggregate(null, sheet)` — the `sheet == null` branch
  ([SkillStatSnapshot.cs:33-34](../../Assets/Scripts/Skills/SkillStatSnapshot.cs#L33-L34)) —
  still returns `SkillStatSnapshot.Identity` unchanged.
- A freshly-created `UnitStatSheet` (both the default-constructed editor
  asset case and `ScriptableObject.CreateInstance` mob case) resolves
  `BaseEnergyGain == 0f`, `IncreasedEnergyGainPercent == 0f`,
  `EnergyGainMultiplier == 1f`.

## Dependencies

None — this is the base task.
