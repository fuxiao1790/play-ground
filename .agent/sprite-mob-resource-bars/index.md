# Sprite Mob Resource Bars

## Summary

Replace the UI Toolkit mob-resource-bar path with a world-space sprite path
authored directly in each mob prefab.

Each authored mob gets a `ResourceBar` child containing a focused
`MobResourceBarSprite` component plus cached background/fill `SpriteRenderer`
references. The child transform owns prefab-specific position and size. Parent
transform propagation moves the bar with the mob; no script projects the mob
through the camera or writes bar position each frame.

`MobRoot` remains health source of truth. It refreshes the child presenter from
the existing `Resource.Changed` notification, which already gates the call to
actual health changes only. The presenter writes fill scale directly from the
continuous ratio on each such call, preserves the existing
`GameSettings.DisplayMobHealthBars` option, and hides during soft death. No bar
component has `Update`, `LateUpdate`, target iteration, pooling, or scene search.

Agent edits code, tests, assembly definitions, and documentation only. User owns
all Unity Inspector/Project-window authoring: prefab hierarchy, component
addition/removal, Transform values, renderer configuration, sprite import
settings, scene objects, and serialized reference assignments. Agent must not
hand-edit `.prefab`, `.unity`, `.asset`, or Inspector-generated `.meta` content,
and must not run an editor builder to perform those changes indirectly.

## Major Decisions

### Use a child presenter, not a second polling controller

`MobResourceBarSprite` is a focused child `MonoBehaviour` because its Transform
and renderer layout must be visually adjustable per mob prefab. It owns only
presentation state: cached renderer references, authored full-fill scale,
lifecycle visibility, and settings subscription. It does not own health.

`MobRoot` holds the serialized child reference and forwards health changes. This
follows the root-component rule while keeping visual geometry out of `MobRoot`.

### Inherit motion; update fill on change

The resource-bar transform is a child of the mob root. Rigidbody/Transform
movement therefore propagates through Unity's transform hierarchy without a
managed world-to-panel projection or per-frame bar position assignment.

Fill uses a dedicated rectangular sprite with a left-center pivot, shared
across all three mob prefabs' fill renderers. Background uses a separate
dedicated rectangular sprite, shared across all three prefabs' background
renderers (two shared sprites total — one per role — not one tinted sprite;
see **Introduced** below). Full-fill local scale is captured once. Runtime
changes only the fill child's local X scale, written directly from the
continuous health ratio on every call. There is no step-quantization layer:
`Resource.Changed` already gates `SetHealth` calls to actual health changes
only, and pre-emptively coarsening those writes further was judged premature
optimization. Revisit only if the task 006 profiling pass shows per-tick
regen writes are a measured cost, not before.

### Preserve the existing display setting

Interpretation of the user's confirmation: sprite bars continue to obey
`DisplayMobHealthBars`. `GameRoot.gameSettings` is a plain, non-serialized
private field resolved once via `GetComponent<GameSettings>()` in `Awake`
(`GameSettings` always lives on the same GameObject as `GameRoot` by
convention) — not an Inspector reference at all, matching the existing
`MobRoot.skillDriver` precedent (also `GetComponent`-resolved, also not
`[SerializeField]`) rather than the manually-wired cross-GameObject fields
(`player`, `gameplayCamera`, `spawnController`, `playArea`) that genuinely
need either manual assignment or a scene-wide `Find*` fallback because they
live elsewhere. `GameRoot` then passes the resolved instance to static mobs
and `SpawnController`; `SpawnController` passes it to each rented mob. The
child presenter subscribes/unsubscribes at `OnEnable`/`OnDisable` and applies
changes without a frame loop.

This went through two iterations: first a manually-wired `[SerializeField]`
(which shipped unassigned in `BenchmarkLarge`, silently leaving every mob's
presenter defaulted to always-visible since `SubscribeSettings` no-ops on
null), then a `[SerializeField]` with a same-GameObject `GetComponent`
fallback, then finally dropping `[SerializeField]` entirely once it was clear
there is no case where this reference should ever point anywhere other than
`GetComponent<GameSettings>()` on `GameRoot`'s own GameObject — a serialized
slot for that is pure surface area for the exact misconfiguration that
already happened once, with no corresponding flexibility benefit.

Test fixtures or stripped worlds may omit a bar and settings; an absent child
bar means no presentation, while an authored production bar without a settings
binding defaults visible. A bare test `GameObject` with only `GameRoot` added
still resolves to null via `GetComponent` (no `GameSettings` component
present), so this behavior is unchanged for tests — only production scenes
where `GameSettings` is genuinely co-located benefit. Production prefab/scene
tests enforce the complete wiring in `BenchmarkLarge`.

