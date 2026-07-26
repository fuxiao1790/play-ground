---
name: resource-bar-controller
description: C# controller that binds PlayerRoot HP/MP state to the resource bar template
---

# 002 — ResourceBarUi Controller

## Depends On

001-resource-bar-template.md (needs the UXML template and named elements).

## Scope

A single new `MonoBehaviour`, `ResourceBarUi`, that projects `PlayerRoot`'s
existing HP/MP state onto the task-001 template. Read-only: no commands sent
back into Game Logic, no new Game Logic state.

## File

- `Assets/Scripts/SkillUi/ResourceBarUi.cs` (new), `namespace
  PlayGround.SkillUi` (see index.md's namespace note — deliberately not
  `PlayGround.Skills`).

## Shape (mirror existing controllers in this assembly)

Reference precedents: `SkillLoadoutUi.cs` and `GameplayInputSurface.cs` in the
same folder — same `[RequireComponent(typeof(UIDocument))]` +
`[DefaultExecutionOrder(1000)]` shape, same `Awake`/`OnEnable`/`OnDisable`
split from [coding-standards.md:45-58](../../Docs/coding-standards.md#L45-L58).

Fields:
- `[SerializeField] private PlayerRoot playerRoot;`
- `[SerializeField] private VisualTreeAsset resourceBarsTemplate;`

Private state:
- `UIDocument document;`
- `VisualElement root;`
- `VisualElement resourceBars;` (the instantiated `#resource-bars` root)
- `ProgressBar healthBar;`
- `ProgressBar manaBar;`

`Awake()`:
- `document = GetComponent<UIDocument>();`
- `if (playerRoot == null) playerRoot = FindAnyObjectByType<PlayerRoot>();`
  (same fallback `SkillLoadoutUi.Awake()` uses)
- `ValidateSetup()` — throw `InvalidOperationException` if `document`,
  `playerRoot`, or `resourceBarsTemplate` is null, matching
  `GameplayInputSurface.ValidateSetup()` / `SkillLoadoutUi.ValidateSetup()`.

`OnEnable()`:
- `root = document.rootVisualElement;`
- `resourceBars = resourceBarsTemplate.Instantiate().Q<VisualElement>("resource-bars");`
- `healthBar = resourceBars.Q<ProgressBar>("health-bar");`
- `manaBar = resourceBars.Q<ProgressBar>("mana-bar");`
- Throw if either `ProgressBar` query comes back null (fail-fast on a
  misassigned template, same spirit as `SkillLoadoutUi`'s `#bar` null check).
- `root.Add(resourceBars);` (same pattern as `root.Add(modal)` in
  `SkillLoadoutUi.OpenPicker`)

`OnDisable()`:
- `resourceBars?.RemoveFromHierarchy();`
- (No event subscriptions to unsubscribe — this controller polls in
  `Update()` rather than subscribing to `Resource.Changed`, see rationale
  below.)

`Update()`:
- `UpdateBar(healthBar, playerRoot.CurrentHealth, playerRoot.CombatMaxHealth);`
- `UpdateBar(manaBar, playerRoot.CurrentMana, playerRoot.CombatMaxMana);`
- Private helper `UpdateBar(ProgressBar bar, float current, float max)` sets
  `bar.lowValue = 0f; bar.highValue = max; bar.value = current; bar.title =
  $"{Mathf.CeilToInt(current)}/{Mathf.CeilToInt(max)}";`

  Why poll instead of subscribing to `Resource.Changed`: `PlayerRoot.Update()`
  mirrors `Current` from the ECS proxy every frame via `MirrorCurrent`
  ([PlayerRoot.cs:350-357](../../Assets/Scripts/Player/PlayerRoot.cs#L350-L357)),
  and regen means `Current` changes most frames anyway whenever
  `RegenPerSecond > 0`. `Changed` would fire nearly every frame in practice,
  so it buys nothing over polling — and the existing cooldown-label precedent
  in this same assembly already polls a continuously-changing value in
  `Update()` rather than eventing
  ([SkillLoadoutUi.cs:92-98](../../Assets/Scripts/SkillUi/SkillLoadoutUi.cs#L92-L98)).
  Keep this consistent with that precedent rather than introducing a second
  refresh idiom in the same assembly.

## Non-Goals

- No Game Logic changes. `PlayerRoot` is read-only from this controller's
  perspective.
- No generic "N-resource" data-driven system. Exactly two named bars (health,
  mana), matching the current `Resource` fields on `PlayerRoot`. If a third
  resource is ever added to `PlayerRoot`, extending this is a same-shape
  addition (one field, one `UpdateBar` call) — not something to build for
  speculatively now.
- No world-space mob health bars (confirmed out of scope for this plan).

## Acceptance Criteria

- Compiles inside `PlayGround.SkillUi` with no new assembly reference beyond
  what it already has (`PlayGround.GameLogic`, `Unity.InputSystem` — this
  controller doesn't need `Unity.InputSystem` itself, but adding it doesn't
  require touching the asmdef either way since it's already referenced).
- Throws a clear setup exception in `Awake()`/`OnEnable()` if `playerRoot`,
  `resourceBarsTemplate`, or either named `ProgressBar` is missing/misnamed —
  no silent no-op.
- `Update()` performs no allocation beyond the per-frame title string (matches
  existing cooldown-label behavior in this assembly — not a regression, not
  something to gold-plate here).
- `OnDisable()` removes the runtime-added `resourceBars` element from `root`,
  leaving no dangling child if the component is disabled/destroyed.

## Estimated Scope

Small — one small MonoBehaviour, no new Game Logic surface.
