# HUD And World-Label Panel Isolation Plan

## Summary

Refactor current shared UI Toolkit panel into two runtime panels:

```text
HudPanel (sort order 0)
  click-to-fire, HUD, skill bar, player resources, debug overlay,
  picker, pause background, pause menu

WorldLabelsPanel (sort order -10)
  mob resource bars only
```

Move existing `#labels-layer`; do not copy it. `MobResourceBarUi` keeps its
existing managed mob-registry data flow, pooling, and projection behavior, but
binds a dedicated `UIDocument` backed by a distinct `PanelSettings` asset.
Rename current `SkillPanel` asset to `HudPanel` so profiler attribution describes
panel ownership correctly.

Resulting performance contract:

- HUD work is `O(skill slots + fixed HUD controls)` and never iterates or retains
  one element per mob, projectile, or AOE.
- World-label work is `O(registered mobs + visible mob bars)` and never iterates
  projectile or AOE entities.
- Debug projectile/AOE counts remain a fixed singleton read plus a fixed number
  of label writes; count values may change, but UI work does not scale with
  entity population.

This plan supersedes the one-panel architectural decision in
`.agent/mob-health-ui-layers/index.md`. That earlier plan remains implementation
history; this plan owns the corrective refactor.

## Architectural Decisions

### Distinct PanelSettings assets define isolation

Two `UIDocument` components pointing to one `PanelSettings` still share one
runtime panel. Therefore world labels require both a distinct `UIDocument` and a
distinct `PanelSettings`. `WorldLabelsPanel` uses the same scale mode, reference
resolution, theme, target display, and atlas settings as `HudPanel`, but has a
lower panel sort order.

Unity 6 documents that `PanelSettings.sortingOrder` orders distinct panels and
that multiple documents can share one panel settings object. Sources:

- Unity 6 `PanelSettings` API:
  https://docs.unity3d.com/6000.0/ScriptReference/UIElements.PanelSettings.html
- Unity runtime UI event system: pointer events visit panels in sort order until
  a panel reacts:
  https://docs.unity3d.com/2022.3/Manual/UIE-Runtime-Event-System.html

### Move, do not duplicate, labels ownership

Remove `#labels-layer` from `SkillLoadoutUi.uxml`. Add it exactly once in a new
world-label root UXML. Do not keep a hidden HUD fallback, conditional document
selection, adapter, or migration-time duplicate. `MobResourceBarUi` continues
to own the runtime label elements and points directly at the world-label
document.

### Preserve one mob-health data path

Authoritative flow remains:

```text
MobRoot health/anchor
  -> CombatRoot.TargetRegistry.Targets
  -> MobResourceBarUi
  -> pooled world-label visual
```

No new mob registry, event stream, health copy, ECS query, projectile query, or
AOE query is introduced. Panel isolation changes presentation ownership only.

### Keep input authority in HUD panel

`GameplayInputSurface` stays in `HudPanel`. Every world-label element remains
`PickingMode.Ignore`, allowing pointer events to continue to the higher-order
HUD panel's click-to-fire surface when no HUD control consumes them. HUD sort
order remains above world labels, so picker, HUD, and pause background draw over
mob bars. Pause background therefore still dims and shields the whole lower
presentation, including separate world labels.

### Treat profiler waits separately from data scaling

Panel self work and element/job call counts prove algorithmic isolation.
Inclusive `WaitForJobGroupID` may grow when combat jobs saturate workers; that is
scheduler contention, not a UI entity scan. Verification reports both, and does
not attribute worker-job samples nested below a UI wait as a UI dependency.

## Constraints And Invariants

### UI owns presentation only

- UI reads gameplay state and sends intent; it does not own health, loadouts,
  cooldowns, or ECS entities.
- Dependency direction stays `PlayGround.Ui -> PlayGround.GameLogic ->
  PlayGround.Sim`.
- Sources: `Docs/ui.md` **Ownership** and **State And Commands**;
  `Docs/architecture/layer-rules.md` **Package Boundary**.
- Validation: panel split adds no gameplay state or reverse dependency.

### Projectile/AOE population cannot enter UI iteration

