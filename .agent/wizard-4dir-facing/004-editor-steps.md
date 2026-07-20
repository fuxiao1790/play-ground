# 004 — Unity editor steps (USER)

**Dependencies**: 001-003 merged and compiling (open Unity, wait for compile — the Create menu and PlayerRoot field only exist after compile).
**Status update 2026-07-19**: user split the sheet into per-color files and already sliced `wizard-blue.png` → `wizard-blue_0..3` (293×384, pivot bottom-center). Step A reduces to import settings only.

## A. Import settings on `wizard-blue.png`

1. Select `Assets/Sprite/Actors/wizard-blue.png`.
2. Inspector: set **Pixels Per Unit = 600** → **Apply**. (384px tall / 600 × 3 total prefab scale ≈ 1.9 world units — matches the old player height. Higher PPU = smaller.)
3. Pivot note: slices use **bottom-center** pivot, so the wizard's feet sit at the player's center and the body draws above it (old sprite was centered). If the wizard looks like it floats too high over the collider/shadows, either re-pivot to **Center** in the Sprite Editor, or drop the **Visual** child's local Y a bit.

## B. Create the facing asset

1. In `Assets/ScriptableObjects/Player/`: right-click → Create → Folder → name it `Facing`.
2. Inside it: right-click → **Create → PlayGround → Player → Sprite Facing Set**, name it `WizardBlueFacing`.
3. Assign: **Up Left = `wizard-blue_1`**, **Up Right = `wizard-blue_0`**, **Down Left = `wizard-blue_3`**, **Down Right = `wizard-blue_2`**.

## C. Wire the prefab

Do this **before entering Play mode** — PlayerRoot now fail-fasts on a missing facing set.

1. Open `Assets/Prefabs/Player/Player.prefab` (double-click).
2. Select the **Visual** child → SpriteRenderer → **Sprite = `wizard-blue_2`**.
3. Select the prefab root → **PlayerRoot** component → **Facing Set = `WizardBlueFacing`**.
4. Save (Ctrl+S), exit prefab mode.

## D. Verify (Play mode, `BenchmarkLarge` scene)

- [ ] No console errors on import/compile/play.
- [ ] Aim mouse into each quadrant around the player → sprite faces that diagonal (backs when aiming up, fronts when aiming down).
      If left/right feel swapped → swap the two Up fields and the two Down fields in `WizardBlueFacing`, set prefab sprite to `wizard-blue_3`.
- [ ] Sweep mouse slowly through straight-up / straight-down → no left/right flicker.
- [ ] On-screen size ≈ old player (~2 world units). Off → adjust Pixels Per Unit on `wizard-blue.png` (600 baseline; higher = smaller).
- [ ] Sprite vertical anchoring looks right (see A.3 pivot note).
- [ ] Take a hit → hurt flash still works; facing keeps updating.
- [ ] Mobs and projectile aim behave exactly as before.

## Acceptance criteria

All D checkboxes pass; direction mapping confirmed or corrected via the data-only swap.

## Future skins

**Update 2026-07-19**: Claude replicated wizard-blue's exact slicing (rects x=33/317/610/897, 293×384, pivot bottom-center) into the red/green/purple metas directly — red/green kept their existing sprite IDs, purple got fresh consistent IDs (its _1.._3 entries were malformed). All four pngs verified identical 1238×384. Remaining per-skin work: multi-select all four pngs → set Pixels Per Unit once → Apply; then per skin a new `SpriteFacingSet` asset + swap the prefab's Facing Set reference. Zero code.

Note: facing directions are intercardinal (diagonals) — `PlayerFacing` buckets aim into 4 quadrants, each sprite covers a 90° arc centered on its diagonal; transitions sit on the cardinal axes with hysteresis. No cardinal frames anywhere.
