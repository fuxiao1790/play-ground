# 003 — PlayerRoot consumes the sheet

## Change
`Assets/Scripts/Player/PlayerRoot.cs` reads vitals + movement from a `UnitStatSheet` instead of its
own duplicated serialized fields.

- Add `[SerializeField] private UnitStatSheet statSheet;`.
- **Remove** the duplicated serialized `maxHealth` and `moveSpeed` fields. **Keep** player-specific
  fields that are not generic combat stats: dash (`dashSpeed`/`dashDuration`/`dashCooldown`),
  `stopThreshold`, accel/friction multipliers, `hurtFlashSeconds`, `targetRadius`.
- `Awake`: fail-fast validate `statSheet != null` (same style as the existing reference checks).
- `CombatMaxHealth => statSheet.MaxHealth`.
- Construct `PlayerMovement` with `statSheet.MoveSpeed` (in place of the removed field).
- Construct `PlayerHealth(..., statSheet.MaxHealth, hurtFlashSeconds)`.

## Ownership / constraints honored
- GameObject still owns **max** health and seeds ECS at registration (path unchanged); only the value
  source moves from a field to `statSheet.MaxHealth`.
- ECS remains the owner of **current** health; `PlayerHealth` + `ReceiveCombatTick` mirror untouched.
- Dash / feel tuning stays on `PlayerRoot` — the sheet holds only stats generic to all units.

## Acceptance criteria
- Compiles.
- Player seeded `TargetHealth.Max` matches `statSheet.MaxHealth`; damage still mirrors back via ECS.
- Player move speed matches `statSheet.MoveSpeed`.
- No remaining references to the removed `maxHealth`/`moveSpeed` fields.

## Dependencies
001 (type). Pairs with 002 for the player's offensive stats. 005 assigns the asset.

## Scope
Small–medium — one file; remove 2 fields, add 1, redirect 3 usages.
