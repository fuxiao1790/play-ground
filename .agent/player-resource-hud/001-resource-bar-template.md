---
name: resource-bar-template
description: Author the UXML template and USS for the HP/MP HUD bars
---

# 001 — Resource Bar Template (UXML + USS)

## Depends On

None. First task.

## Scope

Author the stable visual structure and styling for the HP/MP HUD widget. No
C# in this task — the template is inert until task 002 instantiates and binds
it.

## Files

- `Assets/Scripts/Ui/ResourceBars/ResourceBarsUi.uxml` (new)
- `Assets/Scripts/Ui/ResourceBars/ResourceBarsUi.uss` (new)

Follows the same file-layout convention as the skill UI reference
(`Assets/Scripts/Ui/SkillLoadout/SkillLoadoutUi.uxml` +
`SkillLoadoutUi.uss`).

## Structure

This is a template asset, not a panel root — it is not assigned as any
`UIDocument`'s Source Asset. Task 002's controller instantiates it and adds it
as a sibling of `#bar` directly into the shared panel's `rootVisualElement`
(same pattern `SkillLoadoutUi` uses for its picker modal).

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" editor-extension-mode="False">
    <Style src="ResourceBarsUi.uss" />
    <ui:VisualElement name="resource-bars" class="resource-bars">
        <ui:ProgressBar name="health-bar" class="resource-bar resource-bar--health" />
        <ui:ProgressBar name="mana-bar" class="resource-bar resource-bar--mana" />
    </ui:VisualElement>
</ui:UXML>
```

Use `ui:ProgressBar`, not a hand-built fill (`VisualElement` + inline
`style.width`). Its `value`/`lowValue`/`highValue` are data properties the
controller sets in task 002; the control computes its own fill width
internally, keeping the "C# sets runtime values, USS owns visual values" rule
intact ([ui.md:48-61](../../Docs/ui.md#L48-L61)) — no `style.*` assignment
needed in the controller.

## Styling Requirements

- Position: bottom-left corner (`position: absolute; left: 16px; bottom:
  16px;` on `.resource-bars`), stacked vertically (health above mana), each
  the full width of the container with a small gap between them.
  Not top-left: that's reserved for `DebugOverlay`
  ([coding-standards.md:372-374](../../Docs/coding-standards.md#L372-L374)).
- Two color variants via `.resource-bar--health` /
  `.resource-bar--mana`, each overriding
  `.unity-progress-bar__progress` (the built-in fill sub-element) to a
  distinct color (e.g. red-ish for health, blue-ish for mana) and
  `.unity-progress-bar__background` to a shared dark track color. Reuse the
  same rgba-conversion convention already documented at the top of
  `SkillLoadoutUi.uss`.
- The `ProgressBar`'s built-in title text (set in task 002 to `"123/200"`
  style current/max) should remain legible — style
  `.unity-progress-bar__title` color/font-size as needed.
- **Click-through is required.** Add `picking-mode: ignore` to `.resource-bars`
  and to every element inside it (`.resource-bar`,
  `.unity-progress-bar__background`, `.unity-progress-bar__progress`,
  `.unity-progress-bar__title`, or equivalently a broad descendant selector
  covering all of them). This is not optional polish: any element under the
  top UI layer that doesn't ignore picking silently swallows
  `GameplayInputSurface` clicks under its bounds
  ([ui.md:98-103](../../Docs/ui.md#L98-L103)). Task 003's acceptance criteria
  re-verify this in play mode.

## Acceptance Criteria

- `ResourceBarsUi.uxml` references `ResourceBarsUi.uss` and declares exactly
  the `#resource-bars` container with `#health-bar` and `#mana-bar`
  `ui:ProgressBar` children, matching the names task 002 queries by.
- No inline `style="..."` attributes in the UXML (visual values live in USS
  per project convention).
- USS gives health and mana bars visually distinct fill colors and positions
  the widget bottom-left without overlapping the skill bar (bottom-center) or
  the debug overlay (top-left).
- Every element in the widget has `picking-mode: ignore` applied (verified
  visually in task 003, not testable from the template alone).

## Estimated Scope

Small — two new small text files, no logic.