### Remove the old implementation instead of retaining two paths

After the sprite path is wired, delete the old `MobResourceBarUi` controller,
world-label UXML/USS, dedicated panel asset, and empty `LabelUI` scene object.
Remove `MobRoot.resourceBarOffset` and `ResourceBarAnchorPosition`; the authored
child transform replaces that representation. Keep the pause-menu setting and
game-settings persistence because the new presenter consumes them.

### Keep Inspector-authored files user-owned

Implementation pauses after code APIs exist. Agent provides exact Inspector
steps, then waits for user to author and save prefabs/scene/assets. Agent may
inspect those files read-only afterward and report missing wiring, but may not
repair serialized data directly or through Unity editor automation.

## Constraints And Invariants

### Ownership and dependency direction

- Scene/authoring owns mob Transforms and `SpriteRenderer` presentation
  (`Docs/layers/scene-and-authoring.md`, **Owns**).
- `MobRoot` owns serialized prefab validation, runtime resource state, death,
  and local mob presentation coordination
  (`Docs/reference/game-logic/mobs.md`, **Runtime Ownership**; and
  `Docs/coding-standards.md`, **Root Component Rule**).
- ECS simulation must never read live Unity objects. Sprite changes remain on
  the managed presentation side
  (`Docs/layers/scene-and-authoring.md`, **Forbidden Dependencies**;
  `Docs/coding-standards.md`, **Hybrid ECS/Scene Rule**).

### Health source and frame ordering

- ECS finalization owns authoritative health. `CombatApplyBridge` resolves the
  managed target during `PresentationSystemGroup` and calls
  `ICombatTarget.ReceiveCombatTick` once per compact target result
  (`Docs/contracts/combat-hit-and-tick-results.md`, **Guarantees**;
  `Assets/Scripts/System/Presentation/CombatApplyBridge.cs`).
- `MobRoot` mirrors proxy health into its existing `Resource`; `Resource`
  publishes `Changed` only after an actual max/current change
  (`Assets/Scripts/Mob/MobRoot.cs`, `MirrorResourcesFromProxy` and
  `ReceiveCombatTick`; `Assets/Scripts/Common/Stats/Resource.cs`).
- Renderer writes happen only on the main thread in the child presenter. No ECS
  job receives a `SpriteRenderer`, Transform, or presenter reference.

### Lifecycle and pooling

- Cross-component subscriptions belong in `OnEnable` and must be removed in
  `OnDisable` (`Docs/coding-standards.md`, **Awake vs OnEnable Boundary**).
- `MobPool.Rent` reactivates and calls `InitializeForSpawn`; `MobPool.Return`
  deactivates and reparents the mob (`Assets/Scripts/Spawn/MobPool.cs`).
- Every rent must restore full/current fill and alive visibility; soft death
  must hide both renderers before pooled return. Reuse must not retain prior
  fill step or hidden state.

### Allocation and performance budget

- Mob update loops may not allocate; scene searches are forbidden on hot paths
  (`Docs/performance.md`, **Core Rules**; `Docs/coding-standards.md`,
  **Allocation Rule**).
- Mobs are low-count scene actors; current design target is roughly player plus
  fewer than 50 mobs, while `BenchmarkLarge` deliberately stresses up to 200
  (`Docs/performance.md`, **Runtime Strategy**;
  `Assets/Scenes/BenchmarkLarge.unity`, `SpawnController.cap`).
- Sprite path adds two authored renderers per mob. All mobs reuse one background
  sprite/material across their background renderers and one fill sprite/material
  across their fill renderers (two shared assets total, not one), so renderer
  batching remains possible within each role.
- No bar script runs per rendered frame solely because a mob moved. Fill writes
  occur on every actual health change (`Resource.Changed` gates this, not a
  frame loop); no additional visual-step quantization on top of that. Settings
  writes occur only when the setting changes.
- CPU and GPU/render cost must be measured separately; profiler CSV captures do
  not establish GPU cost (`Docs/profiling.md`, **World-Label Query Guidance**).

### Visual behavior

- Existing bars display continuously while enabled, including at full health;
  preserve that behavior.
- Background/fill use the old visual intent: dark background, red health fill,
  and approximately the old 36:5 aspect ratio
  (`Assets/Scripts/Ui/WorldLabels/MobResourceBar.uss`).
- Bar child local position is authored separately for Bat, Slime, and Skeleton.
- Bar renderers use the same sorting layer as mob visuals, with stable orders
  above the current mob visual order 9. Background sprite/material is
  consistent across all three prefabs' background renderers; fill
  sprite/material is consistent across all three prefabs' fill renderers.
  Ordering is consistent across all three prefabs.

