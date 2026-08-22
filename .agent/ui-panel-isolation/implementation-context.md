# Implementation Context

## Architectural Decisions
- Split one shared UI Toolkit panel into two runtime panels: `HudPanel` (sort 0)
  and `WorldLabelsPanel` (sort -10, mob resource bars only).
- Distinct `UIDocument` + distinct `PanelSettings` per panel (sharing one
  PanelSettings recreates one runtime panel even with two documents).
- Move `#labels-layer` out of HUD UXML into a new `WorldLabelsUi.uxml`; do not
  copy/duplicate it. `MobResourceBarUi` binds exclusively to the new document.
- Rename `Assets/UI/SkillPanel.asset` -> `HudPanel.asset` (profiler attribution).

## Global Invariants
- UI owns presentation only; no gameplay state, no reverse dependency into
  `PlayGround.GameLogic`/`PlayGround.Sim`.
- No UI controller may create a projectile/AOE `EntityQuery` or per-entity
  visual. World-label work stays `O(mobs)`; HUD stays
  `O(skill slots + fixed controls)`; neither scales with projectiles/AOEs.
- Every world-label element (root + descendants) is `PickingMode.Ignore` so
  pointer events fall through to HUD's click-to-fire surface.
- HUD sorts above world labels; pause background (in HUD) dims/shields world
  labels too.
- Awake validates injected refs and fails fast; OnEnable binds document roots;
  OnDisable detaches owned runtime visuals. No per-frame LINQ/temp collections.
- No fallback query against the old HUD root, no conditional dual-document
  path, no compatibility shim.

## Ownership Boundaries
- `MobResourceBarUi` keeps its existing managed mob-registry data flow,
  pooling, projection, and fill-caching logic unchanged — only its document
  binding and error text move to the new asset.
- Input authority (`GameplayInputSurface`, click-to-fire) stays in `HudPanel`.

## Data Flow
`MobRoot health/anchor -> CombatRoot.TargetRegistry.Targets ->
MobResourceBarUi -> pooled world-label visual`. No new registry, event
stream, or query is introduced anywhere in this plan.

## Lifecycle / Allocation Rules
- Awake = validate owned setup; OnEnable = bind roots; OnDisable = release
  owned runtime visuals. Reuse existing dictionary/list/stack pooling.

## ECS / Job / Threading Constraints
- N/A for tasks 001-003 (pure UI asset/controller work). Task 004 profiling
  must separate UI self-time/call-count from `WaitForJobGroupID` scheduler
  contention — rising inclusive wait alone is not a UI regression.

## Determinism Requirements
- None beyond existing pooling/reconciliation order; no new nondeterminism
  introduced.

## Producer / Consumer Separation
- N/A (no manager/event payload changes in this plan).

## Reused Mechanisms
- Existing `SkillLoadoutUi.uxml` HUD tree, `GameplayInputSurface` authority,
  `MobResourceBarUi` pooling/reconciliation, `MobResourceBar.uxml`/uss,
  existing PanelSettings as template for scale/theme/atlas parity.

## Introduced Mechanisms
- `WorldLabelsUi.uxml` + small panel-root `WorldLabelsUi.uss`.
- One `WorldLabelsPanel` PanelSettings asset (task 002, Editor).
- One scene `WorldLabelsUI` GameObject with one `UIDocument` (task 002, Editor).
- Panel-isolation EditMode/PlayMode tests (task 003).

## Validation Requirements
- Task 001: static/search verification (exactly one authored `#labels-layer`,
  no HUD reference to it, controller fails fast on wrong document).
- Task 002: Unity Editor asset/scene operations — **agent cannot perform
  these**; must be done by the user through the Unity Editor. No YAML/meta
  hand-editing.
- Task 003: EditMode/PlayMode tests authored by agent; user must run Unity
  Test Runner and export XML under `Logs/`. Agent never runs Unity Test
  Runner and must review exported XML before claiming pass.
- Task 004: requires user-provided profiler CSV captures under
  `ProfilerCaptures/`; agent does not invent thresholds before a baseline
  exists.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Ui/Hud/SkillLoadoutUi.uxml`, `SkillLoadoutUi.uss`
- `Assets/Scripts/Ui/WorldLabels/WorldLabelsUi.uxml` (new),
  `WorldLabelsUi.uss` (new)
- `Assets/Scripts/Ui/WorldLabels/MobResourceBarUi.cs`,
  `MobResourceBar.uxml`
- `Assets/UI/SkillPanel.asset` -> `HudPanel.asset`,
  `Assets/UI/WorldLabelsPanel.asset` (new, task 002)
- Scene `BenchmarkLarge` / `GameUI` object (task 002)
- `Docs/ui.md`, `Docs/folder-structure.md` (task 003)
