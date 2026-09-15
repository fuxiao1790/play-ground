---
name: vfx-graph-plan
description: Plan a Unity VFX Graph for a described combat effect, including the ECS data-shape contract, editor graph DOT diagram, and matching pixel-art texture prompts.
user-invocable: true
---

# VFX Graph Plan

## Purpose

Turn a user's desired VFX description into an editor-ready Unity VFX Graph
plan. This is planning and authoring guidance only: do not create or edit a
`VisualEffectAsset`, prefab, C# code, or texture.

When a plan depends on an existing graph, inspect it with the `vfx-graph-agent`
skill and AgentVFX CLI output. Do not read `.vfx` serialization or infer graph
structure from screenshots. AgentVFX is read-only: use it to ground the plan,
not to author the graph.

Create all deliverables under `./.agent/<task_name>/`. Pick a short,
kebab-case `<task_name>` from effect name. If folder already exists, add to its
existing plan; do not replace user files.

## First: Ground Current Contract

Read these before planning:

- `Docs/project-overview.md`
- `Docs/reference/simulation/vfx-system.md`
- `Docs/reference/simulation/vfx-shared-graph-area-size-corruption.md`
- `Docs/visual/style-reference.md`
- current `Assets/Scripts/System/Vfx/VfxDataShapes.cs`
- current dispatcher/root code named by VFX-system doc when a property or
  registration rule needs confirmation

Docs describe intended design and can lag code. Treat the current
`VfxDataShapeTable` and dispatcher validation as authority for exact enum,
property, and event names. Record any meaningful doc/code mismatch in
`index.md`; do not silently use a stale alias.

Extract from user description: visual purpose, element/palette, gameplay
geometry, trigger/slot if known, expected lifetime, scale, motion, and whether
it needs textures. Infer only details implied by description. If shape choice
or source geometry is load-bearing and not clear, ask one concise question
before creating deliverables.

## Choose Data Shape From Runtime Meaning

Choose shape because of emitter payload and graph lifetime, never because a
visual label sounds suitable.

- One-shot radial/location effect: current circular/impact shape.
- A circular effect emitted once but self-driven for authored AOE duration:
  current timed-circular/lingering shape. It must use copied `Duration` and
  `TickInterval` particle data to drive its own pulses.
- Directional chain link or distance-gated projectile trail: `LineSegment`.
  This is its own start/end/width contract. It is not an AOE shape.

Use exact current enum names and required exposed properties from code in the
plan. Include `SpawnCount` (`int`) and `OnSpawn` for every supported shape.
Never add an unregistered `GraphicsBuffer` property: registration requires the
shape's exact buffer set. If requested geometry cannot be expressed by an
existing producer and shape, state that plainly in `index.md` as an integration
change required before graph authoring; do not invent a fallback graph.

## Non-Negotiable Buffer Lifetime Rule

Request graphics buffers are transient current-dispatch staging data. One graph
asset can be shared by multiple skill sets, and a later dispatch overwrites its
buffers while old particles are alive.

For every request value the graph needs:

```text
request buffer -> Initialize Particles -> persistent particle attribute
persistent particle attribute + age/lifetime/random/curves -> Update/Output
```

In `Initialize Particles`, clamp current-batch `spawnIndex` with current
`SpawnCount`, sample each required buffer, and copy the values into persistent
attributes. `Update Particle` and `Output Particle` must not sample any request
buffer, use `spawnIndex`, or depend on `SpawnCount`. For area effects, preserve
the stored base area scale and multiply it by size-over-life; do not let an
Output `Set Size` overwrite it.

## Deliverables

Create these files:

```text
./.agent/<task_name>/
|-- index.md
|-- editor-plan.md
|-- graph.dot
`-- texture-prompts.md
```

### `index.md`

Make this authoritative brief. Include:

- user request and assumptions/questions resolved;
- selected data shape, intended registration slot/emitter, exact exposed
  property/event contract, and why it fits;
- visual direction: gameplay silhouette, palette, lifetime, density, and
  readability at combat zoom;
- hard runtime rules: one `OnSpawn` batch event, buffer-to-attribute copying,
  no gameplay decisions inside graph, and mixed-area-size risk where relevant;
- integration gaps or changes required, if any;
- manual validation checklist. For every per-request graph, require an
  old-particles-alive mixed-value test using one graph asset, different payload
  values, varied batch counts, and varied request ordering.

### `editor-plan.md`

Describe exact editor construction in execution order. Name graph contexts,
operators, blocks, custom particle attributes, exposed properties, event/burst
setup, texture/material inputs, and connections. Distinguish:

- request-buffer values sampled only in `Initialize Particles`;
- persistent attributes used downstream;
- artistic controls such as curves, random ranges, and blend mode.

Specify every block's intent, not only its title. Give the artist explicit
values/ranges when user description supports them; otherwise mark them as
deliberate tuning knobs rather than inventing a final value.

### `graph.dot`

Write valid Graphviz DOT that maps the VFX Graph as it should appear in Unity
editor. Use `digraph`, left-to-right layout, readable rounded nodes, and
clusters for:

1. external exposed properties and `OnSpawn` event;
2. Spawn context and burst count;
3. Initialize Particles;
4. Update Particle;
5. Output Particle.

Show flow from event to spawn to Initialize, then particle lifetime flow through
Update and Output. Label data edges with sampled buffer/property or persistent
attribute names. Make unsafe paths visibly absent: no request-buffer node may
connect to Update or Output. Include a small legend distinguishing transient
payload from persistent particle data. The DOT is an implementation blueprint,
not decorative art: list the actual operators and blocks selected for this
effect in node labels.

### `texture-prompts.md`

Give one standalone image-generation prompt for each recommended additional
texture (core sprite, trail strip, radial mask, noise sheet, etc.). Do not
generate images. Avoid duplicate prompts when one texture can serve multiple
uses. For each prompt state intended filename, dimensions/aspect ratio, VFX
Graph use, and whether it should tile.

Every prompt must match `Docs/visual/style-reference.md`:

- crisp, high-saturation, low-resolution 2D pixel art for a three-quarter
  top-down action game;
- bright readable core, colored aura/trail, limited transparent edge detail;
- element palette: blue arcane/ice, orange-red fire, green nature/poison,
  violet void, gold/white holy or high-impact neutral, unless user specifies a
  justified override;
- clear silhouette at combat zoom, with secondary detail dimmer than impact,
  hazard rim, targets, and projectile paths;
- actual alpha PNG, no text, border, UI, checkerboard, matte, or scene
  background; mostly opaque pixels and very few semi-transparent edge pixels.

For a seamless texture, explicitly request edge-to-edge tiling. For a sprite,
explicitly request subject centered and wholly within bounds. Include a concise
negative prompt when it prevents likely failure, such as soft painterly,
photorealistic, blurred, or 3D-rendered output.

## Finish

Report task folder path, selected shape, and artifact list. Do not claim the
graph was built or visually validated. Do not run Unity tests; name any needed
manual/editor or automated checks for user to perform.
