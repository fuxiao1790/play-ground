# UI Architecture

This page is the main guide for UI in this project.

UI is presentation and input routing. UI shows state owned by Game Logic or
runtime services. UI sends user intent back through a small API. UI does not own
gameplay state, cooldowns, loadouts, combat authority, or ECS entities.

## Ownership

Scene And Authoring owns the Unity UI objects and presentation roots. This
includes `UIDocument`, UI Toolkit panels, `PanelSettings` (one asset per
distinct runtime panel — see Runtime Shape below), scene wiring, and the UI
input surface.

The `PlayGround.Ui` assembly owns feature-specific UI controllers. It may
read Game Logic state and send commands to Game Logic. The dependency direction
stays one way:

```text
PlayGround.Ui -> PlayGround.GameLogic -> PlayGround.Sim
```

Game Logic and ECS Simulation must not reference UI types. UI must not make ECS
simulation decisions or create ECS combat entities.

See [Layer Rules](./architecture/layer-rules.md) and [Scene And Authoring](./layers/scene-and-authoring.md)
for the wider boundary rules.

## Runtime Shape

The project uses Unity UI Toolkit at runtime, split across two independently
owned runtime panels:

- `HudPanel` (sort order `0`): click-to-fire, HUD (skill bar, player
  resources, performance/debug overlay), skill picker, pause background, and
  pause menu. Root document: `Assets/Scripts/Ui/Hud/SkillLoadoutUi.uxml`.
- `WorldLabelsPanel` (sort order `-10`): mob resource bars only. Root
  document: `Assets/Scripts/Ui/WorldLabels/WorldLabelsUi.uxml`.

A distinct `UIDocument` alone does not isolate a panel: two documents that
share one `PanelSettings` asset still render into one runtime panel. Each
panel above therefore has its own `PanelSettings` asset (`HudPanel.asset`,
`WorldLabelsPanel.asset`) with matching scale mode, reference resolution,
theme, target display, and atlas settings — only `sortingOrder` differs.
Higher `sortingOrder` draws later and is checked first for pointer events, so
`HudPanel` (`0`) draws over, and receives pointer events before,
`WorldLabelsPanel` (`-10`).

- `PanelSettings` controls how each panel is rendered.
- A root UXML file per panel describes stable visual structure and links its
  USS file.
- Small UXML templates describe repeated or dynamic pieces.
- USS owns visual styling.
- C# owns state binding, dynamic cloning, event wiring, and small runtime
  updates.

The current skill UI is the `HudPanel` reference implementation:

- Shared HUD root: `Assets/Scripts/Ui/Hud/` (`SkillLoadoutUi.uxml`,
  `SkillLoadoutUi.uss`, `ResourceBarUi.uss`)
- Skill-bar-private templates: `Assets/Scripts/Ui/Hud/SkillLoadout/`
- Shared skill catalogs: `Assets/Scripts/Skills/Authoring/` (consumed by UI
  and persistence)
- Runtime controller: `Assets/Scripts/Ui/Hud/SkillLoadout/SkillLoadoutUi.cs`
- Compiled assembly: `Assets/Scripts/Ui/PlayGround.Ui.asmdef`
- Sample scene wiring: `Assets/Scenes/BenchmarkLarge.unity` (`GameUI` object)

World labels are the `WorldLabelsPanel` reference implementation:

- World-label root: `Assets/Scripts/Ui/WorldLabels/` (`WorldLabelsUi.uxml`,
  `WorldLabelsUi.uss`, `MobResourceBar.uxml`, `MobResourceBar.uss`)
- Runtime controller: `Assets/Scripts/Ui/WorldLabels/MobResourceBarUi.cs`
- Sample scene wiring: `Assets/Scenes/BenchmarkLarge.unity` (`WorldLabelsUI`
  object, sibling of `GameUI`)

## UXML, USS, And C# Rules

Use UXML for stable hierarchy and named elements. Use USS for layout, colors,
spacing, sizing, opacity, and other visual values. Use C# for values that come
from runtime state.

The root UXML should remain small. The skill UI root contains `#bar`; C# clones
node, support, trigger, and picker templates into that container as needed.
This keeps variable counts data-driven without rebuilding the authored root.

Feature controllers should query named elements and classes from UXML. They
should not recreate the whole visual tree with `new VisualElement`, put visual
values in `style.*` assignments, or duplicate domain state in a second local
model.

## State And Commands

UI is a projection of an existing source of truth.

1. A Game Logic object owns the gameplay state.
2. The UI reads that state and binds labels, enabled states, visibility, and
   other presentation values.
3. A user action becomes a command or method call into the owning Game Logic
   object.
4. UI waits for the resulting event or state revision before refreshing.

