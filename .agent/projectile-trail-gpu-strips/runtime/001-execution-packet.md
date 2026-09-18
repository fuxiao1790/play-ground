# Task Execution Packet

## Task
001-verify-vfx-strip-bridge.md

## Goal
Prove, in a disposable fixture (not the production graph), that a compute
shader write to a GraphicsBuffer immediately before `SendEvent` is visible to
that same batch's VFX Graph `Initialize Particle Strip`, that an invalid
resolved index never touches a live strip, and that a later batch never
rewrites an older batch's still-alive points. Record findings (safe reuse
delay, culled/paused behavior) in `index.md` before any later task relies on
them.

## Files Allowed To Modify
- None (new-only task).

## Files Allowed To Create
- `Assets/Tests/TestAssets/ProjectileTrailStripBridgeProbe.compute` (done)
- `Assets/Tests/PlayMode/ProjectileTrailStripBridgeProbe.cs` (done)
- `Assets/Tests/PlayMode/ProjectileTrailStripBridgeProbeFixture.cs` (done)
- `Assets/Tests/PlayMode/ProjectileTrailStripBridgeTests.cs` (done)
- `Assets/Tests/TestAssets/ProjectileTrailStripProbe.vfx` — **user-built in
  editor**, per [001-editor-plan.md](001-editor-plan.md) and its diagram
  [001-graph.dot](001-graph.dot); AgentVFX is read-only and cannot
  create/edit graphs.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`, `CombatAoeVfxDispatcher.cs`,
  `VfxDataShapes.cs` (buffer/dispatch conventions reused by the probe)
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`,
  `Assets/Tests/PlayMode/CombatAtlasTestFixture.cs` (test/fixture conventions
  reused by the probe)
- `Docs/testing.md` (test running/XML rules)

## Behavior To Preserve
- No production code touched by this task; it is a standalone proof only.

## Behavior To Change
- N/A (additive only).

## Relevant Global Context
See `../implementation-context.md`. Directly relevant here: "VFX Graph 17.4's
`Initialize Particle Strip` rejects a point before reservation when
`stripIndex >= STRIP_COUNT`" and "Request GraphicsBuffers are transient; only
Initialize may sample them."

## Dependencies Confirmed
- None (task 001 has no prerequisite task).
- AgentVFX bridge reachability re-checked this session: **reachable**
  (`unity command vfx_ping` succeeded, port 7800) — `index.md`'s "Open
  dependencies" note that the bridge "currently reports no reachable server"
  is stale and should be corrected.
- AgentVFX bridge is read-only (`.agent/vfx-graph.md`): confirmed it cannot
  create/edit/save a `.vfx` asset. Graph authoring is therefore user/editor
  work, not agent work, per this project's own skill contract and the
  standing "editor steps are user steps" rule.

## Step-By-Step Instructions
1. (Done, agent) Write the compute shader, C# probe driver, asset-loading
   fixture, and the three named PlayMode tests — see Files Allowed To Create.
2. (User, editor) Build `ProjectileTrailStripProbe.vfx` exactly per
   [001-editor-plan.md](001-editor-plan.md).
3. (User) Run `ProjectileTrailStripBridgeTests` in PlayMode, export XML under
   `Logs/` per `Docs/testing.md`.
4. (Agent, after user reports XML path) Read the XML, report pass/fail per
   test, and update `index.md`'s design-validation section with whatever the
   run establishes (safe reuse delay, same-frame ordering behavior, culled
   behavior) or with what failed and needs a plan revision.

## Acceptance Criteria
- Dynamic per-element `stripIndex` demonstrably selects independent strips.
- Invalid sentinel does not touch slot 0 or another live strip.
- Compute-written index reaches same batch's Initialize on target APIs.
- Old points retain position/width/slot while later batches vary (this
  fixture proxies "position/width" with the `batchTag` debug sample, since
  the fixture has no trail-position payload — that's production scope, not
  this scheduling proof).
- Safe lifetime/staging assumptions recorded in `index.md`.

## Validation Required
User-run PlayMode tests (`ProjectileTrailStripBridgeTests`), XML reviewed by
the agent per `Docs/testing.md`. Agent does not run tests itself.

## Hard Boundaries
- Do not modify files outside the allowed list.
- Do not create/edit/save the `.vfx` asset via script or by hand-editing its
  serialized YAML — editor steps are user steps.
- Do not claim a pass without reading exported result XML.
- Stop and report if the Custom HLSL buffer-binding syntax in the editor-plan
  doesn't match what Unity's inspector actually offers — revise the plan,
  don't improvise a workaround.
