# 006 — Docs And Tests

## Docs

- **`Docs/contracts/render-batch-data.md`** — rewrite the submission section: one
  `Graphics.RenderMeshIndirect` per update over a shared `StructuredBuffer<CombatInstanceData>`
  indexed by `SV_InstanceID`; remove all "1023 cap / chunked `RenderMeshInstanced` / per-instance
  `MaterialPropertyBlock`" wording (now false). Keep the Restrictions section intact
  (domain from `ProjectileTag`/`AoeTag`, faction from `CombatFaction`, never render state; pooling
  never keys on `CombatRenderBatchId`) — this rework does not change any of that.
- Add a short "Per-instance data channel" note stating *why* indirect is used: instanced
  rendering cannot carry a per-instance UV rect (MPB is uniform; extra struct members ignored),
  so the atlas UV must travel in the `StructuredBuffer`. Prevents a future reader from
  "simplifying" back to `RenderMeshInstanced`.
- Do **not** edit the `.agent/combat-render-atlas/` docs — that plan is left as-is by direction.

## Tests

The PlayMode fixtures already build a real `CombatRoot` + registry and load
`CombatAtlasTest.spriteatlasv2` via `CombatAtlasTestFixture`. Indirect submission itself
(GPU draw) isn't observable from PlayMode asserts, so keep test scope to what *is* verifiable:

- **Reuse the fixture unchanged** — atlas configuration/registration path is identical; the shader
  swap and buffer plumbing don't change `Register(...)` or the components.
- **Instance-data round-trip (new, cheap):** a test that scatters a couple of known active
  entities and asserts the produced `CombatInstanceData` carries the expected `objectToWorld`
  (matching `CombatRenderMatrixUtility.ElementFor`) and `uvRect` (matching the registry entry).
  This guards the matrix+UV packing without needing a GPU readback. Factor `Scatter` (or a pure
  helper it calls) so it can be exercised without issuing a draw.
- **Existing matrix-scale assertions** (`AoePlayModeTests` `(0.06f, 0.08f)` etc.) are unaffected —
  they test `ElementFor`/`GetAoeRenderComponent`, which this plan does not touch. Confirm they
  still pass; do not re-derive.
- **Draw-call count** is verified manually in the Frame Debugger (one combat draw), not in an
  automated test — note this in the plan's verification checklist.

## Acceptance Criteria

- `render-batch-data.md` describes the indirect/single-draw design with no stale
  instanced/1023/MPB wording, Restrictions preserved.
- PlayMode suite compiles and passes; the new instance-data round-trip test asserts matrix+UV
  packing.
- Manual Frame Debugger check documented: exactly one combat draw call.

## Dependencies

003 + 004 (system/material must be final). 005 is content, independent.

## Scope

Small.
