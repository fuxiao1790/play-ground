# Task 001 editor plan — `ProjectileTrailStripProbe.vfx`

Diagram: [001-graph.dot](001-graph.dot) — render it (e.g. Graphviz `dot -Tsvg`
or a `.dot` viewer) for the full node/wiring picture; the steps below are the
same content in construction order. Built per the
[vfx-graph](../../vfx-graph.md) skill's diagramming conventions, adapted to
this task's `runtime/` layout since this plan already owns
`.agent/projectile-trail-gpu-strips/index.md` as its master brief.

This is editor-only work: AgentVFX has no create/edit/save capability (read-only
bridge, see [.agent/vfx-graph.md](../../vfx-graph.md)), so this graph must be
built by hand in the Unity editor. Every node/context/operator/setting name
below was verified against the installed `com.unity.visualeffectgraph@17.4.0`
via `vfx_type_describe`/`vfx_types_list`, or read directly off the production
graph `Assets/Vfx/LineSeg/MagicBoltTrail.vfx` via `vfx_graph_read` — nothing
here is guessed. The one open question (exact `m_HLSLCode` syntax for a
Custom HLSL block that writes an `RWStructuredBuffer`) is flagged where it
comes up; the bridge cannot resolve it because it's read-only and this
project has no existing Custom HLSL block to read for a template.

Asset path (must match exactly — the test fixture loader hard-codes it):
`Assets/Tests/TestAssets/ProjectileTrailStripProbe.vfx`

## 1. Create the asset

Create a new empty Visual Effect Graph at that path and open it.

## 2. Exposed properties (blackboard)

This fixture must exercise the **real** `ProjectileTrail` contract
(index.md architecture decisions 3 and 5; task 002's planned
`VfxDataShapeTable` entries), not a simplified stand-in — the point of task
001 is to prove the real buffer set/types/stride work, not a narrower one.
Add these exposed properties, matching
`ProjectileTrailStripBridgeProbe.cs`'s property-name constants exactly
(case-sensitive):

| Name | Type | Notes |
|---|---|---|
| `ResolvedStripIndices` | GraphicsBuffer | uint, per-spawnIndex, compute-written each dispatch — real property |
| `TrailPositions` | GraphicsBuffer | float2, per-spawnIndex, compute-written each dispatch — real property. This proof has no real trail geometry, so the compute shader stuffs the dispatch's batch tag into both components instead; see the debug read-back in step 5. |
| `TrailWidths` | GraphicsBuffer | float, per-spawnIndex, compute-written each dispatch — real property, constant dummy value here |
| `PointLifetime` | Float | set once by the probe's constructor, not per batch — real property (index.md: "set on the graph by registration", not a per-request buffer) |
| `SpawnCount` | Int32 | same convention as every other combat VFX graph |
| `DebugStripSamples` | GraphicsBuffer | uint2 (`stripIndex`, `(uint)position.x`), one entry per strip slot, written by the graph itself, read back by the test — **test-only, no production equivalent** (see step 5) |

The first five mirror `Assets/Vfx/LineSeg/MagicBoltTrail.vfx`'s blackboard
pattern exactly (`StartPositions`/`EndPositions`/`Widths`/`SpawnCount` as
GraphicsBuffer/Int32 exposed Parameters wired straight into Spawn/Initialize)
— same pattern, real `ProjectileTrail` buffer set. `DebugStripSamples` is the
one addition: Unity exposes no public API to read a graph's internal
particle-attribute state back to C#, so this is the only way the PlayMode
tests can observe what Initialize actually did with the real buffers above.

## 3. Event + Spawn context

1. Add an **Event** context (`VFXBasicEvent`), set `eventName` = `OnSpawn`.
2. Add a **Spawn** context, `loopDuration`/`loopCount` = `Infinite` (defaults).
   Connect Event's flow output into Spawn's flow input.
3. Add a **Single Burst** block inside Spawn. Set `repeat` = `Single`,
   `spawnMode` = `Constant`. Wire the exposed `SpawnCount` parameter directly
   into the burst's Count input slot (exactly like `node:19` -> `node:22`
   slot 43 in `MagicBoltTrail.vfx`).

## 4. Initialize Particle Strip context

Add context **Initialize Particle Strip** (`VFXBasicInitialize`, confirmed
`dataType` = `ParticleStrip` mode — pick the "Initialize Particle Strip"
entry from the context creation menu, not plain "Initialize Particle").
Connect Spawn's flow output into it.

