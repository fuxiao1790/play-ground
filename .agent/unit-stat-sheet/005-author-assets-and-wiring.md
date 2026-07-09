# 005 — Author assets and prefab wiring

## Change
Editor-side authoring: create the `UnitStatSheet` assets and assign them on prefabs. `.asset` files
must be created **in the Unity editor** (correct GUID/serialization) — not hand-written.

- Create folder `Assets/ScriptableObjects/Units/`.
- Author assets (numbers per `Docs/reference/game-logic/mobs.md`):
  - `PlayerStatSheet` — maxHealth ~500, moveSpeed ~7 (match current player fields), offense = defaults.
  - `SlimeStatSheet` — maxHealth 35, moveSpeed 2.5.
  - `SkeletonStatSheet` — maxHealth 30, moveSpeed per archetype.
  - `BatStatSheet` — maxHealth 20, moveSpeed per archetype.
- Assign references:
  - Player prefab: same `PlayerStatSheet` on both `PlayerRoot.statSheet` and `SkillDriver.statSheet`.
  - Each mob prefab: its archetype sheet on `MobRoot.statSheet`.

## Acceptance criteria
- Every authored player/mob prefab has a non-null `statSheet` (fail-fast validation passes at Play).
- In-scene behavior matches the previous serialized values (no regression) before any tuning.

## Dependencies
001–004.

## Scope
Manual editor work; no code.

---

## Verification (whole feature — run after 005)

Harness cannot build/run Unity; verify in editor + PlayMode:

1. **Compiles**; `UnitStatSheet` menu item present.
2. **Player health from sheet** — set a distinct `maxHealth`; confirm seeded `TargetHealth.Max` /
   HUD reflects it and ECS damage still mirrors back.
3. **Offense wired** — `damageMultiplier = 2`, `critChance = 1` → damage ~doubles, all hits crit;
   reset to defaults → matches pre-change baseline.
4. **Movement** — change `moveSpeed`; player/mob speed changes.
5. **Mob archetypes** — differing kill-times per archetype; code-built mobs and existing mob
   PlayMode tests still pass.