- High-count projectile/AOE state belongs to ECS; UI must not add work to those
  paths.
- Sources: `Docs/ui.md` **Lifecycle And Performance**;
  `Docs/performance.md` **Runtime Strategy** and **Design Implications**;
  `Docs/coding-standards.md` **Hybrid ECS/Scene Rule**.
- Validation: `MobResourceBarUi` retains only managed `MobRoot` keys;
  `PerformanceUi` reads one `CombatStatsDisplaySingleton`; no UI controller
  creates a projectile/AOE query or per-entity visual.

### Mob-label scaling is bounded by managed actors

- Scene actors are low-count; projectile/AOE entities are high-count.
- `CombatTargetRegistry<T>.Targets` is managed membership exposed read-only.
- Sources: `Docs/coding-standards.md` **Hybrid ECS/Scene Rule**;
  `Assets/Scripts/System/Targets/CombatTargetRegistry.cs`;
  `Assets/Scripts/Ui/WorldLabels/MobResourceBarUi.cs`.
- Validation: existing `O(mobs)` reconciliation and pooling remain isolated in
  `WorldLabelsPanel`.

### Stable UI structure and explicit ownership

- Stable hierarchy belongs in UXML; styling belongs in USS; controllers bind
  named roots during `OnEnable`.
- Sources: `Docs/ui.md` **Runtime Shape**, **UXML, USS, And C# Rules**, and
  **Lifecycle And Performance**.
- Validation: each panel gets one authored root; no runtime root insertion or
  label-root fallback is added.

### Input and visual ordering remain deterministic

- Click-to-fire is the sole gameplay pointer source. Decorative labels never
  intercept input. Picker/HUD/pause render above labels; pause blocks gameplay.
- Sources: `Docs/ui.md` **Input Layering**;
  `Assets/Scripts/Ui/Hud/GameplayInputSurface.cs`;
  `Assets/Scripts/Ui/Hud/PauseMenu/PauseMenuUi.cs`;
  official Unity runtime panel-order/event docs cited above.
- Validation: `WorldLabelsPanel.sortingOrder = -10`, `HudPanel.sortingOrder = 0`,
  and every label descendant is picking-ignore.

### Lifecycle and allocation rules remain intact

- Required references are Inspector-injected and validated once.
- `Awake` validates owned setup; `OnEnable` binds document roots; `OnDisable`
  detaches owned runtime visuals.
- Reconciliation uses reusable dictionary/list/stack storage and no per-frame
  LINQ or temporary collections.
- Sources: `Docs/coding-standards.md` **Awake vs OnEnable Boundary**, **Fail Fast
  Validation**, and **Allocation Rule**.
- Validation: panel move changes only serialized document ownership and root
  lookup; existing pooled entries remain single-owned.

### Scene assets are authored through Unity

- Do not hand-edit scene YAML or `.meta` files.
- Required references use Inspector wiring and fail fast.
- Sources: `Docs/ui.md` **Scene And Inspector Setup**;
  `Docs/coding-standards.md` **Root Component Rule** and **Fail Fast Validation**.
- Validation: task 002 is explicitly a Unity Editor/Inspector operation.

### Test evidence comes from exported XML

- Agent never runs Unity Test Runner.
- User exports results under `Logs/`; only reviewed XML supports pass claims.
- Sources: `Docs/project-overview.md` **Agent instructions** and
  `Docs/testing.md` **Running Tests / Result Files**.

## Mechanisms Reused Vs. Introduced

### Reused

- Existing `SkillLoadoutUi.uxml` HUD tree and `GameplayInputSurface` authority.
- Existing `MobResourceBarUi`, mob registry traversal, camera projection, and
  pooled visual entries.
- Existing `MobResourceBar.uxml`/USS repeated-view assets.
- Existing PanelSettings configuration as template for scale/theme/atlas parity.
- Existing panel sort-order and picking mechanisms supplied by UI Toolkit.

### Introduced

- `WorldLabelsUi.uxml` and its small panel-root stylesheet.
- One distinct `WorldLabelsPanel` PanelSettings asset.
- One scene `WorldLabelsUI` GameObject with one `UIDocument`.
- Panel-isolation contract tests and controlled profiling comparison.

