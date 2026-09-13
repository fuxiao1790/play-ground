# 001 — Authoring: Projectile Trail Slot

## Scope

Add the trail VFX authoring slot to `BasicAttackPrefab` and its loadout
validation, mirroring `TargetedPrefab`'s link-effect slot and
`SkillLoadoutValidator.ValidateTargetedDefinition`'s link checks exactly.

## Changes

### `Assets/Scripts/System/Authoring/BasicAttackPrefab.cs`

Add fields (near the existing `spriteRenderer`/sound fields):

```csharp
[SerializeField] private VisualEffectAsset trailEffect;
[SerializeField] private VfxDataShape trailEffectShape = VfxDataShape.LineSegment;
[SerializeField, Min(0f)] private float trailWidth = 0.25f;
[SerializeField, Min(0f)] private float trailStepDistance = 0.5f;
```

`trailStepDistance` is the minimum world-space distance the projectile must
travel since the last emitted segment before another `LineSegmentVfxEvent` is
enqueued (see 004 — emission is distance-gated, not per-frame). A value of `0`
degenerates back to emitting whenever the entity has moved at all, which is
the excessive/unpredictable behavior this field exists to prevent, so the
validator (below) warns on it same as it does for `trailWidth`.

Requires `using PlayGround.System.Combat.Vfx;` and `using UnityEngine.VFX;`
added to this file (neither is currently imported here).

Add properties:

```csharp
public VisualEffectAsset TrailEffect => trailEffect;
public VfxDataShape TrailEffectShape => trailEffectShape;
public float TrailWidth => trailWidth;
public float TrailStepDistance => trailStepDistance;
```

Do **not** add a trail-shape check to `IsValidTemplate`/`ValidateReferences`.
Per the index rationale, this prefab's `Awake()` hard-throws on invalid setup
and every existing projectile prefab has no trail authored; a hard check here
would be a behavior change for every current projectile prefab that doesn't
need one. Misconfiguration is a loadout-validator warning instead (see below).

No `Configure(...)` helper parameter needed unless existing test helpers that
call `BasicAttackPrefab.Configure(...)` need to author a trail directly for a
new unit test — check `Assets/Tests/EditMode/ProjectileAuthoringEditModeTests.cs`
and `Assets/Tests/EditMode/ProjectileContinuousAuthoringEditModeTests.cs` for
the current `Configure` signature before deciding whether to extend it; if no
planned test needs to author a trail via `Configure`, leave it unchanged and
let field defaults cover it.

### `Assets/Scripts/Skills/SkillValidationWarning.cs`

Add one enum value:

```csharp
ProjectileVisualWarning,
```

(placed after `TargetedVisualWarning`, matching the existing grouping of
per-skill-kind visual warning codes).

### `Assets/Scripts/Skills/SkillLoadoutValidator.cs`

Add a case to `ValidateSkillSet` alongside the existing
`if (skill.Definition is TargetedDefinition targeted)` branch:

```csharp
if (skill.Definition is ProjectileDefinition projectile)
    ValidateProjectileDefinition(projectile, slotIndex, warnings);
```

Add the method (placed after `ValidateTargetedDefinition`), mirroring its
link-effect checks at lines 159–177:

```csharp
private static void ValidateProjectileDefinition(
    ProjectileDefinition definition,
    int slotIndex,
    List<SkillValidationWarning> warnings)
{
    BasicAttackPrefab prefab = definition.prefab;
    if (prefab == null || prefab.TrailEffect == null)
    {
        return;
    }

    if (prefab.TrailEffectShape != VfxDataShape.LineSegment)
    {
        AddWarning(warnings, SkillValidationWarningCode.ProjectileVisualWarning, slotIndex,
            "Projectile trail VFX is assigned but is not a LineSegment shape; it will be dropped at registration.");
    }

    if (prefab.TrailWidth <= 0f)
    {
        AddWarning(warnings, SkillValidationWarningCode.ProjectileVisualWarning, slotIndex,
            "Projectile trail VFX is assigned but trailWidth is not positive.");
    }

    if (prefab.TrailStepDistance <= 0f)
    {
        AddWarning(warnings, SkillValidationWarningCode.ProjectileVisualWarning, slotIndex,
            "Projectile trail VFX is assigned but trailStepDistance is not positive; the trail will emit every simulation tick instead of at a controlled distance interval.");
    }
}
```

Note the guard is `TrailEffect == null` returns early (unlike Targeted's link
check, which warns when the effect is *missing* because Targeted always wants
one). A projectile with no trail authored is the common case and must not
produce a warning.

## Acceptance Criteria

- `BasicAttackPrefab` exposes `TrailEffect`/`TrailEffectShape`/`TrailWidth`/
  `TrailStepDistance`, defaulting to `null`/`LineSegment`/`0.25f`/`0.5f`
  respectively.
- No existing projectile prefab throws `MissingReferenceException` on load as
  a result of this change (verify by inspecting `IsValidTemplate` is
  untouched).
- A projectile skill with a trail effect assigned but the wrong shape, a
  non-positive width, or a non-positive step distance produces exactly one
  `ProjectileVisualWarning` each, surfaced through `SkillLoadoutValidator.Validate`.
- A projectile skill with no trail effect assigned produces zero new warnings.

## Dependencies

None (first task). Feeds 002 (`RuntimeProjectileDefinition`/`SkillDriver` read
`prefab.TrailEffect`/`TrailEffectShape`/`TrailWidth`).
