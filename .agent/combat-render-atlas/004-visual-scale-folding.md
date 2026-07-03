# Visual Scale Folding

*(Unaffected by the 2026-07-03 atlas-configuration revision — this task is about
`CombatRenderComponent.VisualScale`, orthogonal to `UvRect`/how the atlas itself is produced.
Already implemented and verified as part of the initial pass.)*

## Change

Fix `GetAoeRenderComponent` to multiply in the registry entry's (now folded,
per task 003) `VisualScale`, which it currently ignores entirely. Verify
`GetProjectileRenderComponent` and `CombatRenderMatrixUtility.ElementFor`
need no further change.

### `GetAoeRenderComponent` — real fix

Current code (`CombatRenderComponents.cs:99-111`):

```csharp
public CombatRenderComponent GetAoeRenderComponent(int renderId, AoeSpawnGeometry geometry)
{
    if (!Entries.ContainsKey(renderId)) return default;
    return new CombatRenderComponent
    {
        IsRenderable = 1,
        AlignToVelocity = 0,
        VisualScale = new float2(geometry.VisualScale.x, geometry.VisualScale.y),
        VisualRotationSin = geometry.VisualRotationSin,
        VisualRotationCos = geometry.VisualRotationCos,
        RenderZ = CombatRoot.AoeRenderZ
    };
}
```

This "works" today only because `AoeTypeRegistry.TryBakeVisual`
(`AoeTypeRegistry.cs:40-60`) hardcodes the registered `VisualScale` to
`Vector2.one`, and the sprite's real pixel size is baked into that kind's
mesh vertices instead (`BuildSpriteMesh`, removed by task 003). Once every
kind shares one unit-quad mesh, the sprite's native pixel size only exists in
`entry.VisualScale` (folded by task 003's `Register()`) — so it must be
multiplied in here, or AOEs render at the mesh-native 1×1-world-unit size
(i.e., far too small).

Required change:

```csharp
public CombatRenderComponent GetAoeRenderComponent(int renderId, AoeSpawnGeometry geometry)
{
    if (!Entries.TryGetValue(renderId, out var entry)) return default;
    float2 scale = new float2(geometry.VisualScale.x, geometry.VisualScale.y)
        * new float2(entry.VisualScale.x, entry.VisualScale.y);
    return new CombatRenderComponent
    {
        IsRenderable = 1,
        AlignToVelocity = 0,
        VisualScale = scale,
        VisualRotationSin = geometry.VisualRotationSin,
        VisualRotationCos = geometry.VisualRotationCos,
        RenderZ = CombatRoot.AoeRenderZ
    };
}
```

(Switches from `Entries.ContainsKey` to `Entries.TryGetValue` since the entry
is now actually needed, not just checked for presence.)

### `GetProjectileRenderComponent` — verify, expect no-op

Current code already reads `res.VisualScale` (soon `entry.VisualScale` after
task 003's flattening) directly into the output — this already includes
whatever the registry entry's `VisualScale` holds. Before task 003, that was
an *authored* scale only (mesh baked in the native pixel size separately);
after task 003, `entry.VisualScale` *is* the fold of authored scale × native
pixel size. Since this call site already forwards the entry's `VisualScale`
verbatim, **no code change is needed** here — the fold happening upstream
(inside `Register()`) is sufficient. Confirm this by reading the post-003
code and reasoning through one concrete example (e.g. a 64×64px sprite at
32 pixels-per-unit with authored `VisualScale = Vector2.one`, mesh half-size
today would be 1×1 world units baked into vertices; post-fold,
`entry.VisualScale` should equal `(2, 2)` since native size = 64/32 = 2 world
units per axis — verify the arithmetic matches what task 003 actually
implements).

### `CombatRenderMatrixUtility.ElementFor` — verify, expect no-op

Consumes `CombatRenderComponent.VisualScale` opaquely
(`CombatRenderComponents.cs:317-370`, `float2 scale = render.VisualScale;`
then used directly in the matrix scale terms). Since both
`GetProjectileRenderComponent` and (after this task's fix)
`GetAoeRenderComponent` produce a `VisualScale` that already has native pixel
size folded in, this method needs no change — it was already
resolution-agnostic (just consumes whatever `VisualScale` it's handed).
Confirm by reading the method after 003/004 land; do not change it unless
the verification above reveals a mismatch.

## Acceptance Criteria

- AOEs render at the same on-screen size after this change as they did
  before the whole atlas rework (same `geometry.VisualScale` input, same
  final world-space size) — verify with a manual Editor test comparing an
  AOE's rendered size against its collision radius/area (which is unaffected
  by this plan and stays the ground truth for "correct" size).
- Projectiles render at the same on-screen size as before (regression check
  only — no code change expected here, but must be verified since the
  registry-side `VisualScale` value it consumes did change meaning).
- `CombatRenderMatrixUtility.ElementFor` is confirmed unchanged (or, if a
  real gap is found during verification, document exactly what was wrong and
  why — do not silently patch without recording the reason, since this
  method was believed correct going in).

## Dependencies

003 (registry rework — needs the flattened `CombatRenderResourceEntry.VisualScale`
field and the folding behavior in `Register()`).

## Scope

Small.
