# 001 - Render Gate Becomes Sprite Gate

## Goal

Rename/reframe `CombatRenderActiveTag` as sprite visibility rather than generic
entity activity.

Proposed name:

```text
CombatSpriteRenderActiveTag
```

This is behavior-preserving. Existing projectiles and AOEs still enable the tag
exactly where they currently enable `CombatRenderActiveTag`.

## Scope

- Rename component declaration in
  `Assets/Scripts/System/Common/CombatRenderComponents.cs`.
- Update render prepare, batched render, stats, spawn apply, lifetime, collision,
  and AOE collision references.
- Update lifecycle comments to state:
  sprite gate is enabled when the entity's combat lifecycle is sprite-visible,
  not simply when `Active` is enabled.
- Update render/stat docs that mention `CombatRenderActiveTag`.

## Acceptance Criteria

- No `CombatRenderActiveTag` references remain in production code or docs except
  migration notes.
- Rendered sprite counts and behavior are unchanged with zero arming time.
- Stats still count sprite-visible projectiles/AOEs, not all live entities.
- `Active` remains the dead-slot reuse authority.

## Dependencies

None.

## Estimated Scope

Medium mechanical rename, broad but low behavior risk.
