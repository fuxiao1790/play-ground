# UI Architecture

This page is the main guide for UI in this project.

UI is presentation and input routing. UI shows state owned by Game Logic or
runtime services. UI sends user intent back through a small API. UI does not own
gameplay state, cooldowns, loadouts, combat authority, or ECS entities.

## Ownership

Scene And Authoring owns the Unity UI objects and presentation roots. This
includes `UIDocument`, UI Toolkit panels, `PanelSettings`, scene wiring, and
the UI input surface.

The `PlayGround.SkillUi` assembly owns feature-specific UI controllers. It may
read Game Logic state and send commands to Game Logic. The dependency direction
stays one way:

```text
PlayGround.SkillUi -> PlayGround.GameLogic -> PlayGround.Sim
```

Game Logic and ECS Simulation must not reference UI types. UI must not make ECS
simulation decisions or create ECS combat entities.

See [Layer Rules](./architecture/layer-rules.md) and [Scene And Authoring](./layers/scene-and-authoring.md)
for the wider boundary rules.

## Runtime Shape

The project uses Unity UI Toolkit at runtime.

- A scene `UIDocument` owns the panel instance.
- `PanelSettings` controls how the panel is rendered.
- A root UXML file describes stable visual structure and links its USS file.
- Small UXML templates describe repeated or dynamic pieces.
- USS owns visual styling.
- C# owns state binding, dynamic cloning, event wiring, and small runtime
  updates.

The current skill UI is the reference implementation:

- Root and templates: `Assets/Scripts/Ui/SkillLoadout/`
- Runtime controller: `Assets/Scripts/SkillUi/SkillLoadoutUi.cs`
- Compiled assembly: `Assets/Scripts/SkillUi/PlayGround.SkillUi.asmdef`
- Sample scene wiring: `Assets/Scenes/BenchmarkLarge.unity`

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

The `GameUI` panel has two functional layers:

```text
top:    buttons, skill bar, picker, other interactive UI
bottom: full-screen world input surface
```

`GameplayInputSurface` is the only mouse click-to-fire source. It receives
pointer down/up events, captures the pointer while held, and exposes
`FireHeld` through `IGameplayInputSource`.

`PlayerRoot` consumes that source and passes the result to `SkillDriver`. The UI
layer pushes the source into `PlayerRoot`; Game Logic does not look up or read
UI objects.

UI Toolkit picking decides whether a click reaches the world surface:

- Normal controls use position picking and consume their own clicks.
- The world surface sits behind the controls.
- The top UI layer decides whether each element consumes a click or allows it
  to pass through with `picking-mode: ignore`.
- Pointer capture keeps a held click stable until release or capture loss.

Opening the skill picker is a separate modal input mode. `SkillLoadoutUi` tells
`PlayerRoot` to suspend gameplay input while the picker is open, then clears the
suspension when it closes. This is not a replacement for UI layering.

Runtime UI pointer delivery requires the scene's UI event setup, normally an
`EventSystem` with `InputSystemUIInputModule`.

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

See [Coding Standards](./coding-standards.md) and [ECS Notes](./reference/simulation/ecs-notes.md).

## Scene And Inspector Setup

For a runtime UI feature:

1. Add or reuse a `GameUI` object with a `UIDocument`.
2. Assign `PanelSettings` and the root UXML source asset.
3. Add the feature controller and assign required references in the Inspector.
4. Assign UXML templates and catalogs where the controller needs them.
5. Add `GameplayInputSurface` only when the panel is the gameplay pointer-input
   owner.
6. Verify the scene has the UI event-system setup when pointer interaction is
   required.
7. Playtest both UI clicks and world input, including pointer release and modal
   open/close behavior.

Editor wiring is a user/editor operation. Do not hand-edit scene YAML or meta
files to simulate Inspector assignments.

## Adding A New UI Feature

Use this order:

1. Define the owning state and the read/command contract first.
2. Decide whether the feature belongs in the existing `UIDocument` or needs a
   separate panel.
3. Author stable structure in UXML and appearance in USS.
4. Add a small controller in `PlayGround.SkillUi` for binding and events.
5. Keep input ownership explicit. Put new interactive controls above the world
   surface and define pass-through behavior deliberately.
6. Refresh from owner events or revisions. Do not create a second gameplay
   state model in the UI.
7. Verify setup errors, input routing, modal behavior, resize behavior, and
   play-mode behavior in the target scene.

A future HUD feature should follow the same projection rule: read a public
 presentation view from its owner, bind it to UI elements, and leave resource
 or combat authority outside the UI.

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

  
