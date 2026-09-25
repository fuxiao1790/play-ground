# 003 - Clean Assets And Graph Fixture

## Goal

Remove authored projectile segment trails and their exclusive art while keeping
targeted LineSegment content and AgentVFX coverage.

## Changes

1. Remove obsolete serialized fields from:
   - `Assets/Prefabs/Skills/Projectile/MagicBolt.prefab`
   - `Assets/Prefabs/Skills/Projectile/MagicBolt2.prefab`
   - `Assets/Prefabs/Skills/Projectile/FireArrow.prefab`
2. Delete asset plus `.meta` pairs:
   - `Assets/Vfx/LineSeg/MagicBoltTrail.vfx`
   - `Assets/Vfx/LineSeg/FireArrowTrail.vfx`
   - `Assets/Sprite/Skills/magic-bolt-2-trail.png`
   - `Assets/Sprite/Skills/fire-arrow-trail.png`
   - `Assets/Sprite/Skills/fire-arrow-trail-v2.png` (currently unreferenced
     alternate trail texture)
3. `Assets/Tests/EditMode/AgentVfxReadCompatibilityTests.cs`:
   - Change generic `GraphPath` fixture to
     `Assets/Vfx/LineSeg/PlagueLink.vfx`.
   - Keep all read-only bridge assertions unchanged.
4. Preserve:
   - `Assets/Vfx/LineSeg/PlagueLink.vfx` and `.meta`.
   - `Assets/Prefabs/Skills/Targeted/Plague.prefab` link binding.
   - Whole shared LineSegment runtime contract.

## Acceptance Criteria

- Deleted graph GUIDs have no remaining serialized references.
- No projectile prefab serializes `trailEffect`, `trailEffectShape`,
  `trailWidth`, or `trailStepDistance`.
- Projectile sprite renderer, sprite, material, hurtbox, and sound fields remain
  unchanged.
- `PlagueLink.vfx` still loads through AgentVFX read compatibility tests and
  remains bound to targeted prefab.
- `Assets/Vfx/LineSeg/` contains targeted link content only.

## Dependencies

- Depends on task 001 removing `BasicAttackPrefab` serialized fields.

## Scope / Complexity

Small. Asset deletion and serialized-reference cleanup; targeted preservation
requires careful GUID scan.

