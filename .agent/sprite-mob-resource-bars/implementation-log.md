# Implementation Log

## Status
Paused at task 003 (user-owned Unity Editor authoring). Waiting for user to
author prefabs/scene and confirm before task 004 proceeds.

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-implement-sprite-presenter.md | Complete | |
| 002-bind-health-settings-lifecycle.md | Complete | One deviation, see below |
| 003-user-inspector-authoring.md | Waiting on user | Instructions delivered to user in chat; agent will verify read-only after confirmation |
| 004-remove-ui-toolkit-source.md | Blocked | Waiting on 003 |
| 005-replace-resource-bar-tests.md | Pending | |
| 006-update-docs-and-profile.md | Pending | |

## Completed Tasks

### 001-implement-sprite-presenter.md
- Added `Assets/Scripts/Mob/MobResourceBarSprite.cs` (namespace `PlayGround.Mob`, assembly `PlayGround.GameLogic`).
- Serialized `background`/`fill` `SpriteRenderer`, optional `fillTransform` (derived from `fill.transform` in `Awake` if unassigned), `visibleFillSteps` (`Min(36)`, default 36).
- `Awake` validates background/fill are assigned (throws `MissingReferenceException` with actionable message) and caches `fillFullScale`.
- `SetHealth(current, max)` clamps ratio to `[0,1]`, quantizes to an integer step (`currentStep` starts at `-1` so the first call always applies), writes fill local X scale only when the step changes, preserves Y/Z scale.
- `SetAliveVisible(bool)` and settings-driven `ApplySettingsVisible(bool)` combine via `aliveVisible && settingsVisible` before touching `background.enabled`/`fill.enabled`. Both default `true` so an authored bar with no settings binding yet is visible by default.
- `BindGameSettings(GameSettings)` supports being called either before or after the component's own `OnEnable` (matters because pooled/scene mobs finish `OnEnable` before `SpawnController`/`GameRoot` binder code runs): stores the reference and (re)subscribes to `DisplayMobHealthBarsChanged` immediately if already enabled; `OnEnable`/`OnDisable` subscribe/unsubscribe using a `settingsSubscribed` guard so re-enabling a pooled bar without a rebind still re-subscribes to the previously bound instance.
- No `Update`/`LateUpdate`/camera/scene-search/material-instance code anywhere in the type.

### 002-bind-health-settings-lifecycle.md
- `MobRoot.cs`: added `[SerializeField] private MobResourceBarSprite resourceBar;` (optional — absent is supported, no null-check exceptions, only `?.` calls). Subscribed `health.Changed += HandleHealthChanged` once, at the same point `health` is first constructed in `InitializeForSpawn`. `HandleHealthChanged` calls `resourceBar?.SetHealth(health.Current, health.Max)`. `InitializeForSpawn` now calls `resourceBar?.SetAliveVisible(true)` before the health create/reset branch, then explicitly `resourceBar?.SetHealth(...)` right after (needed because the `Resource` constructor path for a brand-new mob does not fire `Changed`, only `Reset` does — so relying solely on the event would skip the very first spawn). `SoftDie()` calls `resourceBar?.SetAliveVisible(false)` alongside the existing collider/sprite disable calls. Added `public void BindGameSettings(GameSettings settings) => resourceBar?.BindGameSettings(settings);`.
- `GameRoot.cs`: added `[SerializeField] private GameSettings gameSettings;`. `Start()` calls `mobs[i].BindGameSettings(gameSettings)` for each scene mob. `spawnController?.Bind(...)` now passes `gameSettings` as a third argument.
- `SpawnController.cs`: added `[SerializeField] private GameSettings gameSettings;` (same "authored value wins, Bind only fills if null" pattern already used for `combatRoot`/`target`). `Bind(CombatRoot, Transform, GameSettings)` gained the third parameter. `WireMob` calls `mob.BindGameSettings(gameSettings)`.
- `Assets/Tests/PlayMode/MobSpawnControllerPlayModeTests.cs:214`: updated the one existing `controller.Bind(combatRoot, null)` call site to the new 3-arg signature (`..., null)`), since it's a direct caller of the changed API and no bar/settings behavior is under test there.

