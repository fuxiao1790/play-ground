# 015 — Documentation and authored content

**Depends on:** all. **Scope:** medium. **Risk:** none technically; high if skipped.

## Why

`Docs/` is the authority in this project, and a doc set that lags the code is worse than no doc set
— the next reader trusts it. This task also carries the editor work, which is the user's (C14).

## Doc updates

| File | Change |
|---|---|
| `Docs/reference/game-logic/skill-system.md` | New Targeted domain: two skill types, `TargetedDefinition` / `LingeringTargetedDefinition`, the `Targeted` tag, both new trigger links with their compatible tags, and the updated energy-driven source/child table. |
| `Docs/reference/game-logic/skill-modifiers.md` | `MultipleChainsSupport` in the augment roster and the compatible-tags table; `IncreasedAoeSupport` / `ConcentratedEffectSupport` gaining `Targeted`; the `Any` widening. |
| `Docs/contracts/spawn-events-and-commands.md` | The two new event types, the two new `IntervalChildKind` values, the `AcquireAnchor` instance-frame field, and the note that the identical-struct count is now five with a pointer to the todo debt entry. |
| `Docs/contracts/combat-hit-and-tick-results.md` | `CombatHitEvent.DamageScale`, its zero-means-one rule, and where it applies relative to the crit roll. |
| `Docs/contracts/combat-root-api.md` | `RegisterSpawnTemplate` / `RegisterTimedSpawnTemplate` / `SpawnRegisteredTargeted` / `RegisterTargetedType`, noting the two-position signature. |
| `Docs/reference/simulation/index.md` | Targeted in the ECS aspect table; the frame path updated with the resolve systems. |
| `Docs/reference/simulation/` | New `targeted-system.md` aspect doc: archetypes, the two pools, the resolve loop, the walk state, the render mirror, and the caps. Mirrors `aoe-system.md`. |
| `Docs/layers/ecs-simulation.md` | Targeted in Owns / Main Systems. |
| `Docs/architecture/phase-order.md` | The resolve systems in the inferred frame order. |
| `Docs/folder-structure.md` | `Assets/Scripts/System/Targeted/` in the runtime map; the new skill and support files. |
| `Docs/testing.md` | Targeted entries in the first-tests lists. |
| `Docs/todo.md` | Strike the targeted-skills line. Keep the `refactor debt` entry — it is now more urgent, not less. |


## User steps (editor work — the agent does not perform these)

Unity editor work is instructions for the user, never hand-edited asset YAML.

1. **Prefab.** Create `Assets/Prefabs/Combat/TargetedChain.prefab` with a `TargetedPrefab`
   component. Add a `Visual` child with a `SpriteRenderer` **only** if a debug sprite is wanted.
   **Do not add a `Hurtbox` child** — validation rejects it as a copied-template mistake.
2. **VFX graph.** Create a `LineSegment`-shaped VFX graph. It must read `StartPositions`,
   `EndPositions`, and `Widths`. Assign it to the prefab's link slot with shape `LineSegment`.
   If it shares a graph with another skill set, obey the area-size isolation rule: sample in
   `Initialize Particles` and copy to a particle attribute; never sample in `Update`/`Output`.
3. **Sprite atlas.** If a debug sprite is used, add it to the combat sprite atlas and repack, and
   confirm its material has GPU instancing enabled.
4. **Skill assets.** Create a `Targeted Skill` and a `Lingering Targeted Skill` via
   `Assets > Create > PlayGround > Skills`. Suggested first content: `acquireRadius` ~6,
   `maxTargets` 4, `chainRadius` ~4, `chainDamageFalloff` 0.7, `chainDelaySeconds` 0.05,
   `count` 1. On the prefab, set `linkWidth` and `vfxEffectSize` to non-zero values — a chain has
   no gameplay area for the runtime to derive them from, so unset means invisible.
5. **Skill sets and loadout.** Wrap each skill in a `SkillSet`, add them to the player's
   `SkillLoadout`, and bind a root set to input.
6. **Trigger demo.** Wire an existing projectile set → `OnImpactTargetedTrigger` → the targeted set
   to exercise the impact path.
7. **Register the VFX graph** on the scene's `CombatVfxRoot` so the dispatcher can resolve it.

## Acceptance criteria

- Every doc in the table above is updated in the same change as the code it describes.
- `Docs/todo.md` no longer lists targeted skills as outstanding, and still lists the spawn-event
  consolidation debt.
- The new `targeted-system.md` covers archetypes, pools, resolve loop, walk state, render mirror,
  and caps.
- No doc still claims targeted skills are unimplemented or that `Any` means "projectile or AOE".
- The user steps above are handed over as written instructions, and the feature is confirmed
  working in the real app after the user completes them.

## Notes

Two things future readers will look for and must find: **why there are five identical spawn-event
structs** (todo debt entry plus index.md §6), and **why chains can overkill** (requirements §3.5 —
finalize runs after resolve, so mid-frame `Health` is stale by construction).
