# Implementation Context

## Architectural Decisions
- Add a player-only, always-visible HP/MP HUD to the existing `GameUI` `UIDocument`.
- Extend `PlayGround.SkillUi` with `ResourceBarUi`; do not create another assembly.
- Use a UXML/USS template with two `ProgressBar` controls. The controller instantiates it as a sibling of `#bar` in the shared panel.

## Global Invariants
- UI is a read-only projection of state owned by `PlayerRoot`; it sends no gameplay commands and creates no ECS state.
- Preserve the one-way dependency `PlayGround.SkillUi -> PlayGround.GameLogic -> PlayGround.Sim`.
- UXML owns hierarchy, USS owns visual values, and C# owns runtime data binding only.
- The health orb sits bottom-left and mana orb bottom-right; both and their descendants must ignore picking so gameplay clicks reach `GameplayInputSurface`.
- Do not place either orb at top-left; `DebugOverlay` owns that location.

## Ownership Boundaries
- Scene and authoring own `UIDocument`, `PanelSettings`, and Inspector wiring.
- `ResourceBarUi` owns the cloned runtime template and cached UI references.
- `PlayerRoot` remains the sole source of HP/MP state.

## Data Flow
- `PlayerRoot` public health/mana properties -> `ResourceBarUi` -> `ProgressBar` values/titles.

## Lifecycle / Allocation Rules
- `Awake`: resolve and validate owned references.
- `OnEnable`: instantiate/add the template and query/cache named elements.
- `OnDisable`: remove the owned runtime element.
- `Update`: only cached, small presentation writes; no scene searches, visual-tree allocation, or style assignments.

## ECS / Job / Threading Constraints
- UI work is main-thread, low-count presentation only; it must not enter high-count ECS combat paths.

## Determinism Requirements
- None beyond direct current-state presentation.

## Producer / Consumer Separation
- The HUD consumes player state only; it is not a game-logic or ECS producer.

## Reused Mechanisms
- Shared `GameUI` `UIDocument`/panel, `SkillLoadoutUi` template-instantiation pattern, and `PlayerRoot` HP/MP public read surface.

## Introduced Mechanisms
- `ResourceBarsUi.uxml`, `ResourceBarsUi.uss`, and `ResourceBarUi` in `PlayGround.SkillUi`.

## Validation Requirements
- Check required named UXML elements and USS rules statically.
- Build/compile relevant Unity code when available.
- Wire the component in `BenchmarkLarge` through the Unity Inspector only; verify resource updates, click-through, layout, enable/disable behavior, and missing-reference errors in play mode.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Ui/ResourceBars/ResourceBarsUi.uxml`
- `Assets/Scripts/Ui/ResourceBars/ResourceBarsUi.uss`
- `Assets/Scripts/SkillUi/ResourceBarUi.cs`
- `Assets/Scripts/Player/PlayerRoot.cs`
- `Assets/Scenes/BenchmarkLarge.unity`
- `Assets/Scripts/SkillUi/SkillLoadoutUi.cs`