#### Deviation from the task text
Task 002/index.md say to remove `MobRoot.resourceBarOffset` and `ResourceBarAnchorPosition`. I did **not** remove them yet. Reason: `Assets/Scripts/Ui/WorldLabels/MobResourceBarUi.cs:112` (the old UI Toolkit implementation) still reads `mob.ResourceBarAnchorPosition`, and that file is only deleted in task 004, which is explicitly gated behind the user completing task 003 in the Unity Editor. Removing the property now would break compilation for the entire span during which the user is expected to have Unity open doing prefab authoring — clearly worse than deferring. I left `resourceBarOffset`/`ResourceBarAnchorPosition` in place with a one-line comment noting they're dead weight kept only for that one remaining caller, and will delete both together with `MobResourceBarUi.cs` as part of task 004. Net end state after 004 is identical to what task 002 originally specified; only the exact task boundary moved. No other part of 002 was changed.

## Blockers
- Task 003 requires the user to perform Unity Editor authoring (sprite import, prefab hierarchy, component wiring, BenchmarkLarge scene edits). Agent cannot proceed to task 004 until the user confirms this is done. Exact instructions were posted to the user in chat (see task 003 file for the canonical version); waiting on confirmation.

## Plan Amendments
- User requested two shared sprites (one for background, one for fill) instead
  of the originally planned single shared tinted sprite, to allow custom art
  per role. This is authoring-only: `MobResourceBarSprite.cs` never inspects
  `SpriteRenderer.sprite`, so **zero code changes** were required. Updated
  `index.md` (Inherit motion section, allocation/performance budget, visual
  behavior constraints, Introduced Mechanisms, Design Validation) and
  `003-user-inspector-authoring.md` (sprite creation steps, acceptance
  criteria) to describe two shared sprite/material pairs — one reused across
  all three prefabs' background renderers, one reused across all three
  prefabs' fill renderers — instead of one. Batching invariant still holds,
  now per-role instead of globally shared.