For skill loadout editing, `SkillDriver` owns the loadout, validation, revision,
cooldowns, and edit result. `SkillLoadoutUi` displays that state and submits
edit commands. It does not optimistically change a local loadout copy.

See [Player Skill UI](./reference/design/player-skill-ui.md), [Skill Loadout Editing](./contracts/skill-loadout-editing.md),
and [Skill Loadout Edit](./flows/skill-loadout-edit.md).

## Input Layering

Input layering is split by panel, not by one shared layer stack.

`HudPanel` (sort order `0`) owns four named layers, defined directly in
`SkillLoadoutUi.uxml`. UXML child order is back-to-front (later siblings draw
over earlier siblings), so front-to-back reading order within `HudPanel` is:

```text
front: #pause-menu-layer     (PauseMenuUi)     - ignores picking; menu controls opt in
       #pause-background     (PauseMenuUi)     - hidden by default; visible + Position while paused
       #popup-layer          (SkillLoadoutUi)  - ignores picking; picker controls opt in
       #hud-container        (SkillLoadoutUi)  - existing HUD; ignores picking except controls
back:  #click-to-fire-layer  (GameplayInputSurface) - Position by default, the only click-to-fire source
```

`WorldLabelsPanel` (sort order `-10`) owns exactly one layer, defined in
`WorldLabelsUi.uxml`:

```text
#labels-layer (MobResourceBarUi) - always picking-ignore; world-projected mob resource bars
```

Because `HudPanel` sorts above `WorldLabelsPanel`, the runtime event system
checks `HudPanel` first. `#click-to-fire-layer` is therefore the effective
back layer across both panels, and `WorldLabelsPanel` never intercepts a
pointer event: its only content is picking-ignore.

Each layer is a full-screen root child of its own panel's
`rootVisualElement` (`.ui-layer` in `HudPanel`, `.labels-layer` in
`WorldLabelsPanel`). Controllers query their named layer and attach feature
content to it; no controller calls `root.Add(...)`, `root.Insert(...)`, or
`BringToFront()` to manage layer order. Sibling order within a panel is fixed
by that panel's authored UXML and does not depend on controller execution
order or on the other panel.

`GameplayInputSurface` is the only mouse click-to-fire source, and it lives in
`HudPanel`. It binds `#click-to-fire-layer` in `OnEnable`, receives pointer
down/up events, captures the pointer while held, and exposes `FireHeld`
through `IGameplayInputSource`.

`PlayerRoot` consumes that source and passes the result to `SkillDriver`. The UI
layer pushes the source into `PlayerRoot`; Game Logic does not look up or read
UI objects.

UI Toolkit picking decides whether a click reaches the world surface:

- Normal controls use position picking and consume their own clicks.
- The click-to-fire layer sits behind every other `HudPanel` layer, and
  behind all of `WorldLabelsPanel` because `HudPanel` sorts above it.
- Each higher `HudPanel` layer ignores picking by default
  (`picking-mode: Ignore`) so decorative content passes clicks through;
  interactive descendants opt back in explicitly, since `PickingMode.Ignore`
  does not propagate to children. `WorldLabelsPanel`'s `#labels-layer` and
  every mob-bar descendant stay picking-ignore always; the panel has no
  focusable or pickable control.
- While paused, `#pause-background` switches to `PickingMode.Position` and
  becomes visible, shielding `#popup-layer`, `#hud-container`, and — because
  it sits in the higher-sorted `HudPanel` — every `WorldLabelsPanel` element
  too, from pointer input; `#pause-menu-layer` stays above it so its controls
  remain interactive.
- Pointer capture keeps a held click stable until release or capture loss.

Opening the skill picker is a separate modal input mode. `SkillLoadoutUi` tells
`PlayerRoot` to suspend gameplay input while the picker is open, then clears the
suspension when it closes. This is not a replacement for UI layering.

Runtime UI pointer delivery requires the scene's UI event setup, normally an
`EventSystem` with `InputSystemUIInputModule`; the same event system visits
both panels in sort order.

World-projected labels keep their visual objects in `WorldLabelsPanel`'s
`#labels-layer`, while the world object owns its authored anchor. Mob prefabs
store a local-space resource-bar offset on `MobRoot`; `MobResourceBarUi` reads
the resulting world position. This allows differently sized mob prefabs to
place their bars independently without making game logic reference UI types or
putting one global offset on a scene object. Current mob bar binds health.
Generic resource-bar asset and controller naming, plus resource-specific USS
modifier classes, allow future mana or other resource bindings to reuse the
same projection, pooling, and layer path.

## Lifecycle And Performance

Follow the project coding rules:

- `Awake` sets up self-owned references, arrays, and validation.
- `OnEnable` accesses `UIDocument.rootVisualElement`, registers with other
  components, and subscribes to events.