Context settings (visible in its Inspector, confirmed setting names:
`capacity`, `stripCapacity`, `particlePerStripCount`, `needsComputeBounds`,
`boundsMode`):
- `capacity`: 32 (total particle/point budget; generous headroom over the
  test's `StripCapacity=4 * a handful of points each`)
- `stripCapacity`: 8 (>= the C# test's `StripCapacity = 4`, so slot indices
  0-3 the test uses are always valid; extra headroom costs nothing here)
- `particlePerStripCount`: 4
- `needsComputeBounds`: off; `boundsMode`: Manual, with a generous fixed
  bounds box (e.g. center 0, size 100) so culling never hides the probe.

This context has a confirmed plain input slot named **`stripIndex`** (not a
mode toggle — just a normal input pin taking an `int`/`uint`). Wire it from a
**Sample Graphics Buffer** operator:
- `buffer` input <- exposed `ResolvedStripIndices` property
- `index` input <- a **Get Attribute: spawnIndex** operator (`Current`
  location) — this is `Operator:UnityEditor.VFX.VFXAttributeParameter` with
  `attribute` = `spawnIndex`, confirmed present in `MagicBoltTrail.vfx` as
  `node:23` ("Get|_Spawn Index|Current") feeding the same buffer-sample
  pattern.
- Sample Graphics Buffer's `m_Type` setting: `uint` (so its output `s`
  matches `stripIndex`'s type directly with no cast node needed).

Add two more **Sample Graphics Buffer** operators the same way, both reusing
the same spawnIndex operator for `index`:
- `buffer` <- exposed `TrailPositions`, `m_Type` = `Vector2` (float2). Feed
  its output into a **Set Attribute** block, `attribute` = `position`
  (built-in particle attribute, `Composition` = `Overwrite`) — this is
  exactly the real "request buffer -> Initialize -> persistent attribute"
  copy the `vfx-graph` skill's non-negotiable buffer lifetime rule requires,
  using the same `position` attribute the real trail graph will use to place
  points.
- `buffer` <- exposed `TrailWidths`, `m_Type` = `float`. Feed into a **Set
  Attribute** block, `attribute` = `width` (built-in), `Composition` =
  `Overwrite`.

Add a **Set Attribute** block, `attribute` = `lifetime` (built-in),
`Source` = `Slot`, wired from the exposed `PointLifetime` float property
directly (not a Sample Graphics Buffer — it's a plain per-graph value, set
once by the probe's constructor, not per request).

Per index.md's non-negotiable buffer lifetime rule: all three Sample
Graphics Buffer nodes belong only in this Initialize context. Nothing
downstream (Update/Output) may read `ResolvedStripIndices`, `TrailPositions`,
or `TrailWidths` directly — only the persistent `position`/`width`/`lifetime`
attributes they were copied into.

## 5. Debug write (Custom HLSL block, Init context, after strip reservation)

Add a **Custom HLSL** block (`Block:UnityEditor.VFX.Block.CustomHLSL`,
confirmed valid in `Init`/`Update`/`Output` contexts) as the **last** block in
Initialize Particle Strip, after the `position`/`width`/`lifetime` blocks
above — ordering matters: this block only observes state for particles the
context actually reserved a strip point for, which is exactly the "did an
invalid index get rejected before reservation" signal task 001 needs. This
block, and the `DebugStripSamples` buffer it writes, have **no production
equivalent** — they exist only so the PlayMode test can read back what the
real `position`/`stripIndex` attributes above actually ended up holding.

Intent (write this in the block's HLSL code, adjust to whatever exact
parameter-binding syntax Unity's Custom HLSL inspector expects once you open
it — the bridge cannot confirm this syntax since it's read-only and there's
no existing Custom HLSL block in this project to read as a template; **stop
and report back here if the signature below doesn't compile** so the plan can
be corrected with the real syntax rather than guessed further):

```hlsl
void WriteDebugStripSample(in RWStructuredBuffer<uint2> DebugStripSamples, in uint stripIndex, in float3 position)
{
    DebugStripSamples[stripIndex] = uint2(stripIndex, (uint)position.x);
}
```

Bind its three parameters (see `001-graph.dot` for the exact wiring):
- `DebugStripSamples` <- the exposed `DebugStripSamples` GraphicsBuffer
  property directly.
- `stripIndex` <- a **Get Attribute: Strip Index** operator (`Current`
  location; confirmed operator id `Get|_Strip_Index`), not the raw
  Sample-Graphics-Buffer output from step 4 — reading it back as a particle
  attribute means this block only ever sees particles that actually reserved
  a strip point, which is the exact signal task 001 needs.
- `position` <- a **Get Attribute: position** operator (`Current` location)
  reading the built-in attribute set from `TrailPositions` above. Its `.x`
  component holds this dispatch's batch tag (the compute shader wrote
  `float2(tag, tag)`, since this proof has no real trail geometry).

## 6. Update + Output (only need to compile; content is irrelevant to the proof)

- **Update Particle** context (plain "Update Particle" — there is no separate
  strip-specific Update context; strip-ness is carried by the data type set
  in Initialize). No blocks required.
- **Output**: add whatever Particle Strip output your editor's creation menu
  offers (Unlit/Lit/Shader Graph quad — pick the simplest one that compiles;
  the bridge only confirmed `Output ParticleStrip|Shader Graph|Quad` exists
  in this project's current node cache, but any strip-shaped output context
  satisfies the graph's compile requirement). Visual appearance does not
  matter for this proof — only that the graph compiles and runs.

## 7. Save, and sanity-check before handing back to the agent

- Compile the graph (should be automatic/no errors).
- Confirm the blackboard exposes exactly `ResolvedStripIndices`,
  `TrailPositions`, `TrailWidths`, `PointLifetime`, `SpawnCount`,
  `DebugStripSamples` with the types in the table above — the test's
  `VisualEffect.SetGraphicsBuffer`/`SetInt`/`SetFloat` calls fail silently
  (Unity logs a warning, does not throw) if a name or type doesn't match.
- Then hand back to run `ProjectileTrailStripBridgeTests` per the task's
  Tests line — PlayMode, exported XML under `Logs/`.

## If Custom HLSL turns out not to give buffer read-back this way

If the Custom HLSL block cannot bind an `RWStructuredBuffer` parameter the
way step 5 assumes, do not improvise a workaround — this is exactly the kind
of "Unity GPU/VFX scheduling... main unknown" task 001 exists to surface.
Report back what the editor's Custom HLSL inspector actually offers (its
generated parameter list/signature once a function is typed in) and the plan
will be revised — per task 001's own acceptance criterion: "if any fail,
revise plan before continuing with runtime implementation."