- User questioned the `[SerializeField] private Transform fillTransform;`
  override on `MobResourceBarSprite` (optional wrapper transform, distinct
  from `fill`'s own transform, that task 001 originally specified "or derive
  once from assigned fill renderer"). Confirmed it was genuinely unused: grep
  found it referenced only in `Slime.prefab`, left unassigned (`fileID: 0`,
  i.e. already falling back to `fill.transform`) since the user had started
  task 003 authoring on that prefab. No prefab in this project needs a
  wrapper transform between the fill renderer and its own scale. Removed the
  serialized field from `MobResourceBarSprite.cs`; `Awake` now always sets
  `fillTransform = fill.transform` (private, non-serialized). Updated
  `001-implement-sprite-presenter.md` to match. `Slime.prefab`'s now-orphaned
  `fillTransform: {fileID: 0}` YAML key is harmless — Unity drops unknown
  serialized keys silently — and is prefab-authored content the agent does
  not edit; it will disappear next time the user saves that prefab.

- User asked to remove the 36-step fill quantization layer outright ("optimize
  if it becomes a performance problem" — i.e. do not pre-optimize).
  `SetHealth` now writes `fillTransform.localScale.x` directly from the
  continuous `current/max` ratio on every call, unconditionally. Removed
  `visibleFillSteps` field and `currentStep` tracking from
  `MobResourceBarSprite.cs` entirely. This is a deliberate simplicity-over-
  pre-emptive-optimization call: `Resource.Changed` (in `Resource.cs`) already
  only fires `MobRoot.HandleHealthChanged` on an actual numeric change, so the
  remaining exposure is per-tick regen writing the Transform every frame
  health ticks up, which was the exact case quantization existed to avoid. If
  task 006's profiling pass shows this is a real cost, reintroduce a threshold
  then — not before. Updated `001-implement-sprite-presenter.md` (change
  description + acceptance criteria), `index.md` (Summary, "Use a child
  presenter" decision, "Inherit motion" decision, Introduced Mechanisms,
  allocation/performance budget constraint — all had quantization language),
  and `005-replace-resource-bar-tests.md` (renamed the planned
  `HealthChangesQuantizedFillAndUnchangedStepDoesNotChangeScale` test to
  `HealthChangeSetsFillScaleProportionalToRatio`, and the EditMode prefab-
  authoring coverage bullet from "left-anchored, 36-step" to "left-anchored,
  ratio-proportional"). `Slime.prefab` still has an orphaned
  `visibleFillSteps: 36` YAML key from earlier authoring; harmless, Unity
  drops it on next save, not edited by the agent.

## Bug Fixes (found via user playtesting during task 003)
- User hit a real `NullReferenceException` at `MobResourceBarSprite.SetHealth`
  line 40 (`fillTransform.localScale = scale`), thrown from
  `MobRoot.Awake -> InitializeForSpawn` during `MobPool.Rent`. Root cause:
  `fillTransform`/`fillFullScale` were only ever set in
  `MobResourceBarSprite.Awake()`, but Unity does not guarantee a parent
  GameObject's `Awake` runs after its children's `Awake` — `MobRoot` (parent)
  was winning the race against `MobResourceBarSprite` (on the child
  `ResourceBar`), so `SetHealth` ran before its own `Awake` had populated
  `fillTransform`. User asked for "validation at Awake instead of failing
  during gameplay" — pinning validation to `Awake` specifically would not
  have fixed this (Awake ordering was the actual bug), so instead applied the
  same idempotent-initialization pattern already used by
  `GameSettings.EnsureInitialized()` elsewhere in this codebase: added
  `private bool initialized` + `EnsureInitialized()` to
  `MobResourceBarSprite`, called from `Awake()` and from the top of every
  public method (`SetHealth`, `SetAliveVisible`, `BindGameSettings`). This
  makes the component correct no matter which order Unity invokes things in,
  and still throws the existing clear `MissingReferenceException` (via
  `ValidateReferences()`, now called from `EnsureInitialized()`) if
  background/fill are genuinely unassigned. Updated
  `001-implement-sprite-presenter.md`'s runtime contract and acceptance
  criteria to describe this. `SubscribeSettings`/`OnEnable` were not at risk
  (they only touch `gameSettings`/plain bools, no Awake-computed state) so
  were left unguarded.

- User hit the health-bar-visibility toggle silently not working in
  `BenchmarkLarge`, and traced it to `GameRoot.gameSettings` still being
  unassigned (confirmed in the scene YAML: no `gameSettings:` key on either
  `GameRoot` or `SpawnController`). Root cause: null propagated through
  `GameRoot.Start -> SpawnController.Bind -> WireMob -> MobRoot.BindGameSettings
  -> MobResourceBarSprite.BindGameSettings(null)`, so `SubscribeSettings`
  always no-opped and `settingsVisible` stayed at its default `true` forever.
  User then asked why `GameRoot` needs manual wiring for a `GameSettings`
  component that lives on its own GameObject. Agreed — the original
  manual-only design's stated reason (preserve null in stripped test worlds)
  doesn't actually require manual-only; a same-GameObject
  `GetComponent<GameSettings>()` fallback in `GameRoot.OnEnable()` gives
  identical test behavior (a bare test GameObject with only `GameRoot` still
  resolves null) while eliminating this exact class of bug in production.
  Added `if (gameSettings == null) { gameSettings = GetComponent<GameSettings>(); }`
  to `GameRoot.cs`, consistent with the existing fallback pattern already used
  for `player`/`gameplayCamera`/`spawnController`/`playArea` in that same
  method. Updated `index.md` ("Preserve the existing display setting") and
  `002-bind-health-settings-lifecycle.md` to describe the fallback. User still
  needs to save `BenchmarkLarge.unity` once after this code change lands (or
  just assign it manually as originally instructed) for the fix to take
  effect on this scene, but manual assignment is no longer required going
  forward for any GameObject where `GameSettings` and `GameRoot` are
  co-located.
- User pushed further: the GameObject already *is* GameRoot, so why does it
  need a reference to itself at all, fallback or not? Correct — the fallback
  fix still left `gameSettings` as a `[SerializeField]` Inspector slot, which
  is unnecessary surface area for a value that can only ever legitimately be
  "the `GameSettings` on this same GameObject." `MobRoot.skillDriver` already
  establishes the right pattern in this codebase: a same-GameObject component
  dependency resolved via `GetComponent<T>()` in `Awake` as a plain private
  field, never `[SerializeField]`. Removed `[SerializeField]` from
  `GameRoot.gameSettings` entirely; it's now `private GameSettings
  gameSettings;` resolved unconditionally as `gameSettings =
  GetComponent<GameSettings>();` in `Awake()`, and the `OnEnable`
  null-check/fallback block from the previous fix was deleted (no longer
  needed — nothing can pre-populate it now). The Inspector "Game Settings"
  slot the user had just manually wired in the screenshot will disappear next
  time Unity reserializes `GameRoot` in the scene; harmless, same as the
  earlier `fillTransform`/`visibleFillSteps` orphaned-key situations. Updated
  `index.md` and `002-bind-health-settings-lifecycle.md` to describe the final
  three-iteration history (manual -> fallback -> no-Inspector-field) so future
  readers don't reintroduce the serialized version.

## Notes For Later Tasks
- Read-only scene/asset check (before posting task 003 instructions to the user): `Assets/Scenes/BenchmarkLarge.unity` currently has **no** `LabelUI` GameObject and **no** `UIDocument`/`MobResourceBarUi` component anywhere — the only `UIDocument` in the scene is on `HudUI`. Grepping the whole `Assets/` tree for `MobResourceBarUi.cs`'s script guid (`646dcce427fe4594dbdf1e3e02e6f20f`) and for `WorldLabelsPanel.asset`'s guid (`5e809c41dc5f9904fa1a173bc62833c9`) finds zero references outside each file's own `.meta`. So the old world-label path is already fully unwired in this project as it stands today — task 003's "remove now-empty LabelUI root GameObject" step does not apply (nothing to remove), and `WorldLabelsPanel.asset` is confirmed safe to delete with no reference check needed at delete time. `GameSettings` lives directly on the `GameRoot` GameObject (fileID 912359183 in the scene), consistent with task 003's assumption. Task 004/006 acceptance criteria that assume old wiring existed in-scene should be read as "confirm it's still absent," not "confirm removal happened."

## Validation Summary
- Manual code review of both changed/added files against every acceptance-criteria line in 001 and 002: all satisfied (see per-task notes above).
- Attempted `dotnet build PlayGround.GameLogic.csproj` for an independent compile check. It reached and successfully compiled several upstream Unity package assemblies, then failed with `CS8168`/`CS8347` inside `Library/PackageCache/com.unity.render-pipelines.core@.../PassesData.cs` — a pre-existing Unity package source file this task never touches, before the build ever reached `PlayGround.GameLogic`'s own sources. This is a known dotnet-CLI-vs-Unity-Roslyn version mismatch on unrelated package code, not something introduced here, and not something the agent should patch (it's inside `Library/PackageCache`, Unity-managed). Confirmed by file path: 100% within `Library/PackageCache`, nothing under `Assets/`.
- Net effect: no independent CLI compile confirmation was obtainable for this project as configured. The user will get a real compile check for free when opening the Unity Editor to do task 003's Inspector authoring; flagging this now so it isn't mistaken for a passed check.
