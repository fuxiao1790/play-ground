# 004 — Registry / Material

## Change

Point the registry's shared material at the new indirect shader and drop the instancing flag.
Small, surgical edits to `CombatRenderResourceRegistry.EnsureSharedResources()` in
`CombatRenderComponents.cs`.

## Edits

- `Shader.Find("Combat/AtlasInstancedSprite")` → `Shader.Find("Combat/AtlasIndirectSprite")`
  (task 001's shader name). Keep the null-check throw.
- Remove `enableInstancing = true` from the `new Material(shader) { … }` initializer — the
  manual-buffer indirect path does not use GPU instancing keywords. (Harmless if left, but it
  signals the wrong mechanism; remove for clarity.)
- Keep everything else: `SharedMaterial.mainTexture = atlasTexture` (the atlas sampler binding),
  `SharedMesh = BuildUnitQuadMesh()`, `Layer`, `Register(...)` UV math, single-page guard,
  `Unregister()` destroying `SharedMesh`/`SharedMaterial` (never the atlas texture).

## Buffer binding site (decision)

The instance `StructuredBuffer` is bound in the render system via
`RenderParams.matProps.SetBuffer("_InstanceData", _instanceBuffer)` (task 003), **not** here.
Rationale: the buffer is owned by and grows in the render system; binding through `matProps`
keeps the registry's shared material free of per-frame/lifecycle state and avoids the registry
needing a reference to a system-owned buffer. The registry stays purely the owner of static GPU
resources (mesh, material, atlas ref).

## Acceptance Criteria

- The shared material uses `Combat/AtlasIndirectSprite`; its `_MainTex` is the packed atlas
  texture, exactly as before.
- No `enableInstancing` on the material.
- `Unregister()` still destroys only registry-created resources; the atlas asset is untouched.

## Dependencies

001 (shader must exist for `Shader.Find`). Trivial to land alongside 003.

## Scope

Trivial.
