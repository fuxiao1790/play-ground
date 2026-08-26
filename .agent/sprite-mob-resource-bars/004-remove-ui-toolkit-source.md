# 004 — Remove UI Toolkit Source Path

## Change

After user confirms task 003 and read-only verification succeeds, agent removes
source-controlled old implementation files that are not Inspector-authored
runtime wiring:

- `Assets/Scripts/Ui/WorldLabels/MobResourceBarUi.cs`;
- world-label UXML/USS source files and their folder;
- stale world-label comments in HUD USS;
- now-unused `PlayGround.Sim`, `Unity.Collections`, and `Unity.Entities`
  references from `PlayGround.Ui.asmdef`, after reference search confirms no
  remaining UI consumer.

Asset deletion that should flow through Unity Project window, including
`WorldLabelsPanel.asset` and its meta, remains user-owned in task 003. Agent does
not edit scene/prefab/meta serialization while performing cleanup.

Keep `GameSettings`, pause-menu toggle, and persistence code because sprite bars
consume them.

## Acceptance Criteria

- Repository contains one mob-resource-bar runtime implementation.
- No compiled code references `MobResourceBarUi`, UI Toolkit world-label
  elements, camera projection, or marker pooling.
- UI assembly compiles without simulation/Entities references.
- Agent changed no `.prefab`, `.unity`, `.asset`, or Inspector-generated `.meta`
  file.

## Dependencies

- 003 complete and verified read-only.

## Scope / Complexity

Small: source deletion, comments, and assembly-reference cleanup.

