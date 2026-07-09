# 004 — MobRoot consumes the sheet

## Change
`Assets/Scripts/Mob/MobRoot.cs` reads vitals + movement from a `UnitStatSheet`, and supports
code-built mobs via a runtime clone.

- Add `[SerializeField] private UnitStatSheet statSheet;`.
- **Remove** the duplicated serialized `maxHealth` and `speed` fields. **Keep** `targetRadius`
  (collision-shape fallback, not a combat stat) and the wander tuning fields.
- Redirect getters: `MaxHealth => statSheet.MaxHealth`, `Speed => statSheet.MoveSpeed`,
  `CombatMaxHealth => statSheet.MaxHealth`.
- `Awake`: seed `CurrentHealth = Mathf.Max(1f, statSheet.MaxHealth)`; fail-fast validate
  `statSheet != null` **after** `ConfigureAuthoring` has had a chance to run for code-built mobs
  (validation belongs where an authored-prefab mob would otherwise reach `Awake` with no sheet).
- `ConfigureAuthoring(float health, float moveSpeed, float radius)` (called by
  `MobSpawnerRoot.CreateRuntimeMobPrefab()` and mob tests, which have no authored asset): build a
  runtime clone —
  ```csharp
  statSheet = ScriptableObject.CreateInstance<UnitStatSheet>();
  statSheet.SetRuntimeValues(health, moveSpeed);   // from 001
  targetRadius = radius;
  ```
  This keeps "SO-only" true even for programmatic mobs and preserves existing tests.

## Ownership / constraints honored
- Runtime clone via `CreateInstance` is the ScriptableObject-rule "runtime-created clone" carve-out;
  authored assets are never mutated.
- Max health seeding into ECS and the current-health mirror path are unchanged.

## Acceptance criteria
- Compiles.
- Authored mob prefabs spawn with `statSheet.MaxHealth` / `MoveSpeed`.
- `MobSpawnerRoot.CreateRuntimeMobPrefab()` and existing mob PlayMode tests still pass (runtime-clone path).
- No remaining references to the removed `maxHealth`/`speed` fields.

## Dependencies
001 (type, incl. `SetRuntimeValues`). 005 assigns archetype assets.

## Scope
Small–medium — one file; remove 2 fields, add 1, redirect getters, adjust `ConfigureAuthoring`.
