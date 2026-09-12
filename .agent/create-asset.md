---
name: create-asset
description: Generate a pixel-art game asset image prompt (particle/effect, character, mob, environment/prop, or icon) following project visual style rules
user-invocable: true
---

# Create Asset

## Purpose
Produce a ready-to-use image-generation prompt for a single pixel-art game asset
texture, matching this project's visual style and output constraints. One
shared template covers every asset kind (particle/effect, character, mob,
environment/prop, UI icon) instead of a separate near-duplicate file per kind.

## Usage
Invoke with a kind and a short description, e.g.:
- "create-asset particle: Arcane Projectile"
- "create-asset mob: Bone-white skeleton warrior"
- "create-asset character: Player back-facing idle pose"
- "create-asset prop: Mossy stone crate"
- "create-asset icon: Fire skill icon"

## Inputs
- **Kind** (required): `particle/effect` | `character` | `mob` | `environment/prop` | `icon/UI`
- **Description** (required): the specific subject/effect/pose to depict
- **Size** (optional): pixel dimensions; use the kind's default below if one exists, otherwise ask
- **Palette override** (optional): specific colors to use instead of the kind's default guidance

## Clarify Before Generating
Kind and description are required. If either is missing, unclear, or
ambiguous from the invocation, stop and ask the user for it — do not guess a
kind or invent a subject. Ask for size too whenever the kind has no
established default (see table below) and none was given.

## Per-Kind Defaults
Read the matching section of
[style-reference.md](../Docs/visual/style-reference.md) before generating,
then apply:

| Kind | Default size | Style reference section | Palette guidance |
|---|---|---|---|
| particle/effect | 64x64 for radial/symmetric effects (bursts, impacts, auras); non-square, aspect-matched to the effect for elongated/directional effects (beams, chains/links, trails) — ask for dimensions if the aspect isn't obvious from the description | Skills And Effects | bright core + colored trail/aura; blue=arcane/ice, orange-red=fire, green=nature/poison, violet=void, gold/white=holy or high-impact neutral |
| character | no established default — ask | Characters | warm red/brown/gold player palette, bright blue magic accent; dark outline + small bright highlights |
| mob | no established default — ask | Mobs | one dominant palette per family (green slime, red mushroom, bone-white skeleton, green goblin, violet bat, brown treant, stone golem, blue wraith); must contrast player red/brown/blue |
| environment/prop | no established default — ask | Environment And Props | muted, repeatable palette for floor tiles; obstacles/collision need a visually distinct edge |
| icon/UI | no established default — ask | Combat Readability Checks | keep visually separate from authored pixel-art world assets |

64x64 is a project-confirmed default only for radial/symmetric particle/effect
assets, not a fixed size for the whole kind. Square art was never the
requirement in
[combat-atlas-tight-mesh-uv-distortion.md](../Docs/reference/simulation/combat-atlas-tight-mesh-uv-distortion.md) —
that doc's fix is importing combat-registered sprites as Full Rect (or filling
the rect edge-to-edge under Tight), which removes the UV-distortion risk at
any aspect ratio. So an elongated effect (e.g. a chain/link segment or beam)
should get dimensions matching its actual shape, not be forced square.
For every other kind, ask the user for size rather than guessing one.

## Shared Requirements (apply to every kind)
- Use case: sprite rendering in a game engine with sprite/particle system integration (or UI system for icons).
- Style: crisp low-resolution pixel-art, clean silhouette, limited palette, chunky game-friendly forms; no soft realistic detail.
- Simple, readable, centered, fully inside bounds, no text/borders/shadows, only requested colors. Keep legible after shader/material tinting and color modulation.
- Use mostly opaque pixels and very few semi-transparent edge pixels for performance.
- Transparency: real alpha PNG. If the design contains no black pixels, use a solid black background (fully opaque). Otherwise use full transparency outside the effect/subject. No matte/checkerboard/background. Output only the texture asset.

## Output
Compose the final prompt from: the kind's use case + style + palette guidance
+ the requested description + the shared requirements above, then run it
through the image generation step. Never drop the transparency/opaque-pixel
rules regardless of kind.
