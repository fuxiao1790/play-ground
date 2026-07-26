# Task Execution Packet

## Task

001-resource-bar-template.md

## Goal

Create the inert UXML/USS template for bottom-left player health and mana progress bars.

## Files Allowed To Modify

- `Assets/Scripts/Ui/ResourceBars/ResourceBarsUi.uxml` (new)
- `Assets/Scripts/Ui/ResourceBars/ResourceBarsUi.uss` (new)

## Files Allowed To Create

- The two files above.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- `Assets/Scripts/Ui/SkillLoadout/SkillLoadoutUi.uxml`
- `Assets/Scripts/Ui/SkillLoadout/SkillLoadoutUi.uss`
- `Docs/ui.md`

## Behavior To Preserve

- The current shared UI panel, skill bar, gameplay pointer routing, and debug overlay placement.

## Behavior To Change

- Add an uninstantiated resource-bar template; no runtime behavior changes yet.

## Relevant Global Context

- UXML defines names and static structure; USS owns layout/color/size; C# will bind data later.
- Use `ui:ProgressBar`, not a custom fill or inline styles.
- Place at bottom-left, never top-left or bottom-center.
- Apply `picking-mode: ignore` to the container and all widget descendants.

## Dependencies Confirmed

- None. This is the first task.

## Step-By-Step Instructions

1. Create `ResourceBarsUi.uxml` with a stylesheet reference and exactly `#resource-bars`, `#health-bar`, and `#mana-bar` using `ui:ProgressBar`.
2. Create `ResourceBarsUi.uss` to position and stack the widget bottom-left, give health/mana distinct fill colors and shared dark tracks, keep titles readable, and make all elements click-through.

## Acceptance Criteria

- The UXML has no inline style attributes and the three required element names.
- The stylesheet provides distinct health/mana fills and suitable bottom-left placement.
- Every widget element ignores picking.

## Validation Required

- Static inspection of named elements, stylesheet reference, absence of inline styles, bottom-left layout, variant fills, and click-through rules.

## Hard Boundaries

- Do not modify files outside the two listed new assets.
- Do not add C# or scene wiring.
- Do not introduce architecture or abstractions beyond the template.
