# Task Execution Packet

## Task

003-scene-wiring-and-verification.md

## Goal

Wire the completed `ResourceBarUi` component to the existing `GameUI` object in `BenchmarkLarge` through the Unity Inspector and run its play-mode checklist.

## Files Allowed To Modify

- None by file edit. Unity scene/prefab wiring must be performed in the Inspector by the editor user.

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- `Assets/Scenes/BenchmarkLarge.unity`
- `Assets/Scripts/SkillUi/ResourceBarUi.cs`
- `Assets/Scripts/Ui/ResourceBars/ResourceBarsUi.uxml`

## Behavior To Preserve

- Existing `GameUI` document, skill UI, gameplay input surface, and scene content.

## Behavior To Change

- Inspector-only: add and configure `ResourceBarUi` on the existing `GameUI` object, then save the scene.

## Relevant Global Context

- Never hand-edit scene YAML or `.meta` files.
- Assign the same scene `PlayerRoot` used by current UI controllers and assign the resource-bars UXML template.
- The visual widget must remain bottom-left and click-through.

## Dependencies Confirmed

- `ResourceBarUi` exists in `PlayGround.SkillUi`, requires `UIDocument`, has `playerRoot` and `resourceBarsTemplate` serialized fields, and queries the completed named template elements.
- `ResourceBarsUi.uxml` exists.

## Step-By-Step Instructions

1. Open `Assets/Scenes/BenchmarkLarge.unity` in Unity.
2. On `GameUI`, add `ResourceBarUi` alongside `UIDocument`, `SkillLoadoutUi`, and `GameplayInputSurface`.
3. Assign `Player Root` to the existing scene PlayerRoot and `Resource Bars Template` to `ResourceBarsUi.uxml`.
4. Save the scene.
5. Enter Play Mode and perform every checklist item in the assigned task file, including click-and-hold over the widget.

## Acceptance Criteria

- Both references are assigned on the GameUI component with no missing-reference warning.
- Visual placement/update/regen/death behavior and click-through gameplay input all pass.

## Validation Required

- A Unity-editor manual play-mode pass. Automated file inspection cannot prove Inspector wiring or pointer input delivery.

## Hard Boundaries

- Do not edit scene YAML, `.meta` files, code, UXML, or USS.
- Do not substitute static checks for the required manual click-through and play-mode validation.