- `OnDisable` unsubscribes, unregisters, clears held input, and removes runtime
  elements owned by the component.
- `Update` performs only small, cached presentation updates.
- Rebuild dynamic UI only after an edit or relevant state change, not every
  frame.
- Cache queried elements and callbacks. Do not search the scene or allocate
  lists every frame.

UI work is low-count managed presentation work. It must not add work to the
high-count ECS projectile, AOE, collision, or rendering paths.

Each panel has an explicit scaling contract:

- `HudPanel` work is `O(skill slots + fixed HUD controls)` and never iterates
  or retains one element per mob, projectile, or AOE.
- `WorldLabelsPanel` work is `O(registered mobs + visible mob bars)` and never
  iterates projectile or AOE entities.
- Neither panel's work is `O(projectiles + AOEs)`. The debug overlay's
  projectile/AOE counts are a fixed singleton read plus a fixed number of
  label writes; the displayed numbers may change every frame, but the UI work
  that reads and writes them does not scale with entity population.

Profiler markers are owned per panel: `HudPanel.PrepareRepaint` covers HUD
work, `WorldLabelsPanel.PrepareRepaint` covers world-label work. A rising
`WaitForJobGroupID` sample nested under a panel's marker reflects ECS worker
contention, not a UI entity scan — do not attribute it to that panel's UI
work without also checking the panel's own self time and element/call
counts.

See [Coding Standards](./coding-standards.md) and [ECS Notes](./reference/simulation/ecs-notes.md).

## Scene And Inspector Setup

For a runtime UI feature:

1. Decide whether the feature belongs in `HudPanel` (`GameUI` object) or
   `WorldLabelsPanel` (`WorldLabelsUI` object) — see Runtime Shape above. Do
   not point a new `UIDocument` at an existing panel's `PanelSettings` unless
   the feature is meant to share that panel; a shared `PanelSettings` means a
   shared runtime panel.
2. Add or reuse that panel's scene object with a `UIDocument`.
3. Assign the panel's `PanelSettings` and the root UXML source asset.
4. Add the feature controller and assign required references in the Inspector.
5. Assign UXML templates and catalogs where the controller needs them.
6. Add `GameplayInputSurface` only in `HudPanel`; it is the sole gameplay
   pointer-input owner.
7. Verify the scene has the UI event-system setup when pointer interaction is
   required.
8. Playtest both UI clicks and world input, including pointer release and
   modal open/close behavior, with both panels present.

Editor wiring is a user/editor operation. Do not hand-edit scene YAML or meta
files to simulate Inspector assignments.

## Adding A New UI Feature

Use this order:

1. Define the owning state and the read/command contract first.
2. Decide whether the feature belongs in `HudPanel`, `WorldLabelsPanel`, or
   needs a new panel entirely (new sort order, distinct `PanelSettings`).
3. Author stable structure in UXML and appearance in USS.
4. Add a small controller in `PlayGround.Ui` for binding and events.
5. Keep input ownership explicit. Put new interactive controls above the world
   surface and define pass-through behavior deliberately.
6. Refresh from owner events or revisions. Do not create a second gameplay
   state model in the UI.
7. Verify setup errors, input routing, modal behavior, resize behavior, and
   play-mode behavior in the target scene.

A future feature in either panel should follow the same projection rule: read
 a public presentation view from its owner, bind it to UI elements, and leave
 resource or combat authority outside the UI.

## Related Documents

- [Project Overview](./project-overview.md)
- [Folder Structure](./folder-structure.md)
- [Layer Rules](./architecture/layer-rules.md)
- [Scene And Authoring](./layers/scene-and-authoring.md)
- [Player Skill UI](./reference/design/player-skill-ui.md)
- [Skill Loadout Editing](./contracts/skill-loadout-editing.md)
- [Skill Loadout Edit Flow](./flows/skill-loadout-edit.md)

## Known UI/Implementation Gaps (Temporary)

Found during a 2026-07-25 doc-vs-implementation review. This section is a
temporary tracking list, not part of the architectural contract. Remove each
entry once the implementation is fixed or the referenced doc is corrected to
match reality.

- **No Escape key, backdrop cancel, or scrolling in the picker.** Only the
  `#cancel` button closes the modal; the choices list has no `ScrollView` or
  overflow handling.

  this is fine for now. bare bone ui is acceptable in current state.

- **`SkillLoadoutEditCommand` carries object references, not `definitionId`.**
  The documented struct shape in [Skill Loadout Editing](./contracts/skill-loadout-editing.md)
  (a `string definitionId` resolved by a "future" `SkillUiCatalog`) is stale;
  the catalog already exists and the command carries `Skill`/`SkillSupport`/
  `TriggerLink` references directly.

  
