# Registry Rework

*(Updated 2026-07-03: no packer dependency, no dirty-flag/rebuild cycle — `Register(...)`
computes the UV rect immediately from the configured atlas texture and `sprite.rect`. See
`002-atlas-configuration.md` for where the atlas texture itself comes from.)*

## Change

Rework `CombatRenderResourceRegistry` (and its nested types) in
`Assets/Scripts/System/Common/CombatRenderComponents.cs` to own one shared
unit-quad `Mesh`, one shared atlas `Material` (built from the task-001
shader), a reference to the manually-assigned atlas `Texture2D` (task 002),
and per-kind UV rects computed immediately at registration — replacing
today's per-kind `Mesh`/`Material`/`MaterialPropertyBlock` construction.

### Structural simplification

Collapse `CombatSpriteRenderResources` into `CombatRenderResourceEntry`
directly:

```csharp
public sealed class CombatRenderResourceEntry
{
    public Vector2 VisualScale;        // folded: authored scale * (sprite.rect.size / pixelsPerUnit)
    public float VisualRotationSin;
    public float VisualRotationCos;
    public Vector4 UvRect;             // (uOffset, vOffset, uScale, vScale) within the configured atlas
}
```

(The entry no longer needs to hold the `Sprite` itself — unlike the original,
runtime-packer-based design, nothing repacks later, so there's no need to
retain the source sprite reference after computing `VisualScale`/`UvRect`
once.)

### Registry shape

```csharp
public sealed class CombatRenderResourceRegistry : IComponentData
{
    public readonly Dictionary<int, CombatRenderResourceEntry> Entries = new();
    public Mesh SharedMesh { get; private set; }
    public Material SharedMaterial { get; private set; }
    public Texture2D AtlasTexture { get; private set; }
    public int Layer { get; private set; }
    public const float BoundsHalfExtent = 100000f;

    public void ConfigureAtlas(Texture2D atlasTexture); // see 002-atlas-configuration.md
    public int Register(Sprite sprite, Vector2 visualScale, float visualRotationDegrees, int layer);
    public void Unregister();
}
```

Notes:
- `Register(...)` keeps the same 4-argument signature as before this revision
  (`sourceMaterial`/`meshName` were already dropped from the original
  per-kind-resource design — no further signature change here).
- `Register(...)` throws (see task 002) if the atlas isn't configured or the
  sprite doesn't belong to it; otherwise computes `UvRect` directly from
  `sprite.rect`/`AtlasTexture.width`/`height` — no packing, no dirty flag, no
  `EnsureAtlasCurrent()`-style call needed anywhere (this method existed in
  the packer-based design and no longer exists at all).
- `EnsureSharedResources()` (called once lazily, from both `Register(...)`
  and `ConfigureAtlas(...)` — whichever runs first): builds `SharedMesh` (a
  unit quad, `[-0.5,0.5]²` positions, `[0,1]²` UVs) and `SharedMaterial`
  (`new Material(Shader.Find("Combat/AtlasInstancedSprite")) { enableInstancing
  = true }`, throw `MissingReferenceException` if the shader isn't found).
- `Unregister()` destroys `SharedMaterial`/`SharedMesh` (registry-created
  resources) but **must not** destroy `AtlasTexture` — it's a referenced,
  not owned, project asset. Just null out the reference. Clears `Entries`,
  resets `_nextRenderId`.
- `layer`: captured as a single registry-level value (last `Register()` call
  wins — every call site passes the same `gameObject.layer` from the one
  `CombatRoot` in practice). Needed because submission is one shared draw
  call for everything (task 005) — `RenderParams.layer` is per-call, not
  per-instance.
- Remove now-dead code from the pre-atlas baseline:
  `BuildResources`/`BuildSpriteMesh` (native-size computation moves into
  `Register()`'s `VisualScale` fold; UV-rect baking is replaced entirely by
  the direct `sprite.rect`-based computation),
  `ConfigureMaterial`/`ConfigureProperties`/`ValidateResources`/`FindSpriteShader`.
  Keep the `PositiveScale`-equivalent clamping (authored scale of 0 or
  negative falls back to 1) inside `Register()`.

## Acceptance Criteria

- `CombatRenderResourceRegistry` builds exactly one `Mesh` and one `Material`
  regardless of how many kinds are registered; `AtlasTexture` is a reference
  to whatever `CombatRoot` assigned via `ConfigureAtlas(...)`, never
  created/destroyed by the registry.
- `Register(...)` returns 0 for a null sprite (unchanged behavior) and a
  sequential positive render id otherwise (unchanged behavior), or throws per
  task 002's validation rules for a non-null sprite that isn't part of the
  configured atlas.
- Each registered kind's `UvRect` is correct on the first `Register(...)`
  call — no separate "build"/"repack" step needed anywhere in the codebase.
- `Unregister()` leaves no dangling `Mesh`/`Material` and does not destroy
  the atlas texture asset.

## Dependencies

001 (shader), 002 (atlas configuration).

## Scope

Medium (reduced from Large in the original packer-based design — no packing
algorithm, no dirty-flag lifecycle to implement).
