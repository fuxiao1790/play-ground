# Wizard Player Sprite + 4-Direction Intercardinal Facing

## Summary

Replace the placeholder player sprite (`Assets/Shooter/Players/Tiles/tile_0000.png`) with the blue (top) row of `Assets/Sprite/Actors/wizard.png` and support 4 intercardinal facings (up-left, up-right, down-left, down-right). Facing stays aim-driven (mouse cursor); only the bucketing changes from 2 sides (`flipX`) to 4 diagonal quadrants with per-direction sprites. One frame per direction → sprite-swap only, no Animator (none exists in the project; `PlayerAnimatorDriver` is a null-guarded no-op).

Sheet facts: 1254×1254, 4×4 grid (~313.5px cells, transparent gutters). Rows top→bottom: blue, red, green, purple variants. Columns 1-2 back views (up diagonals), 3-4 front views (down diagonals). Left/right frames hand-drawn distinct — real frames, no mirroring.

Division of labor: Claude writes code (001-003); the user performs all Unity-editor work (004).

## Constraints & invariants (source-checked)

- **Root Component Rule** (`Docs/coding-standards.md`): `PlayerRoot` holds serialized refs and constructs sealed helpers; roots must not choose presentation state inline → facing logic lives in the `PlayerFacing` helper.
- **Fail-fast Awake validation** (`Docs/coding-standards.md`; pattern at `PlayerRoot.cs:84-103`): new serialized `facingSet` ref throws in `Awake` if missing/incomplete.
- **ScriptableObject Rule** (`Docs/coding-standards.md`): reusable authored data tables live as SOs under `Assets/ScriptableObjects/`.
- **Layer rules** (`Docs/layers/scene-and-authoring.md`): SpriteRenderer presentation is scene/authoring-layer work — no ECS involvement. Combat-atlas `ComputeUvBasis` Tight-mesh distortion applies only to the projectile/AOE batch path, not `SpriteRenderer`.
- **Public contract**: `PlayerFacing.AimDirection` feeds `SkillDriver` via `PlayerRoot.cs:174` — semantics unchanged (projectile aim unaffected).
- **Per-frame budget**: facing runs every `Update` — reassign `spriteRenderer.sprite` only on bucket change, zero allocations.

## Mechanisms reused vs introduced

- **Reused**: `PlayerFacing` helper (extended in place), PlayerRoot serialized-ref + Awake-construction pattern, SO data-table pattern (like `UnitStatSheet`), plain SpriteRenderer render path, editor-native sprite slicing.
- **Introduced**: `SpriteFacingSet` SO (4 Sprite fields). Justified: per-skin data table — other color rows become new assets with zero code; 4 Sprite fields on `PlayerRoot` would bloat the root and not be reusable.
- **Deliberately not introduced**: Animator/clips; any change to `MobRoot` (its own flipX at `MobRoot.cs:451` stays).

## Additive vs refactor comparison

- Minimal/additive approach (new `PlayerDirectionalSprite` helper beside `PlayerFacing`):
  - resulting data flow: aim → PlayerFacing (flipX) AND aim → new helper (sprite) — two writers of orientation
  - new concepts/types introduced: second facing owner with duplicated bucket state
  - copies/translations added: duplicated aim-quadrant computation
  - long-term cost: unclear source of truth for facing; flipX path lingers dead
- Refactor approach (rewrite `PlayerFacing` itself):
  - resulting data flow: aim → quadrant → sprite, single path
  - existing concepts/types changed: `PlayerFacing` constructor + body; flipX path deleted
  - copies/translations removed: none added, flipX write removed
  - long-term benefit: one owner of facing; public surface unchanged
- Decision: **refactor**. Reason: single source of truth; additive variant trips the duplicate-ownership structural warning.

## Design validation

- Root rule: bucketing + sprite choice entirely inside `PlayerFacing`; `PlayerRoot` only passes the SO ref. ✓
- Fail-fast: `facingSet == null || !facingSet.HasAllSprites` throws in Awake. ✓
- Contract: `AimDirection` computation untouched — same normalize + degenerate-vector early-return. ✓
- Budget: sprite assigned only when quadrant flips; hysteresis deadzone (0.05 on normalized aim ≈ ±2.9°) prevents flicker near vertical aim; no allocations. ✓
- Hurt flash (`PlayerAnimatorDriver`/`PlayerHealth` write `spriteRenderer.color`/`enabled`) is orthogonal to `sprite` — unaffected. ✓

## Direction mapping (working guess — verified in Play mode, 004-D)

| sprite (blue row) | view | direction |
|---|---|---|
| wizard_0 (col 1) | back | up-right |
| wizard_1 (col 2) | back | up-left |
| wizard_2 (col 3) | front | **down-right (default)** |
| wizard_3 (col 4) | front | down-left |

If left/right prove swapped: swap `upLeft`↔`upRight` and `downLeft`↔`downRight` in the SO asset inspector and set the prefab default sprite to `wizard_3`. Data-only fix.

## Tasks

- [001-sprite-facing-set.md](./001-sprite-facing-set.md) — new `SpriteFacingSet` ScriptableObject script (Claude)
- [002-player-facing-4dir.md](./002-player-facing-4dir.md) — rewrite `PlayerFacing` for 4-quadrant bucketing (Claude)
- [003-player-root-wiring.md](./003-player-root-wiring.md) — `PlayerRoot` field, validation, constructor wiring (Claude)
- [004-editor-steps.md](./004-editor-steps.md) — slicing, asset creation, prefab wiring, verification (USER, in Unity editor)

## Open questions / notes

- Direction mapping is a visual guess — contained by design (data-only swap, 004-D).
- Blue row only for now; all 16 cells get sliced — future skins = new `SpriteFacingSet` assets, zero code.
- 1254/4 = 313.5px: editor grid slicing handles the remainder; transparent gutters make the half-pixel invisible.
- PPU 400 baseline sizes the wizard ≈ current player (~2 world units incl. prefab scale 2 × Visual 1.5); tune in importer if off.
