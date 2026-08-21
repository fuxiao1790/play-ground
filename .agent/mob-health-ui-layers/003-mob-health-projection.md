# 003 — Add Pooled Mob Health Projection

## Change

Add UI Toolkit mob health bars to `#labels-layer` without changing combat or mob
health ownership.

## Assets And Controller

- Add `Assets/Scripts/Ui/WorldLabels/MobHealthBar.uxml` for one repeated bar.
- Add `Assets/Scripts/Ui/WorldLabels/MobHealthBar.uss` or colocated shared world
  label styles for dimensions, background, fill, borders, and anchor layout.
- Add `Assets/Scripts/Ui/WorldLabels/MobHealthBarUi.cs` in `PlayGround.Ui`.
- Configure through serialized references:
  - `CombatRoot`
  - gameplay `Camera`
  - `VisualTreeAsset` health bar template
  - world-space offset above mob
- Validate all required references in `Awake` and `#labels-layer` in `OnEnable`.

## Data And Lifecycle

- Read `combatRoot.TargetRegistry.Targets` in `LateUpdate`.
- Filter targets with `target is MobRoot mob` and active Unity-object validity.
- Use one dictionary from `MobRoot` to presentation entry, one stack of reusable
  entries, and one reusable stale-key list or equivalent generation marking.
- Acquire on first registered observation; fully reset visual state on acquire.
- Release when target disappears from registry, becomes destroyed, or controller
  disables.
- Do not subscribe to `SpawnController`, add static events, or store copied health.
- Give controller execution order after `GameplayCamera.LateUpdate`.

## Projection And Rendering

- Build anchor from mob world position plus authored offset.
- Reject behind-camera and out-of-viewport anchors before showing view.
- Convert visible anchor with
  `RuntimePanelUtils.CameraTransformWorldToPanel(labelsLayer.panel, world, camera)`.
- Move a zero-size marker with UI Toolkit transform data so motion does not force
  full panel layout.
- Center bar above marker through authored USS transform/offset.
- Set fill from `Mathf.Clamp01(mob.CurrentHealth / mob.MaxHealth)`.
- Keep bar visible at full health for initial behavior; death/unregister removes it.
- Set marker, bar, fill, and every template descendant to
  `PickingMode.Ignore` explicitly.
- Avoid updating fill/style values when unchanged.

## Acceptance Criteria

- Every active, registered, on-screen mob has exactly one health bar.
- Bar tracks final camera and mob position without one-frame camera lag.
- Damage reflected in `MobRoot.CurrentHealth` changes fill proportion.
- Offscreen/behind-camera mobs have hidden views.
- Soft death unregisters and recycles view.
- Pooled mob reuse shows full refreshed health and no stale position/fill.
- Bars render above click-to-fire and below HUD/popups/pause layers.
- Bars never intercept pointer events.
- Steady-state frames allocate no managed garbage from target reconciliation or
  projection.
- `MobRoot`, `SpawnController`, and Sim code gain no UI dependency.

## Dependencies

- Depends on task 001 for `#labels-layer`.
- Scene activation depends on task 004 wiring.

## Scope / Complexity

High: new pooled projection controller, repeated template, camera culling, and
mob-pool lifecycle reconciliation.