## Mechanisms Reused Vs Introduced

### Reused

- `MobRoot` as actor resource/lifecycle owner.
- `Resource.Changed` as the single health-change notification.
- `GameSettings.DisplayMobHealthBars` and its focused change event.
- `GameRoot`/`SpawnController` dependency binding and `MobPool` rent/return
  lifecycle.
- Authored child Transform and `SpriteRenderer` components used by other actor
  visuals.

### Introduced

- `MobResourceBarSprite`: one focused prefab-child presenter. New type is
  justified because it removes camera projection, panel ownership, marker
  pooling, and UI Toolkit transform state while giving each prefab direct visual
  authoring control.
- Two shared rectangular sprite assets — one for background, one for fill —
  each reused across all three mob prefabs for its role. Distinct art per role
  (rather than one tinted sprite) was chosen by the user for authoring
  flexibility. Still avoids runtime sprite creation and per-mob material
  instances: two shared materials total, not one per prefab.

## Design Validation

- **One health source:** presenter receives ratios derived from `MobRoot`'s
  existing `Resource`; it holds no gameplay health or ECS state.
- **Main-thread safety:** all Transform/renderer work occurs through managed mob
  lifecycle and settings callbacks; ECS jobs remain data-only.
- **No motion loop:** parent hierarchy handles bar movement. Presenter declares
  no `Update`/`LateUpdate` and performs no camera call.
- **No allocation loop:** renderers, transforms, settings callback, and full-fill
  scale are cached. Ratio application performs scalar math only.
- **Pool correctness:** activation restores alive state and current ratio;
  deactivation removes settings subscription; death hides renderers.
- **Settings correctness:** existing setting remains source of truth. Mob and
  presenter do not create a second stored option.
- **Authoring correctness:** child transform replaces the old serialized offset,
  so there is one prefab-owned positioning representation.
- **Rendering pressure:** two renderers per active mob are explicit cost. Shared
  per-role assets (one background sprite/material, one fill sprite/material)
  and stable sorting allow batching within each role; profiling task compares
  this cost against both UI Toolkit and no-bar baselines.

## Minimal/Additive Vs Refactor Comparison

### Minimal/additive approach

- **Resulting data flow:** keep `MobResourceBarUi` polling every mob while adding
  a sprite child that also reads mob health.
- **New concepts/types introduced:** sprite presenter beside UI Toolkit marker,
  panel, pool, and settings consumer.
- **Copies/translations added:** two health-to-visual projections, two authored
  position representations (offset plus child transform), and toggle routing to
  two consumers.
- **Long-term cost:** both implementations, tests, docs, assets, and failure
  modes remain synchronized; accidental simultaneous rendering is possible.

### Refactor approach

- **Resulting data flow:** ECS health -> `MobRoot.Resource` -> existing
  `Resource.Changed` -> one child sprite presenter.
- **Existing concepts/types changed or removed:** remove world-label controller,
  marker pool, panel, UXML/USS, camera projection, serialized offset, and UI-only
  assembly dependencies. Retarget existing settings event to sprite presenter.
- **Copies/translations removed or avoided:** no per-frame target scan, no
  world-to-panel position conversion, no duplicate offset, no parallel bar
  models.
- **Long-term benefit:** one resource-bar path, prefab-local authoring, event-only
  visual changes, smaller UI assembly.

### Decision

- **Choose refactor.** It meets the performance experiment's purpose and leaves
  one source of truth and one runtime presentation path. Git history/profile
  captures provide the old comparison; old runtime code need not stay compiled.

Default decision rule applied: two representations or data paths for the same
mob health bar are not retained without a compatibility requirement.

## Tasks

1. [001 — Implement child sprite presenter](001-implement-sprite-presenter.md)
2. [002 — Bind health, settings, and pooled lifecycle](002-bind-health-settings-lifecycle.md)
3. [003 — User Inspector authoring](003-user-inspector-authoring.md)
4. [004 — Remove UI Toolkit source path](004-remove-ui-toolkit-source.md)
5. [005 — Replace resource-bar tests](005-replace-resource-bar-tests.md)
6. [006 — Update architecture and profiling docs](006-update-docs-and-profile.md)

## Open Questions And Considerations

- Confirmation interpreted as preserving the existing display setting. If
  “yes” meant always-on bars, task 002 and settings-related tests/docs must be
  simplified before implementation.
- No pass/fail frame-time threshold is documented. Task 006 produces comparable
  captures and reports main-thread, render-thread, batch, and draw-call deltas;
  user decides whether the experiment meets target hardware budget.
- Sprite bars are world-space. Unlike UI Toolkit bars, their apparent pixel size
  changes with camera zoom. This is intentional for a child-renderer experiment.
