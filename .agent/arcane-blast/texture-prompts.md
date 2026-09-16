# ArcaneBlast texture prompts

## `arcane-shockwave-ring.png`

- Dimensions: 64×64, square radial sprite; non-tiling.
- VFX Graph use: Shockwave `Output Particle|Unlit|Quad` base-color map.

Prompt:

> Create a 64×64 real-alpha PNG sprite for a three-quarter top-down action game: crisp, high-saturation low-resolution pixel-art arcane shockwave ring. Subject centered and wholly within bounds: a thin broken circular ring of electric cyan and vivid blue magic, a few tiny outward pixel nicks, bright white-blue inner highlights, hollow transparent centre, outer edge fading quickly into minimal cobalt fragments. Clear circular silhouette at combat zoom; no filled explosion, smoke, flame, or soft glow. Mostly opaque pixels with very few semi-transparent edge pixels. No text, border, UI, checkerboard, matte, or scene background. Negative prompt: painterly, photorealistic, blurred, anti-aliased, 3D-rendered, soft bloom.

## `arcane-spark-clusters.png`

- Dimensions: 64×64, 2×2 flipbook atlas; non-tiling. Each 32×32 cell is one radial spark-cluster variation.
- VFX Graph use: second root `Output Particle|Unlit|Quad` base-color map; random `texIndex` selects 1 of 4 variants.

Prompt:

> Create a 64×64 real-alpha PNG 2×2 sprite atlas for a three-quarter top-down action game. Divide image into four equal 32×32 cells with no separator pixels. Every cell contains one distinct crisp radial arcane spark cluster: 5–8 sharp white-blue and electric-cyan pixel shards, some near centre and some near outer radius, arranged to read as sparks flying outward when the whole sprite expands. Vary shard count, spacing, and angular grouping per cell; retain centred circular silhouette. Cobalt-blue dim tails only; no large central glow, ring, smoke, flame, or background. Keep every cluster wholly inside its cell. Mostly opaque pixels with very few semi-transparent edge pixels. No text, border, UI, checkerboard, matte, or scene background. Negative prompt: painterly, photorealistic, blurred, anti-aliased, 3D-rendered, soft glow.
