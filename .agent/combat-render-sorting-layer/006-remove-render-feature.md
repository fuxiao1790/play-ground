# 006 - Remove the Old Render Feature

## Scope

`Assets/Scripts/System/Common/CombatIndirectRenderFeature.cs`,
`Assets/Settings/Renderer2D.asset`.

## Change

- Delete `CombatIndirectRenderFeature.cs` in full (both the
  `CombatIndirectRenderData` static handoff and the
  `CombatIndirectRenderFeature`/`CombatIndirectRenderPass` classes) — nothing
  references it once 005 lands.
- In the Editor, open `Renderer2D.asset`'s Inspector and remove the
  **Combat Indirect Render Feature** entry from its Renderer Features list
  (added originally per
  [combat-render-system.md:144-146](../../Docs/reference/simulation/combat-render-system.md#L144-L146)).
  Do this via the Inspector, not a hand-edit of the `.asset` YAML, to avoid
  corrupting the renderer feature list's internal GUID references.

## Acceptance Criteria

- No compile references to `CombatIndirectRenderFeature`/
  `CombatIndirectRenderData` remain anywhere in the project.
- `Renderer2D.asset`'s Renderer Features list no longer contains the combat
  render feature.
- Combat sprites still render correctly (now via the `MeshRenderer` path) with
  the feature removed — confirms the old path was fully superseded, not just
  dead code left behind.

## Dependencies

Depends on 005 (nothing may still call `CombatIndirectRenderData.Publish`/
`Clear` when this file is deleted).

## Complexity

Trivial code deletion + one manual Editor step.
