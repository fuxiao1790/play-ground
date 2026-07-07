# 008 - Docs and Verification

## Scope

`Docs/reference/simulation/combat-render-system.md`,
`Docs/contracts/render-batch-data.md`, manual/PlayMode verification.

## Doc Changes

**`combat-render-system.md`:**
- Summary: replace "shared unit-quad mesh" /
  "recorded inside a URP `ScriptableRendererFeature`" language with the baked
  capacity-mesh + `MeshRenderer` + Sorting Layer mechanism.
- Data Flow diagram: replace the `CombatIndirectRenderFeature`/
  `RecordRenderGraph`/`DrawMeshInstancedIndirect` steps with
  `Mesh.SetSubMesh` + normal `DrawRenderer2DPass` submission.
- Key Types: update `CombatIndirectRenderData` entry (remove — no longer
  exists) and note the new owned `MeshRenderer`/baked mesh on
  `CombatRenderResourceRegistry`.
- Critical Constraints: remove the "URP 2D Renderer requires a RendererFeature"
  constraint (no longer true for this system) and the associated
  `RenderMeshInstanced`/`RenderMeshIndirect` abandonment note needs a caveat
  that the *baked-mesh-through-a-real-Renderer* approach is different from
  those (which were rejected because they were immediate-mode calls with no
  scene `Renderer`, not because a real `MeshRenderer` can't work).
- Add a note on the Sorting Layer contract: `CombatSprites` is expected to
  contain only this one renderer; other content should not be added to that
  layer expecting per-object interleaving with individual projectiles/AOEs,
  since the whole batch is one atomic `Renderer`.

**`render-batch-data.md`:**
- Update the "one `DrawMeshInstancedIndirect` per update" / "recorded inside a
  URP `ScriptableRendererFeature`" language in Fields/Shape and Ordering
  sections to reflect the `MeshRenderer`/`SetSubMesh` mechanism.

## Verification (manual/PlayMode — harness cannot run Unity)

- **Frame Debugger**: confirm exactly one draw call for the combat sprite
  batch (searchable by the `MeshRenderer`'s GameObject name), matching the
  "one draw call" invariant.
- **Visual overlap test**: place VFX, an active combat sprite, the player,
  and a mob so their bounds overlap on screen. Confirm draw order bottom to
  top: VFX, combat sprite, player/mob (player and mob still Y-sorting against
  each other).
- **Growth test**: spawn past `InitialInstanceCapacity` (1024) active
  projectiles/AOEs simultaneously; confirm no visual corruption during the
  mesh-capacity doubling rebuild (matches existing instance-buffer growth
  behavior — should be seamless, same frame).
- **Idle-cost check**: with zero active entities, confirm no per-frame cost
  regression from the always-present `MeshRenderer` (should be near-zero:
  0-index submesh, standard Unity culling skips it) — relevant given
  [[project_idle_combat_system_cost]].
- **Regression check**: confirm removing `CombatIndirectRenderFeature` from
  `Renderer2D.asset` (006) doesn't leave any other system expecting it.

## Dependencies

Last task — depends on 001 through 007 all being complete.

## Complexity

Small for docs; verification is manual/PlayMode (per this project's
plan-then-verify workflow — the harness cannot build/run Unity itself).
