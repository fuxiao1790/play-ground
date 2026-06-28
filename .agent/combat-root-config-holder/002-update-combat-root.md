# 002 — CombatRoot becomes a render config holder

## File
`Assets/Scripts/System/Common/CombatRoot.cs`

## Dependency
Requires 001 (registry API must exist).

## Principle
CombatRoot reads authored config and pushes it to the registry. It stops owning the
render-id counter, the GPU resource store, GPU lifetime, and render-component
construction. Its registration entry points keep their signatures and behavior
(register-by-config, read id back) so **no caller changes**.

## Remove

Fields:
```
renderResourcesById   Dictionary<int, CombatSpriteRenderResources>   (line 50)
nextRenderId          int                                            (line 53)
batchBoundsHalfExtent [SerializeField] float                         (lines 40-42)
spawnVisuals          [SerializeField] bool                          (lines 37-38)
```

> **`spawnVisuals` is removed** (along with its `[Header("AOE visuals")]`). It was a
> redundant second source of truth: whether an AOE renders is already determined by
> whether its type's prefab carries a sprite — `AoeTypeRegistry.TryBakeVisual` returns
> false for a null `VisualPrefab` or a prefab with no `SpriteRenderer`/sprite, so
> `TryGetVisual` already gates registration. `spawnVisuals` only added a global override
> with no per-type granularity; grep confirms nothing outside CombatRoot reads it. If a
> global "hide all AOE sprites" debug toggle is ever wanted, it belongs as a render-system
> flag, not as content config on CombatRoot.

Methods / consts:
```
RegisterRenderResource(Sprite, Vector2, float, Material, string) → int   (lines 574-598)
ProjectileRenderComponentForRenderId(int, int) → CombatRenderComponent   (lines 657-673)
AoeRenderComponentForRenderId(int, AoeSpawnGeometry) → CombatRenderComponent (lines 686-702)
DestroyRenderResources()                                                 (lines 704-714)
ProjectileMeshName / AoeMeshName  consts                                 (lines 567-568)  → now CombatRenderResourceRegistry.*
```

> Removing the serialized `batchBoundsHalfExtent` changes scene serialization for any
> CombatRoot prefab/scene that set a non-default value. Per the constraints it's
> cosmetic (effectively infinite) and the registry now uses a fixed `BoundsHalfExtent`.
> Confirm no scene relies on a tuned value before deleting; otherwise keep the field and
> pass it through `Register` as a parameter instead.

## Keep (unchanged — these make CombatRoot the config holder)

```
projectileRenderIdByType   Dictionary<int, int>     // typeId → renderId, for Spawn(Request)
aoeRenderIdByType          Dictionary<int, int>
ProjectileRenderId(int) → int                        // read-back used by callers + command builders
AoeRenderId(int) → int
ProjectileVisualScale(float) → Vector2               // pure config math
ProjectileRenderZ / ProjectileRenderZStep / ProjectileRenderZSlots / AoeRenderZ  consts
```

The render-Z consts stay: `ProjectileSpawnExpansionSystem.cs:241-242` and
`PlayerSkillDriver.cs:741` reference them as `CombatRoot.*`, and they are render-layout
*config*, not resource ownership.

## Update — route through the registry

**`RegisterTemplate`** (lines 145-153): replace the `RegisterRenderResource(...)` call
with the registry call, keeping the same map population:
```csharp
if (!projectileRenderIdByType.ContainsKey(typeId))
{
    projectileRenderIdByType[typeId] = _renderRegistry?.Register(
        template.Sprite,
        ProjectileVisualScale(template.VisualScale),
        template.VisualRotationDegrees,
        template.Material,
        CombatRenderResourceRegistry.ProjectileMeshName,
        gameObject.layer) ?? 0;
}
```
Signature and the returned `typeId` are unchanged → compiler, mob, and tests unaffected.

**`BuildProjectileRenderResources`** (lines 608-649):
- Replace the opening `DestroyRenderResources();` with:
  ```csharp
  _renderRegistry?.Unregister();
  projectileRenderIdByType.Clear();
  aoeRenderIdByType.Clear();
  ```
  (Awake runs once, but this keeps the reset semantics `DestroyRenderResources` had.)
- Replace the two inline `RegisterRenderResource(...)` calls (the `projectileSprite`
  default at line 615 and the `renderTypes` loop at line 641) with
  `_renderRegistry?.Register(..., gameObject.layer) ?? 0`, using
  `CombatRenderResourceRegistry.ProjectileMeshName`.

**`TryBuildAoeRenderResource`** (lines 675-684): drop the `spawnVisuals` clause so the
guard keys purely on sprite presence, and replace `RegisterRenderResource(...)` with the
registry call:
```csharp
private void TryBuildAoeRenderResource(int typeId)
{
    if (!typeRegistry.TryGetVisual(typeId, out AoeVisualDefinition visual))
        return;
    aoeRenderIdByType[typeId] = _renderRegistry?.Register(
        visual.Sprite, visual.VisualScale, visual.VisualRotationDegrees, visual.Material,
        CombatRenderResourceRegistry.AoeMeshName, gameObject.layer) ?? 0;
}
```
`TryGetVisual` is now the sole gate: register a render resource iff the AOE type's prefab
carried a sprite. (`GetAoeRenderComponent` already returns `default` for a missing entry,
so spriteless / vfx-only AOEs render nothing.)

**Command builders** — replace the local builders with registry calls:
- `ProjectileCommandFor` (line 397): `Render = _renderRegistry.GetProjectileRenderComponent(renderId, baseProjectileId)`
- `AoeCommandFor` (line 451): `Render = _renderRegistry.GetAoeRenderComponent(renderId, geometry)`

**Template render helpers** (lines 274-278):
```csharp
internal CombatRenderComponent ProjectileTemplateRenderComponent(int renderId) =>
    _renderRegistry?.GetProjectileRenderComponent(renderId, 0) ?? default;

internal CombatRenderComponent AoeTemplateRenderComponent(int renderId, AoeSpawnGeometry geometry) =>
    _renderRegistry?.GetAoeRenderComponent(renderId, geometry) ?? default;
```

**Teardown `OnDestroy`** (lines 119-125): replace the manual `Entries.Remove` loop +
`DestroyRenderResources()` with:
```csharp
_renderRegistry?.Unregister();
_renderRegistry = null;
```

## Awake null-registry behavior
`Awake` already warns when the registry singleton is missing and leaves `_renderRegistry`
null (lines 96-97). With the `?.` / `?? 0` guards above, a missing registry yields
renderId 0 ("no visual") everywhere instead of throwing — same tolerance as today.

## Acceptance Criteria
- `CombatRoot` allocates/destroys **no** GPU objects directly; `BatchedSpriteRenderer`
  and `CombatSpriteRenderResources` are referenced only via the registry.
- `RegisterTemplate` / `RegisterConfig` / `RegisterType` keep their signatures; the
  `typeId → renderId` maps are still populated correctly.
- `PlayerSkillDriver`, `MobProjectileAttack`, `MobRoot`, and the PlayMode tests compile
  and run **without modification**; mob projectile/AOE visuals still register.
- Teardown destroys all GPU resources via `registry.Unregister()`.
- No references to any removed field/method/const remain in the file.

## Scope
Medium. ~60 lines removed, ~20 changed, all within CombatRoot. Structural removal, no new concepts.
