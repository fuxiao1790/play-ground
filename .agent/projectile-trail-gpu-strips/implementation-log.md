# Implementation Log

## Status
In progress — blocked on user editor + test work for task 001

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-verify-vfx-strip-bridge.md | Blocked (partial) | Compute shader + C# probe/tests written by agent. Fixture `.vfx` graph must be hand-built by user (AgentVFX is read-only); tests must be run by user. |
| 002-shape-and-authoring.md | Pending | |
| 003-projectile-command-lifecycle.md | Pending | |
| 004-batched-dispatch.md | Pending | |
| 005-gpu-allocator.md | Pending | |
| 006-graph-and-preview.md | Pending | |
| 007-validation-and-docs.md | Pending | |

## Completed Tasks
- None fully complete. Task 001's agent-doable portion is done (see below);
  task itself stays open until the user builds the fixture graph and runs the
  PlayMode tests, and the agent records the findings in `index.md`.

## Files Changed
- `Assets/Tests/TestAssets/ProjectileTrailStripBridgeProbe.compute` (new)
- `Assets/Tests/PlayMode/ProjectileTrailStripBridgeProbe.cs` (new)
- `Assets/Tests/PlayMode/ProjectileTrailStripBridgeProbeFixture.cs` (new)
- `Assets/Tests/PlayMode/ProjectileTrailStripBridgeTests.cs` (new)
- `.agent/projectile-trail-gpu-strips/implementation-context.md` (new)
- `.agent/projectile-trail-gpu-strips/runtime/001-execution-packet.md` (new)
- `.agent/projectile-trail-gpu-strips/runtime/001-editor-plan.md` (new)
- `.agent/projectile-trail-gpu-strips/runtime/001-graph.dot` (new) — fixture
  graph diagram, per the `vfx-graph` skill's Graphviz DOT conventions

## Blockers
- **AgentVFX bridge is read-only** (`.agent/vfx-graph.md`): it can inspect an
  existing graph but has no create/save/node-edit/connection-edit capability.
  Building `Assets/Tests/TestAssets/ProjectileTrailStripProbe.vfx` is
  therefore user/editor work, per
  `runtime/001-editor-plan.md`. Not something a future agent turn can do
  differently — this is a standing tool limitation, not a one-off gap.
- **Tests are user-run, never agent-run** (`Docs/testing.md`,
  `Docs/project-overview.md` §Agent instructions). Once the fixture graph
  exists, the user must run `ProjectileTrailStripBridgeTests` in PlayMode and
  export XML under `Logs/`; the agent reads that XML to report pass/fail.
- The exact Custom HLSL block parameter-binding syntax needed for the debug
  write in the fixture graph (step 5 of the editor-plan) is unverified — the
  bridge can't confirm inspector-generated syntax, and there's no existing
  Custom HLSL block in this project to copy from. Flagged in the editor-plan
  as a stop-and-report point if it doesn't compile as written.

## Validation Summary
- Compile-only check via AgentVFX bridge (`unity command recompile` +
  `recompile_status`), not a test run: caught and fixed two real bugs before
  handoff — missing `using UnityEngine.TestTools;` (`[UnityTest]` unresolved)
  and `GraphicsBuffer.GetData` has no `NativeArray<T>` overload in this Unity
  version (switched `ReadDebugSamples` to return `uint2[]`). One pre-existing
  console error remains (`VFXViewWindow.CloseIfNotLast`
  `InvalidOperationException` at 2026-09-17T00:52, before this session's
  changes) — unrelated to this task, not investigated further.
- `.agent/vfx-graph.md` was updated (new "Context orientation" section) after
  `001-graph.dot` was first written: `rankdir=TB` (was `LR`), no legend inside
  `graph.dot` (palette lives in the skill only), each block-bearing Context
  rendered as a tall vertical card (header + blocks in one strict top-to-
  bottom chain via invisible high-weight edges, same `group`), operators
  placed beside the exact block/port they feed via `rank=same` +
  `constraint=false`, HTML-like table labels with named `PORT`s so edges
  target the exact input port, and Event kept as a plain trigger node outside
  any Context cluster rather than its own numbered cluster. Rebuilt
  `001-graph.dot` to match; tag/brace balance checked (no Graphviz binary
  available in this environment to render it directly).
- User caught a real rendering bug from that rebuild: `rank=same` grouping
  operator nodes (declared outside any cluster, per the skill) together with
  Initialize-cluster-internal block nodes made Graphviz draw the "Initialize
  Particle Strip" cluster boundary split/incorrectly (Set Attribute blocks
  appeared to spill outside their own context box). Removed the four
  `rank=same` groups; wiring edges (`constraint=false`) still connect
  operators to their blocks, just without forced row alignment. This is a
  general Graphviz constraint worth remembering for future `graph.dot` work
  under the updated `vfx-graph` skill: rank=same must not cross a cluster
  boundary.
- Design correction from user review: the fixture originally exposed an
  invented `BatchTags`/no-`TrailPositions`/no-`TrailWidths`/no-`PointLifetime`
  contract instead of the real `ProjectileTrail` shape (index.md decisions 3
  and 5). Fixed: fixture now exposes `ResolvedStripIndices`, `TrailPositions`,
  `TrailWidths`, `PointLifetime`, `SpawnCount` matching the real contract
  exactly, plus one clearly-separate test-only `DebugStripSamples` buffer
  (unavoidable — Unity has no public API to read a graph's internal
  particle-attribute state back to C#). Compute shader, C# probe, tests, and
  `001-editor-plan.md`/`001-graph.dot` all updated together. Final
  `recompile_status` after the fix: `"compilationFailed": false`.
- No PlayMode test run yet; no XML exists. That step is user-run per
  `Docs/testing.md`, after the fixture graph is built.

## Notes For Later Tasks
- `index.md`'s "Open dependencies" section says the AgentVFX bridge
  "currently reports no reachable server" — that's now stale. Bridge is
  reachable (`unity command vfx_ping` succeeded against port 7800 this
  session). It remains read-only regardless of reachability, which is the
  actual constraint for every later task that touches graph authoring
  (002, 006).
- Confirmed via bridge inspection of `Assets/Vfx/LineSeg/MagicBoltTrail.vfx`
  and `vfx_type_describe`: `Initialize Particle Strip` context exists with a
  plain `stripIndex` input slot (no special dynamic-mode toggle), `Sample
  Graphics Buffer` operator exists for buffer-by-index sampling, `Custom
  HLSL` block exists and is valid in Init/Update/Output. These are real,
  confirmed building blocks for tasks 002 and 006, not assumptions.
