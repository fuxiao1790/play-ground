# Implementation Context

## Architectural Decisions
- Consolidate UI under `Assets/Scripts/Ui/`, grouped by component.
- Shared HUD root lives in `Ui/Hud/`; skill-bar-private templates live in `Ui/Hud/SkillLoadout/`.
- Shared skill catalog authoring assets live in `Skills/Authoring/` so GameLogic persistence and UI can both consume them.
- Move `PlayerSaveController.cs` to `Persistence` and use `PlayGround.Persistence` namespace.
- Rename assembly `PlayGround.SkillUi` to `PlayGround.Ui`.

## Global Invariants
- Move each file with its `.meta`; preserve recorded GUID.
- Runtime behavior, scene wiring, namespaces except PlayerSaveController, and asmdef references stay unchanged.
- Update UXML relative `Style src` paths after folder moves.
- Remove dead `ResourceBarsUi.uxml` and stale `ResourceBarsUi` naming.

## Ownership Boundaries
- `PlayGround.Ui` depends on `PlayGround.GameLogic` and `PlayGround.Sim`; no reverse dependency.
- `PlayerSaveController` belongs to `PlayGround.Persistence`, not UI.

## Reused Mechanisms
- Existing `Assets/Scripts/Persistence/` folder and `PlayGround.GameLogic.asmdef`.
- Existing `Ui/SkillLoadout/` folder meta moves with its remaining contents.

## Introduced Mechanisms
- `Assets/Scripts/Ui/Hud/` shared-root grouping.
- `Assets/Scripts/Ui/Hud/SkillLoadout/` nested skill-bar grouping.

## Validation Requirements
- Validate each task with static file, GUID, JSON, path, and reference checks.
- Do not run Unity tests/builds; project instructions defer test execution to user.
- Final report must include exact user-run Unity test command with XML result path.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/SkillUi/`
- `Assets/Scripts/Ui/ResourceBars/`
- `Assets/Scripts/Ui/SkillLoadout/`
- `Assets/Scripts/Ui/Hud/`
- `Assets/Scripts/Persistence/`
- `Docs/ui.md`, `Docs/folder-structure.md`, `Docs/architecture/layer-rules.md`
