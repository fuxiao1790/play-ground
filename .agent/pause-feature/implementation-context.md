# Implementation Context

## Architectural Decisions
- `PauseController` is sole pause authority: `Time.timeScale`, `AudioListener.pause`, `IsPaused`, `PausedChanged`.
- Drivers become zero-dt honest; no pause flags flow into pooled mobs or ECS.
- Gameplay input uses a reason mask; pause and skill picker compose.
- UI observes Game Logic state and uses UI Toolkit picking for world click-through.

## Global Invariants
- Dependency direction: `PlayGround.Ui -> PlayGround.GameLogic -> PlayGround.Sim`; Game Logic does not reference UI.
- Never disable actor roots to pause: their `OnDisable` tears down combat proxies.
- `SkillDriver.ProcessPendingEdit()` must run before any zero-dt return.
- Do not hand-edit scene, prefab, asset, or meta YAML.
- `Awake` owns validation/setup; cross-component subscriptions belong in `OnEnable`/`OnDisable`.

## Ownership Boundaries
- `PauseController` owns global pause state only; observers own their own reactions.
- `PlayerRoot` owns gameplay-input block reasons.
- `GameplayInputSurface` owns pointer capture and world-surface picking.
- `PauseMenuUi` is a projection; it owns its cloned UI only.

## Data Flow
- Esc `UI/Cancel` -> `PauseController` -> `PausedChanged` -> player/input surface/menu.
- `Time.timeScale` -> Unity/ECS delta time -> guarded drivers.

## ECS / Job / Threading Constraints
- ECS time comes from `UnityEngine.Time.deltaTime`; do not add pause ECS state.
- No ECS systems change for this feature.

## Reused Mechanisms
- `Time.timeScale`, `AudioListener.pause`, `UI/Cancel`, UI Toolkit template cloning/picking, existing player input suspension.

## Introduced Mechanisms
- `PauseController`, `GameplayInputBlock`, `PauseMenuUi` template/controller.

## Validation Requirements
- Agents must not run Unity tests. User must run supplied commands with XML results; report passing only after XML review.
- Static/code verification is allowed. Scene editor wiring and manual playtest are user actions.

## Files / Systems Mentioned By The Plan
- Game: `PauseController` (new).
- Drivers: `SkillDriver`, `ContinuousStreamBehaviour`, `PlayerVfxAura`.
- Input/UI: `PlayerRoot`, `GameplayInputSurface`, `SkillLoadoutUi`, `PauseMenuUi` (new).
- Tests: `ContinuousStreamBehaviourEditModeTests`, `PauseFeaturePlayModeTests` (new).
