# 001 — UnitStatSheet type

## Change
Create the authored ScriptableObject that holds a unit's combat stats.

- File: `Assets/Scripts/Common/Stats/UnitStatSheet.cs`
- Namespace: `PlayGround.Common.Stats`
- `[CreateAssetMenu(menuName = "PlayGround/Units/Unit Stat Sheet")]`

Fields (defaults equal `SkillStatSnapshot.Identity` for offense, so an all-default sheet is a no-op augment):

```csharp
[Header("Vitals")]        [SerializeField] float maxHealth = 100f;
[Header("Movement")]      [SerializeField] float moveSpeed = 5f;
[Header("Offense")]
[SerializeField] float increasedRatePercent = 0f;   // attack/cast speed, additive %
[SerializeField] float damageMultiplier     = 1f;
[SerializeField, Range(0f,1f)] float critChance = 0f;
[SerializeField] float critMultiplier       = 1.5f;
[SerializeField] float areaSizeMultiplier   = 1f;
```

Clamping getters: `MaxHealth => Max(1f, maxHealth)`, `MoveSpeed => Max(0f, moveSpeed)`,
`DamageMultiplier => Max(0f, ...)`, `CritChance => Clamp01(...)`, `CritMultiplier => Max(1f, ...)`,
`AreaSizeMultiplier => Max(0f, ...)`, `IncreasedRatePercent => increasedRatePercent`.

Add an **internal runtime-configuration** entry point for code-built units (used by 004):
`internal void SetRuntimeValues(float maxHealth, float moveSpeed)` that sets the two vitals fields.
This method is only ever called on a `ScriptableObject.CreateInstance` clone (never on an authored
asset) — matches the "runtime-created clone" carve-out in the ScriptableObject rule.

## Ownership / constraints honored
- Pure authored data type; no references to `PlayGround.Skills` (offense→snapshot conversion lives
  in the Skills layer, 002), keeping `Common` decoupled.
- Not named `CombatStats*` (avoids collision with diagnostics `CombatStatsSingleton`).

## Acceptance criteria
- Compiles; asset creatable via `Create ▸ PlayGround ▸ Units ▸ Unit Stat Sheet`.
- Getters clamp as specified.

## Dependencies
None. Precedes all other tasks.

## Scope
Small — one new file.