New assets remove ownership coupling. They do not add a second health model or
runtime data path.

## Design Validation

| Invariant | Pressure-test result |
|---|---|
| HUD independent of mob health | HUD tree contains no `#labels-layer` and no mob visual entries; mob damage dirties only `WorldLabelsPanel`. |
| UI independent of projectile/AOE population | No UI query or collection includes combat entities; debug overlay reads one display singleton and writes fixed labels. |
| Bars stay behind HUD/pause | Distinct panel sort orders place world labels below HUD; pause background remains in HUD and covers lower panel. |
| Click-to-fire survives panel split | Labels and descendants ignore picking; runtime event system continues to HUD panel, where existing click surface remains authoritative. |
| Health source remains singular | Existing `MobRoot` values feed existing controller; no snapshot or event duplication added. |
| Pooling remains singular | Existing dictionary/stack/list and entry lifetime move intact; no second controller instance or compatibility fallback. |
| Scaling is measurable | Renamed panels produce separate `HudPanel.PrepareRepaint` and `WorldLabelsPanel.PrepareRepaint` markers; controlled captures hold mobs/camera fixed while combat count changes. |
| Worker contention is not misclassified | Verification separates self work/call cardinality from inclusive job waits. |

## Minimal/Additive Vs. Refactor Comparison

### Minimal/additive approach

- **Resulting data flow:** add a second world-label document but leave
  `#labels-layer` in HUD; let `MobResourceBarUi` select whichever root exists.
- **New concepts/types introduced:** second panel plus fallback/root-selection
  logic and two valid labels destinations.
- **Copies/translations added:** duplicate layer contract, compatibility branch,
  ambiguous scene wiring.
- **Long-term cost:** future labels can silently return to HUD; tests must cover
  both paths; profiler isolation is not structurally guaranteed.

### Refactor approach

- **Resulting data flow:** existing mob state -> existing controller -> exactly
  one labels layer in exactly one dedicated panel.
- **Existing concepts/types changed or removed:** remove HUD `#labels-layer`;
  rename broad `SkillPanel` ownership to `HudPanel`; rebind existing controller.
- **Copies/translations removed or avoided:** no duplicate health model, registry,
  label root, adapter, or migration fallback.
- **Long-term benefit:** panel ownership is visible in assets, scene hierarchy,
  tests, and profiler markers; HUD scaling contract is structural.

### Decision

- **Choose refactor.** Remove old label destination in same change that adds new
  destination.
- **Reason:** additive fallback creates two representations of one presentation
  ownership contract and allows regression to shared-panel coupling.
- **Default decision rule:** if two representations or data paths describe the
  same domain concept, refactor toward one source of truth unless concrete
  compatibility or migration requirements demand both. None do here.

## Task List

1. [001-split-world-label-root.md](001-split-world-label-root.md) - move label
   root out of HUD assets and rebind existing controller contract.
2. [002-author-and-wire-distinct-panels.md](002-author-and-wire-distinct-panels.md)
   - author separate PanelSettings/UIDocument through Unity and wire scene.
3. [003-document-and-test-isolation.md](003-document-and-test-isolation.md) -
   update authoritative UI docs and add regression tests.
4. [004-profile-scaling-isolation.md](004-profile-scaling-isolation.md) - run
   controlled low/high combat captures and verify panel scaling boundaries.

## Open Questions, Dependencies, And Considerations

- No load-bearing open question remains. Unity documentation confirms distinct
  panel sorting and pointer-event propagation by panel order.
- Task 002 requires user/Unity Editor asset and scene serialization; agent must
  not simulate it through YAML/meta edits.
- Distinct panels have separate focus contexts. `WorldLabelsPanel` has no
  focusable controls, so no cross-panel navigation is needed.
- Separate panels improve ownership and dirty-tree isolation, but do not promise
  flat inclusive wall time under worker saturation. Task 004 reports waits
  separately from algorithmic UI work.
- Old `.agent/mob-health-ui-layers/005-tests.md` remains pending historical work.
  Task 003 replaces its shared-panel expectations with the new two-panel
  contract instead of implementing obsolete assertions.

