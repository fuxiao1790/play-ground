# 004 - Shader: SV_InstanceID to Per-Vertex slotIndex

## Scope

`Assets/Shaders/CombatAtlasIndirectSprite.shader`.

## Change

Current vertex input
([CombatAtlasIndirectSprite.shader:56-61](../../Assets/Shaders/CombatAtlasIndirectSprite.shader#L56-L61)):

```hlsl
struct Attributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    uint instanceID : SV_InstanceID;
};
```

`SV_InstanceID` only has meaning for an actual instanced/indirect draw; a plain
mesh submitted through a `MeshRenderer` has no instance concept — every vertex
is just a vertex. Replace it with the baked `slotIndex` stream from 002:

```hlsl
struct Attributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    float2 slotIndex : TEXCOORD1; // .x = slot index (float, cast to uint)
};
```

And in `vert()`
([CombatAtlasIndirectSprite.shader:69-87](../../Assets/Shaders/CombatAtlasIndirectSprite.shader#L69-L87)),
replace:

```hlsl
CombatInstanceData inst = _InstanceData[IN.instanceID];
```

with:

```hlsl
uint slot = (uint)round(IN.slotIndex.x);
CombatInstanceData inst = _InstanceData[slot];
```

Everything else in `vert()`/`frag()` — the rotation/position reconstruction,
UV basis lookup, atlas sampling — is unchanged; `positionOS` and `uv` already
carry the same per-corner data they did before, just repeated per baked quad
instead of coming from a single shared quad drawn `N` times.

Update the comment above `_InstanceData`/`vert()`
([CombatAtlasIndirectSprite.shader:37-42](../../Assets/Shaders/CombatAtlasIndirectSprite.shader#L37-L42))
to describe the new lookup mechanism instead of referencing per-instance
semantics.

## Acceptance Criteria

- Shader compiles under the existing `Pass` block, `LightMode = Universal2D`
  tag unchanged.
- Given the same `_InstanceData`/`_UvBasis` buffer contents as before, visual
  output (position, rotation, UV) is pixel-identical to the pre-refactor
  indirect-draw output for the same instance count.

## Dependencies

Depends on 002 (needs the UV1 `slotIndex` stream to exist on the mesh being
fed into this shader). Should land together with 005 in practice since the
old code path (indirect draw) and new code path (baked mesh + this shader)
can't both be correct simultaneously — but the shader edit itself has no
compile-time dependency on 005.

## Complexity

Small — one struct field swap, one line change in `vert()`.
