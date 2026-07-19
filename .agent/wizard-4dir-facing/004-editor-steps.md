# 004 — Unity editor steps (USER)

**Dependencies**: 001-003 merged and compiling (open Unity, wait for compile — the Create menu and PlayerRoot field only exist after compile).
**Scope**: ~10 minutes in the editor.

## A. Slice the sheet

1. Select `Assets/Sprite/Actors/wizard.png` in the Project window.
2. Inspector: set **Pixels Per Unit = 400** (Sprite Mode stays Multiple, Filter Mode stays Bilinear). Click **Apply**.
3. Click **Open Sprite Editor** → top-left **Slice** dropdown → Type: **Grid By Cell Count**, Column & Row: **4 × 4**, Pivot: **Center** → **Slice** → **Apply** (top-right), close.
4. Expand `wizard.png` in Project: should show `wizard_0`…`wizard_15`; `wizard_0`-`wizard_3` are the blue row (2 backs, 2 fronts).

## B. Create the facing asset

1. In `Assets/ScriptableObjects/Player/`: right-click → Create → Folder → name it `Facing`.
2. Inside it: right-click → **Create → PlayGround → Player → Sprite Facing Set**, name it `WizardBlueFacing`.
3. Assign: **Up Left = `wizard_1`**, **Up Right = `wizard_0`**, **Down Left = `wizard_3`**, **Down Right = `wizard_2`**.

## C. Wire the prefab

Do this **before entering Play mode** — PlayerRoot now fail-fasts on a missing facing set.

1. Open `Assets/Prefabs/Player/Player.prefab` (double-click).
2. Select the **Visual** child → SpriteRenderer → **Sprite = `wizard_2`**.
3. Select the prefab root → **PlayerRoot** component → **Facing Set = `WizardBlueFacing`**.
4. Save (Ctrl+S), exit prefab mode.

## D. Verify (Play mode, `BenchmarkLarge` scene)

- [ ] No console errors on import/compile/play.
- [ ] Aim mouse into each quadrant around the player → sprite faces that diagonal (backs when aiming up, fronts when aiming down).
      If left/right feel swapped → swap the two Up fields and the two Down fields in `WizardBlueFacing`, set prefab sprite to `wizard_3`.
- [ ] Sweep mouse slowly through straight-up / straight-down → no left/right flicker.
- [ ] On-screen size ≈ old player (~2 world units). Off → adjust Pixels Per Unit on `wizard.png` (400 baseline; higher = smaller).
- [ ] Take a hit → hurt flash still works; facing keeps updating.
- [ ] Mobs and projectile aim behave exactly as before.

## Acceptance criteria

All D checkboxes pass; direction mapping confirmed or corrected via the data-only swap.
