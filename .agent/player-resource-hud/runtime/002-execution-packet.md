# Task Execution Packet

## Task

002-resource-bar-controller.md

## Goal

Add the `ResourceBarUi` `MonoBehaviour` that reads the existing `PlayerRoot` health/mana surface and binds it to the task-001 `ProgressBar` template.

## Files Allowed To Modify

- `Assets/Scripts/SkillUi/ResourceBarUi.cs` (new)

## Files Allowed To Create

- The controller file above.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- `Assets/Scripts/SkillUi/SkillLoadoutUi.cs`
- `Assets/Scripts/SkillUi/GameplayInputSurface.cs`
- `Assets/Scripts/Player/PlayerRoot.cs`
- `Assets/Scripts/Ui/ResourceBars/ResourceBarsUi.uxml`

## Behavior To Preserve

- `PlayerRoot` remains the source of truth; no UI commands, Game Logic changes, ECS access, or changes to existing UI controllers.

## Behavior To Change

- Add a read-only controller that clones the resource template into the shared document, writes its two cached bars in `Update`, and removes its clone when disabled.

## Relevant Global Context

- Namespace must be `PlayGround.SkillUi`.
- Use `[DefaultExecutionOrder(1000)]` and `[RequireComponent(typeof(UIDocument))]` like current UI controllers.
- `Awake` may resolve/validate owned refs; `OnEnable` touches `rootVisualElement`; `OnDisable` removes the owned runtime tree.
- No per-frame scene search, template instantiation, visual-tree allocation, or `style.*` assignment. The only expected per-frame allocation is the bar-title string.

## Dependencies Confirmed

- `Assets/Scripts/Ui/ResourceBars/ResourceBarsUi.uxml` exists and has `#resource-bars`, `#health-bar`, and `#mana-bar` `ProgressBar` elements.
- `PlayerRoot` exposes `CurrentHealth`, `CombatMaxHealth`, `CurrentMana`, and `CombatMaxMana` public float properties.
- `PlayGround.SkillUi` already references `PlayGround.GameLogic`, which contains `PlayerRoot`.

## Step-By-Step Instructions

1. Add the exact serialized, private state fields described by the task.
2. In `Awake`, cache `UIDocument`, resolve `playerRoot` with the existing fallback if necessary, and fail fast for missing document/player/template.
3. In `OnEnable`, instantiate/query the template, fail fast for missing named bars, and add its root to the document root.
4. In `Update`, update each bar with current/max state and a ceil-rounded `current/max` title using the specified helper.
5. In `OnDisable`, remove the owned instantiated root.

## Acceptance Criteria

- Compiles in the existing SkillUi assembly without asmdef edits.
- Clear setup exceptions cover missing document/player/template/named bars.
- Update only performs expected title string allocation.
- Disabling removes the cloned element.

## Validation Required

- Static inspect fields, namespace, lifecycle, template queries, data reads, and teardown.
- Run a relevant Unity compile/build check if available.

## Hard Boundaries

- Modify only the new controller file.
- Do not change PlayerRoot, the asmdef, scene, UXML/USS, or later task files.
- Do not introduce generic resource abstractions or subscriptions.
