Generate a 64 * 64 RGBA PNG particle sprite texture for a top-down 2D pixel game.

Effect: Arcane Projectile

Use case: sprite rendering in a game engine with particle system integration.

Style: crisp low-resolution pixel-art, clean silhouette, limited palette, chunky game-friendly forms; no soft realistic detail.

Requirements: simple, readable, centered, fully inside bounds, no text/borders/shadows, only requested colors. Keep it legible after shader/material tinting and color modulation. Use mostly opaque pixels and very few semi-transparent edge pixels for performance.

Transparency: real alpha PNG. If the design contains no black pixels, use a solid black background (fully opaque). Otherwise use full transparency outside the effect. No matte/checkerboard/background. Output only the texture asset.
