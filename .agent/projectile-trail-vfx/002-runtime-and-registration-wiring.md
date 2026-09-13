# 002 — Runtime Definition, Registration, and Spawn Command Wiring

## Scope

Thread the trail VFX id/width from the authored prefab (001) through
compilation, registration, and the resolved spawn command, mirroring
`RegisterAoeVfx`/`RegisterTargetedVfx` and `AoeSpawnCommand.VfxIds`/
`TargetedSpawnCommand.VfxIds`.

## Changes

### `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`

Add three properties to `RuntimeProjectileDefinition`:

```csharp
public int TrailVfxId { get; set; }
public float TrailWidth { get; set; }
public float TrailStepDistance { get; set; }
```

Placed near `ManaCost`/`ArmSeconds` (simple value fields), not inside any of
the `Runtime...SpawnSetup` nested classes.

### `Assets/Scripts/Skills/SkillDriver.cs`

Add a registration method mirroring `RegisterAoeVfx`/`RegisterTargetedVfx`
(both near line 1323–1367):

```csharp
private void RegisterProjectileVfx(RuntimeProjectileDefinition projDef)
{
    if (projDef?.Prefab == null)
        return;

    projDef.TrailVfxId = vfxRoot != null
        ? vfxRoot.Register(projDef.Prefab.TrailEffect, projDef.Prefab.TrailEffectShape)
        : 0;
    projDef.TrailWidth = projDef.Prefab.TrailWidth;
    projDef.TrailStepDistance = projDef.Prefab.TrailStepDistance;
}
```

Call it from `RegisterProjectileTypesRecursive`, inside the existing block:

```csharp
if (def is RuntimeProjectileDefinition projDef && projDef.Prefab != null)
{
    RegisterProjectileVfx(projDef);
    if (combatRoot != null && projDef.TypeId < 0)
    {
        projDef.TypeId = combatRoot.RegisterTemplate(projDef.Prefab);
        projDef.RenderId = combatRoot.ProjectileRenderId(projDef.TypeId);
    }
}
```

`RegisterProjectileVfx` is called unconditionally on every pass (not gated by
`TypeId < 0`), matching `RegisterAoeVfx`/`RegisterTargetedVfx`, which refresh
ids every time their owning `RegisterXTypeDefinition` runs — `vfxRoot.Register`
is idempotent per asset (`Docs/reference/simulation/vfx-system.md`: "the same
asset reference returns the first registered id"), so repeated calls are free.

### `Assets/Scripts/System/Projectiles/ProjectileSpawnRequest.cs`

Add three fields to `ProjectileSpawnCommand`:

```csharp
public int TrailVfxId;
public float TrailWidth;
public float TrailStepDistance;
```

Placed near `RenderTypeId` (both are resolved-id fields, not per-instance
gameplay data).

### `Assets/Scripts/Skills/SkillDriver.cs` — `SkillIntervalTemplateBuilder.BuildProjectileTemplate`

Add to the returned `ProjectileSpawnCommand`:

```csharp
TrailVfxId = child.TrailVfxId,
TrailWidth = child.TrailWidth,
TrailStepDistance = child.TrailStepDistance,
```

placed alongside `RenderTypeId = child.RenderId,`.

### `Assets/Scripts/System/Core/CombatRoot.cs` — `ProjectileCommandFor`

**No change.** This is the direct `CombatRoot.Spawn` API path, which today
also does not populate `SoundIds`/`SpawnSoundRadius` for the same reason (no
`RuntimeProjectileDefinition` is available at that call site — only a raw
`ProjectileSpawnRequest`). Commands built here get `TrailVfxId = 0` by
default, meaning no trail. See index.md Open Questions.

## Acceptance Criteria

- A projectile skill with an authored trail effect produces a nonzero,
  correctly-shape-encoded `RuntimeProjectileDefinition.TrailVfxId` after
  `SkillDriver` registration runs.
- `ProjectileSpawnCommand` built via `SkillIntervalTemplateBuilder.BuildProjectileTemplate`
  carries that same `TrailVfxId`/`TrailWidth`/`TrailStepDistance`.
- `ProjectileSpawnCommand` built via `CombatRoot.ProjectileCommandFor` (direct
  API) always carries `TrailVfxId == 0`, by design.
- Re-running registration (e.g. after a loadout edit that reassigns the trail
  asset) updates `TrailVfxId` without requiring `TypeId` to be re-registered.

## Dependencies

Depends on 001 (`BasicAttackPrefab.TrailEffect`/`TrailEffectShape`/`TrailWidth`
must exist first). Feeds 003 (`ProjectileSpawnCommand.TrailVfxId`/`TrailWidth`
are what `ProjectileSpawnApplyUtility.WriteCommon` reads).
