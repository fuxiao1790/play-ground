---
name: scene-wiring-and-verification
description: Editor wiring for ResourceBarUi and play-mode verification
---

# 003 — Scene Wiring and Verification

## Depends On

002-resource-bar-controller.md (component must exist to add to the scene).

## Scope

This is an **editor/user step**, not a hand-edited-YAML step — per project
convention, Unity scene/prefab wiring goes through the Inspector, not direct
scene-file edits (`ui.md:148-149`: "Editor wiring is a user/editor operation.
Do not hand-edit scene YAML or meta files to simulate Inspector
assignments."). The instructions below are for whoever drives the Unity
Editor (the user), not something to script through file edits.

## Steps

1. Open the reference scene, `Assets/Scenes/BenchmarkLarge.unity` (the same
   scene `SkillLoadoutUi` is wired into per
   [ui.md:46](../../Docs/ui.md#L46)).
2. On the existing `GameUI` object (the one already carrying the `UIDocument`
   + `SkillLoadoutUi` + `GameplayInputSurface`), add the new `ResourceBarUi`
   component.
3. Assign its fields in the Inspector:
   - `Player Root`: the scene's `PlayerRoot` instance (same object
     `SkillLoadoutUi.playerRoot` and `GameplayInputSurface.playerRoot` already
     point to).
   - `Resource Bars Template`: the `ResourceBarsUi.uxml` asset from task 001.
4. Save the scene.

## Play-Mode Verification Checklist

- Enter play mode. Confirm the HP and MP bars appear bottom-left, both at
  full value at spawn, without overlapping the skill bar (bottom-center) or
  the debug overlay text (top-left, if `PerformanceText`/`DebugOverlay` is
  active in this scene).
- Take damage (or otherwise reduce health) and confirm the health bar's fill
  and title text (`current/max`) update within a frame, with no visible
  stutter or rebuild flicker.
- Confirm regen is visible: let health/mana sit below max and confirm the bar
  fills gradually at the authored regen rate rather than snapping.
- **Click-through check** (the invariant from
  [ui.md:98-103](../../Docs/ui.md#L98-L103) that task 001 encoded via
  `picking-mode: ignore`): move the cursor over the resource bar area and
  click-and-hold. Confirm `GameplayInputSurface` still receives the
  click-to-fire input (the player fires/aims normally) instead of the click
  being swallowed by the HUD widget. This is the one behavior that's easy to
  get wrong silently (works visually, breaks input) — do not skip it.
- Let health reach zero and confirm the bar shows empty/`0/max` without
  throwing (covers `PlayerRoot`'s post-death state where `Update()` still
  runs but returns early after `QueueCombatTargetProxyDelete()`).
- Confirm no console errors/exceptions from `ResourceBarUi` on scene load,
  play, or stop.

## Acceptance Criteria

- `ResourceBarUi` is wired on `GameUI` in `BenchmarkLarge.unity` with both
  fields assigned (no missing-reference warnings on scene load).
- All checklist items above pass, especially the click-through check.

## Estimated Scope

Small — Inspector wiring plus a manual playtest pass; no code changes.
