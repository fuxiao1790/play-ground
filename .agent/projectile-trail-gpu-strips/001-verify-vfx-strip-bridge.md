# 001 — Verify VFX strip bridge

Scope: small proof in a disposable `Assets/Tests/` fixture graph and test
driver. No production graph migration yet. Complexity: medium; Unity GPU/VFX
scheduling is the main unknown. Dependency: none.

Work:

1. With a reachable AgentVFX editor bridge, inspect an existing trail graph
   and the test graph through `vfx_graph_read`. Use installed package source
   when bridge cannot expose an internal setting. Never read `.vfx` text.
2. Create a minimal Particle Strip graph in the editor: one `OnSpawn` burst,
   `SpawnCount`, a `GraphicsBuffer ResolvedStripIndices`, and a dynamic
   `stripIndex` input on Initialize. Set a small strip capacity and obvious
   per-slot colors. `uint.MaxValue` should reject a point.
3. Test a compute shader writing indices immediately before `SendEvent` with
   one shared `VisualEffect` instance. Alternate target slots and batch sizes
   while earlier points remain alive. Verify sampled value belongs to its
   birth batch. Cover D3D11 and each intended shipping graphics API.
4. Verify whether current-batch buffers can be reused on the following frame
   without racing deferred VFX Initialize. If needed, add a fixed ring of
   command/result staging buffers with GPU fences; do not add CPU readback.
5. Establish minimum safe reuse delay after final point, including VFX init
   latency, and behavior while graph is culled or paused. Choose `Always
   Simulate` for graph unless equivalent synchronized pausing is proven.

Acceptance:

- Dynamic per-element `stripIndex` demonstrably selects independent strips.
- Invalid sentinel does not touch slot 0 or another live strip.
- Compute-written index reaches same batch's Initialize on target APIs.
- Old points retain position, width, and slot while later batches vary.
- Safe lifetime and staging assumptions recorded in `index.md`; if any fail,
  revise plan before continuing with runtime implementation.

Tests: user runs proposed **PlayMode** `ProjectileTrailStripBridgeTests`
(`ComputeResolvedIndices_ArriveBeforeInitialize`,
`InvalidIndex_DoesNotTouchLiveStrip`,
`LaterBatch_DoesNotChangeOldPoints`). Export XML under `Logs/` per
`Docs/testing.md`; agent reviews XML before claiming a pass.
