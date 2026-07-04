# 005 — Tests + docs

## Tests
- `ProjectileAuthoringEditModeTests.cs:135`: `SizeOf<CombatRenderComponent>`
  `68 -> 32`. Add `SizeOf<CombatRenderAuthoring> == 16`. (Line 69
  `basicPrefab.VisualScale` is an unrelated authoring MonoBehaviour field — leave
  it.)
- `AoePlayModeTests.cs:740`: reads
  `GetComponentData<CombatRenderComponent>(entities[i]).objectToWorld`. Update to
  read the compact fields (`.Position` / `.Rotation`) or reconstruct the value it
  asserts on — read the surrounding assertion to see what it checks (likely
  position/scale) and adapt.
- Command-building tests that set `Render = new CombatRenderComponent { ...
  VisualScale = ... }` — `ProjectileSpawnPipelineTests.cs:431/435`,
  `CombatPoolCleanupSystemTests.cs:185/188`,
  `ProjectileCollisionSimulationTests.cs:408`: move `VisualScale` (and any
  sin/cos) onto a `CombatRenderAuthoring Authoring = new { BaseScale = ... }` on
  the command; keep `Render` for RenderMeta/RenderZ only.
- Run EditMode + PlayMode combat/render/spawn suites.

## Docs
- `Docs/reference/simulation/combat-render-system.md`:
  - Data Flow: prepare writes the compact 2D transform (2x2 basis + position +
    RenderZ), not a full TRS `objectToWorld`.
  - Key Types: `CombatInstanceData`/`CombatRenderComponent` stride **32**
    (`float4 Rotation` + `float3 Position` + `int RenderMeta`); note the new
    `CombatRenderAuthoring` (base scale/sin/cos, CPU-only, 16 B) and that base
    rotation/scale is no longer stashed in spare matrix cells. Update the
    `SizeOf == 68` assertion mention to 32.
  - The UV Basis / "World orientation is in the 4x4 objectToWorld matrix" —
    reword to "in the 2x2 rotation*scale basis + position".
  - Performance Notes: per-instance upload **68 -> 32 B**.
- `Docs/contracts/render-batch-data.md`: `CombatInstanceData` stride `68 -> 32`,
  compact 2D form; add `CombatRenderAuthoring` to the field list; note it is the
  zero-copy uploaded record (still `AddRange`, no repack).

## Memory
- Update [[project_render_indirect]] / add a note that the instance record is now
  a compact 2D transform (stride 32), and `CombatRenderComponent` split its base
  authoring into `CombatRenderAuthoring`.

## Depends on
001-004.
