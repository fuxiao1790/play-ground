# 007 - VFX Sorting Layer Authoring

## Scope

Content/authoring only — no code. Every `VisualEffectAsset`/prefab registered
through `CombatVfxRoot.Register` (see
[vfx-system.md:138-159](../../Docs/reference/simulation/vfx-system.md#L138-L159)):
`BasicAttackPrefab` (`spawnEffect`/`hitEffect`/`expireEffect`/`armingEffect`),
`BasicAoePrefab`/`LingeringAoePrefab`/`AoeTypeDefinition`
(`spawnEffect`/`hitEffect`/`expireEffect`/`pulseEffect`/`armingEffect`).

## Change

For each `VisualEffect` GameObject these assets reference, open its Inspector
and set the Renderer foldout's **Sorting Layer** to `CombatVfx` (added in 001)
and **Order in Layer** to `0` (or whatever relative order is needed if
multiple VFX types need to order against each other — out of scope here,
default to `0` for all).

This is the same Inspector panel confirmed to exist in
[VisualEffectEditor.cs:1449-1454](../../Library/PackageCache/com.unity.visualeffectgraph@1d000c792c1e/Editor/Inspector/VisualEffectEditor.cs#L1449-L1454)
("test #2" in the design discussion) — `m_SortingLayerID`/`m_SortingOrder` on
the `VisualEffect`'s internal renderer, same fields a `SpriteRenderer` has.

## Acceptance Criteria

- Every VFX prefab/asset used by `PlayerSkillDriver`/`MobProjectileAttack`
  registration renders on the `CombatVfx` Sorting Layer.
- Visual check: with combat sprites and VFX overlapping on screen, VFX draws
  behind (bottom of) combat sprites, confirming `CombatVfx` sorts below
  `CombatSprites`.

## Dependencies

Depends on 001 (the `CombatVfx` layer must exist to assign it).
Independent of 002-006 (VFX rendering is untouched code-wise; this is pure
content authoring) but the visual result can only be confirmed once 002-006
land and the combat-sprite tier actually participates in Sorting Layer
ordering.

## Complexity

Small, but has a long tail — however many VFX assets exist need the same one
field changed. No code risk.
