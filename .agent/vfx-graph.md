---
name: vfx-graph
description: Inspect existing Unity VFX Graph assets via AgentVFX CLI output, and plan new Unity VFX Graphs for described combat effects -- including the ECS data-shape contract, editor graph DOT diagram, and matching pixel-art texture prompts.
user-invocable: true
---

# VFX Graph

Two related capabilities live in this skill:

- **Inspect** an existing VFX Graph asset through AgentVFX CLI output.
  Read-only; no authoring or mutation.
- **Plan** a new or revised VFX Graph for a described combat effect. Planning
  and authoring guidance only: never create or edit a `VisualEffectAsset`,
  prefab, C# code, or texture.

## Inspecting an Existing Graph

Use AgentVFX for every VFX Graph analysis. Do not open a `.vfx` file as text
and do not infer graph structure from screenshots.

First verify Unity connection:

```powershell
unity pipeline list
unity command vfx_ping
```

Inspect authored graphs only under `Assets/Vfx/`:

```powershell
unity command vfx_graph_read --assetPath "Assets/Vfx/LineSeg/MagicBoltTrail.vfx" --json
```

`vfx_graph_read` returns nodes, node settings, root and nested slots, data
connections, context flow connections, and exposed blackboard properties.
`valueJson` represents primitives directly; curves, gradients, and asset
references return structured JSON.

Use `vfx_slot_read <slotId>` only for a slot id returned by a fresh graph read.
Use `vfx_types_list` and `vfx_type_describe <typeId>` to understand current
Unity VFX Graph types.

This bridge is read-only. It has no create, save, node-edit, slot-write,
connection-edit, blackboard-edit, compile, or preview commands. If required
information is not present in command output, report the limitation; do not
fall back to screenshot or `.vfx` serialization inspection.

The planning workflow below relies on this bridge's node/operator/property
names as the source of truth for the exact Unity VFX Graph names it must use,
verbatim, in its `graph.dot` diagrams -- never paraphrase a name sourced from
here.

## Planning a New Graph

Turn a user's desired VFX description into an editor-ready Unity VFX Graph
plan. This is planning and authoring guidance only: do not create or edit a
`VisualEffectAsset`, prefab, C# code, or texture.

When a plan depends on an existing graph, ground it with the AgentVFX
inspection path above. Do not read `.vfx` serialization or infer graph
structure from screenshots. AgentVFX is read-only: use it to ground the plan,
not to author the graph.

Create all deliverables under `./.agent/<task_name>/`. Pick a short,
kebab-case `<task_name>` from effect name. If folder already exists, add to its
existing plan; do not replace user files.

### First: Ground Current Contract

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

### Gather Visual Fidelity Before Planning

Before creating any deliverable, gather all 8 fidelity axes below. Graph
complexity (Context/Operator/Block count) must come from these axes, never
from the user's stated complexity request alone -- a user asking for a
"simple" effect with high-fidelity answers still gets the node count that
fidelity demands, and a "make it awesome" request with low-fidelity answers
does not.

1. Reference anchor -- real-world or game reference this effect should evoke.
2. Silhouette/shape -- overall readable shape at combat zoom.
3. Motion character -- how it moves/evolves over its lifetime.
4. Color/light-over-time -- how color and brightness evolve across lifetime.
5. Material/shading style -- surface/light look (flat, glowing, glassy, etc.).
6. Secondary phenomena -- sparks, smoke, embers, distortion, debris, etc.
7. Quality tier/budget -- particle count and texture budget tier.
8. Iterative deltas -- what should change versus a prior version of this
   effect, if one exists.

Ask all 8 axes in a single batched question (one `AskUserQuestion` call
covering every axis), never as sequential one-by-one round trips. Give each
axis concrete multiple-choice options; never leave an axis as an open-ended
free-text prompt.

If the request is vague with no concrete detail to anchor these axes (e.g.
"make it fancy", "make it cooler"), do not guess a specific answer: ask the
user to provide more detail instead.

The user may decline to answer some or all axes. When they do, you may fill
those axes yourself using reasonable defaults grounded in the request and
`Docs/visual/style-reference.md`. Every filled-in assumption must be stated
explicitly in `index.md` before the graph is produced; never silently guess
an axis and proceed.

### Choose Data Shape From Runtime Meaning

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

### Non-Negotiable Buffer Lifetime Rule

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

### Deliverables

Create these files:

```text
./.agent/<task_name>/
|-- index.md
|-- editor-plan.md
|-- graph.dot
`-- texture-prompts.md
```

#### `index.md`

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

#### `editor-plan.md`

Describe exact editor construction in execution order. Name graph contexts,
operators, blocks, custom particle attributes, exposed properties, event/burst
setup, texture/material inputs, and connections. Distinguish:

- request-buffer values sampled only in `Initialize Particles`;
- persistent attributes used downstream;
- artistic controls such as curves, random ranges, and blend mode.

Specify every block's intent, not only its title. Give the artist explicit
values/ranges when user description supports them; otherwise mark them as
deliberate tuning knobs rather than inventing a final value.

#### `graph.dot`

Write valid Graphviz DOT only -- never D2, Mermaid, or any other diagram
language, for this or any other VFX Graph diagram this skill produces.
Produce exactly one diagram in exactly one `graph.dot` file: never split the
graph across multiple files, however large it gets. As complexity grows,
tune layout manually (rank/rankdir hints, explicit rank groups, `nodesep`,
`ranksep`, node ordering) instead of splitting into multiple diagrams.

Map every diagram element 1:1 to a real Unity VFX Graph concept -- Context,
Block, Operator, Exposed Property, or trigger edge. Do not add a diagram-only
node that has no corresponding thing the artist places in the editor.

- **Contexts**: render each as a cluster/container, numbered by creation
  order (`1. Spawn`, `2. Initialize Particle`, `3. Update Particle`,
  `4. Output Particle`, plus any further Contexts this effect actually uses,
  continuing the same creation-order numbering).
- **Blocks**: number top to bottom inside their Context, matching the exact
  stack order the artist will build in the editor.
- **Operators**: wire each operator into the exact block and exact input
  port it feeds, with the edge labeled by that input/property name -- never
  a generic "feeds into" edge.
- **Exposed Properties**: one node per exposed property, wired to every
  block/operator input that consumes it.
- Use the exact current Unity block/operator names from the VFX Graph block
  library (e.g. "Set Velocity from Direction & Speed", not a paraphrase like
  "apply velocity"). Verify an uncertain name with the inspection path above
  (`vfx_types_list`, `vfx_type_describe`, or a `vfx_graph_read` of a
  comparable existing graph) rather than guessing.

### Context orientation

Graph layout must match VFX Graph's Context orientation. This requirement is
about node placement, not a new colour scheme. Keep the fixed element palette
below in this skill; do not duplicate a palette or legend in `graph.dot`.

| Element | Fill | Stroke |
|---|---|---|
| Context container | `#FFFFFF` | `#333333` |
| Block node | `#FFFFFF` | `#333333` |
| Operator node | `#FFE8A3` | `#8A6D00` |
| Exposed Property node | `#BFE3FF` | `#0B5A9E` |
| Event/Trigger node | `#D8F5C0` | `#2E7D32` |
| Normal data-flow edge | n/a | `#333333`, weight 1 |
| Cross-system trigger edge | n/a | `#C0392B`, weight 2, bold |

Each Context must be a tall, narrow container like its Unity editor node:

- Context header/settings at top; Blocks form one compact, strictly vertical
  stack below it, in editor stack order. Blocks must never spread across a row
  or occupy separate columns inside their Context.
- Preserve every editor containment relationship. When AgentVFX reports a
  Block, nested Block, slot, setting, or foldout as belonging to a parent
  Block, render it inside that parent Block's HTML-like table/record label,
  with visible indentation and a nested border/row. Do not promote it to a
  sibling Block or a free-floating node. A connected nested input gets a named
  port on its nested row, so the external edge terminates at the actual child
  input while the child remains visibly inside its parent. Only top-level
  Context Blocks participate in the Context's vertical ordering chain.
- Only the Context and its Blocks belong inside its cluster. Operators,
  Blackboard properties, and Events stay outside the Context.
- Give every Context its own cluster/container; never put the full Event-to-
  Output lifecycle in one outer cluster. A particle-system boundary, when one
  is useful, encloses those Context clusters only and must not become a layout
  column itself.
- Under the graph's left-to-right layout, put Context header and top-level
  Blocks in an internal `rank=same` subgraph so they occupy one vertical
  column. Use a high-weight, invisible, `constraint=false` ordering chain
  (`Context header -> Block 1 -> Block 2 -> ... -> Block N`) to keep their
  top-to-bottom order; use the same DOT `group`, `minlen=1`, and compact
  `nodesep` so the cluster reads as one vertical editor card.
- Use HTML-like DOT labels/ports where needed: input ports on a Block's left,
  output ports on its right, and Context flow ports at the Context top/bottom.
  Edge labels sit beside their actual target port, never above the whole
  Context.

Use `digraph` with `rankdir=LR` and three fixed visual lanes, left to right:

1. **Inputs** -- Blackboard/exposed properties, Events, and source Attribute
   reads. Keep this lane on the far left.
2. **Computations** -- sampling, conversions, math, branching, and composed
   attribute values. Keep each computation between the inputs it consumes and
   the Block input it drives; do not interleave it with Contexts.
3. **Systems** -- Spawn, Initialize, Update, and Output Context containers.
   Keep them in one far-right vertical lane, in lifecycle order. Their internal
   Block stacks remain vertical.

Create one `rank=same` subgraph per lane and use invisible, high-weight,
left-to-right guide edges from Inputs through Computations to Systems. Within
the Systems lane, use another invisible, high-weight, `constraint=false` chain
to keep Spawn, Initialize, Update, and Output top-to-bottom. Make ordinary
data and context-flow edges `constraint=false`; lane guide edges, not wiring
density, control placement. Draw context flow from Event to Spawn to
Initialize, then particle lifetime flow through Update and Output. Label data
edges with the sampled buffer/property or persistent attribute name. Make
unsafe paths visibly absent: no request-buffer node may connect to Update or
Output. The DOT is an implementation blueprint, not decorative art: list the
actual Contexts, Operators, and Blocks selected for this effect in node labels,
using their exact editor names.

#### `texture-prompts.md`

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

### Finish

Report task folder path, selected shape, and artifact list. Do not claim the
graph was built or visually validated. Do not run Unity tests; name any needed
manual/editor or automated checks for user to perform.
