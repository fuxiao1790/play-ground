# Combat Atlas Tight-Mesh UV Distortion

This document records a confirmed combat batch rendering failure mode: a sprite
registered with the combat render registry draws distorted (zoomed, stretched,
and/or skewed) in the ECS batch while the same sprite looks correct in a normal
`SpriteRenderer`.

This is not an atlas packing, prefab transform, or shader problem. The defect is
a hidden geometric assumption in the UV basis derivation: it only holds for
sprites whose mesh vertices reach the sprite rect corners.

## Non-Negotiable Sprite Import Rule

Every sprite registered for combat batch rendering must have mesh vertices at
its rect corners. In practice that means one of:

- The texture is imported with **Mesh Type = Full Rect** (`spriteMeshType: 0`).
- The art's opaque pixels fill the rect to within the sprite extrude distance
  (default 1 px), so the Tight outline still reaches the corners.

Tight mesh buys nothing in this pipeline. The batch renderer always draws one
full quad per instance from the capacity-baked mesh; the sprite's own tight
geometry never reaches the GPU. Tight only changes which vertices
`ComputeUvBasis` sees — and can only make them worse.

## Exact Failure Mechanism

`CombatRenderResourceRegistry.ComputeUvBasis` derives the per-kind affine UV
basis from the packed sprite:

1. It scores `sprite.vertices` and picks three extremes as bottom-left
   (`min(x + y)`), bottom-right (`max(x - y)`), and top-left (`max(y - x)`).
2. It uses those vertices' UVs directly: `origin = uv[bl]`,
   `uAxis = uv[br] - uv[bl]`, `vAxis = uv[tl] - uv[bl]`.
3. The shader stretches `origin + quad.x * uAxis + quad.y * vAxis` across the
   full unit quad.

Step 2 silently assumes the three chosen vertices sit exactly at the sprite
rect's corners. That is true for Full Rect meshes (four corner vertices) and
approximately true for Tight meshes whose art fills the rect. For a Tight mesh
with transparent margins, the outline hugs the opaque pixels, the chosen
"corners" sit well inside the rect, and the basis spans only the inner art
region. The shader then blows that inner region up to the full quad: the sprite
renders zoomed in, with wrong aspect if the art region is not square, plus skew
from irregular outline extremes.

The design rule "UVs come from `sprite.uv` and `sprite.vertices`, never
`sprite.rect`" exists to survive atlas packer rotation. It is still correct,
but it smuggled in this second assumption about where those vertices are.

## Confirmed Incident

2026-07-18, `ArcaneMissile` and `ArcaneMissile2` projectile prefabs:

- Both sprites (`arcane-missile.png`, `arcane-missile-2.png`) are 64x64 with
  `spriteMeshType: 1` (Tight) and fully transparent corners.
- `arcane-missile` opaque pixels span only (6,24)-(54,42) — a thin horizontal
  band, so its distortion is strongly non-uniform (~48x18 px stretched to a
  square quad). `arcane-missile-2` spans (12,12)-(54,54) — mostly a zoom.
- Every other registered sprite is a tilemap tile whose opaque pixels reach
  within 1 px of the rect edges. The 1 px sprite extrude pushes their Tight
  outlines to the corners, so the corner assumption held for them by luck, not
  by design.
- Prefabs, skill assets, and atlas settings were identical in structure to
  working projectiles.

## Misleading Tests And False Leads

- Comparing prefab transforms, visual scale, or skill configuration. The
  distortion is baked into the per-kind UV basis at registration.
- Suspecting atlas packer rotation. `enableRotation` and `enableTightPacking`
  are both off in `Assets/Atlas/Skills.spriteatlasv2`; the affine basis handles
  rotation anyway.
- Suspecting atlas page overflow. The stale 1254x1254 sprite-sheet entries in
  the arcane `.meta` files are leftovers from replaced source images; single
  sprite mode ignores them and the real 64x64 sprites fit one page.
- Validating with a `SpriteRenderer` preview. `SpriteRenderer` draws the
  sprite's own mesh with per-vertex UVs, which is always self-consistent. It
  cannot reveal this defect; only the batch quad path can.

## Correct Pattern

Preferred data fix: import every combat-registered sprite as Full Rect and
repack the atlas.

Robust code fix (if Tight sprites must be supported): stop assuming corner
vertices. Solve the true affine vertex-to-UV map from three non-collinear
vertices and their **positions**, then evaluate it at the real rect corners
derived from `sprite.rect`, `sprite.pivot`, and `sprite.pixelsPerUnit`. This
keeps the packer-rotation guarantee while removing the corner assumption.

## Review Checklist

Before registering a new sprite kind with the combat batch renderer:

- Check the texture's Mesh Type. If it is Tight, either switch it to Full Rect
  or confirm the art fills the rect edge to edge.
- After adding it to the atlas and repacking, view the kind in the ECS batch
  in-game at least once. Do not sign off from the prefab preview.
- Treat any "zoomed in" or "stretched" combat sprite as this failure mode
  first, and check the import settings before touching shader or registry code.

## Current Code Anchors

- [`CombatRenderComponents.cs`](../../../Assets/Scripts/System/Rendering/CombatRenderComponents.cs)
  — `CombatRenderResourceRegistry.ComputeUvBasis` derives the UV basis;
  `Register` calls it per registered kind.
- [combat-render-system.md](./combat-render-system.md) — the UV basis design
  and the sorting/constraint context.
