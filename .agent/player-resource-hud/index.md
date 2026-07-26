---
name: player-resource-hud
description: Implementation plan for a player HP/MP resource display on the HUD
---

# Player Resource HUD

## Summary

Add a small, always-visible HUD widget that shows the player's current HP and
MP as two labeled bars. This is a pure UI projection: it reads state
`PlayerRoot` already owns and sends no commands back into Game Logic. Scope is
player-only for this plan — floating health bars over mobs are explicitly out
of scope (confirmed with the user) and would be a separate, much larger
world-space-positioning feature.

Source todo item: [Docs/todo.md:13](../../Docs/todo.md#L13) — "resource display
on ui. hp, mp, etc..."

## Rationale

`PlayerRoot` already owns and exposes everything the HUD needs to read:
`CombatCurrentHealth`/`CombatMaxHealth`/`CombatHealthRegenPerSecond` and the
mana equivalents ([PlayerRoot.cs:82-90](../../Assets/Scripts/Player/PlayerRoot.cs#L82-L90)),
backed by `Resource` ([Resource.cs](../../Assets/Scripts/Common/Stats/Resource.cs))
which `PlayerRoot.Update()` mirrors from the ECS proxy every frame
([PlayerRoot.cs:350-357](../../Assets/Scripts/Player/PlayerRoot.cs#L350-L357)).
No Game Logic change is required — this is UI-only, following the one-way
`PlayGround.SkillUi -> PlayGround.GameLogic` dependency
([ui.md:19-21](../../Docs/ui.md#L19-L21)).

The existing `SkillLoadoutUi` is the reference implementation
([ui.md:41-46](../../Docs/ui.md#L41-L46)) and already establishes every pattern
this feature needs: a controller that queries named elements from a shared
`UIDocument.rootVisualElement`, adds its own top-level container into that root
(`root.Add(modal)` at [SkillLoadoutUi.cs:278](../../Assets/Scripts/SkillUi/SkillLoadoutUi.cs#L278)),
and does small cached `Update()` polling of continuously-changing values
(cooldown label at [SkillLoadoutUi.cs:92-110](../../Assets/Scripts/SkillUi/SkillLoadoutUi.cs#L92-L110)).
The resource bars follow the same shape: a second controller, sharing the same
`UIDocument`, doing the same kind of per-frame label/value refresh.

## Constraints & Invariants

- **One-way UI dependency.** `PlayGround.SkillUi -> PlayGround.GameLogic ->
  PlayGround.Sim`; UI must not own gameplay state or make ECS decisions.
  Source: [ui.md:19-24](../../Docs/ui.md#L19-L24).
- **UXML/USS/C# split.** UXML for stable named structure, USS for visual
  values, C# only for values that come from runtime state — controllers must
  not set `style.*` for visual values. Source: [ui.md:48-61](../../Docs/ui.md#L48-L61).
  This is why the plan uses UI Toolkit's built-in `ProgressBar` control (see
  Design Validation) instead of a hand-rolled fill bar driven by
  `style.width`.
- **Click-through / input layering.** The world surface sits behind top-layer
  UI; any top-layer element that isn't meant to be interactive must set
  `picking-mode: ignore` or it silently swallows `GameplayInputSurface` clicks
  under its bounds. Source: [ui.md:81-104](../../Docs/ui.md#L98-L103).
- **Debug overlay owns top-left.** "Shared top-left debug text belongs to
  `DebugOverlay`." Source: [coding-standards.md:372-374](../../Docs/coding-standards.md#L372-L374).
  `PerformanceText`/`DebugOverlay` renders there via a separate legacy
  `Canvas` ([PerformanceText.cs:108-119](../../Assets/Scripts/Debugging/PerformanceText.cs#L108-L119)).
  Different render stack (UGUI vs UI Toolkit panel), so there's no picking
  conflict, but placing the HUD there would visually overlap the debug text.
  The bars go bottom-left instead.
- **Lifecycle boundary.** `Awake` sets up self-owned refs/validation;
  `OnEnable` touches `rootVisualElement` and subscribes; `OnDisable` removes
  runtime elements the component owns; `Update` does only small cached
  presentation work — no per-frame allocation of the visual tree, no scene
  search. Source: [ui.md:113-129](../../Docs/ui.md#L113-L129),
  [coding-standards.md:45-58](../../Docs/coding-standards.md#L45-L58).
- **Fail-fast setup.** Required serialized references validate once in
  `Awake()` and throw; no repeated null checks in `Update()`. Source:
  [coding-standards.md:60-78](../../Docs/coding-standards.md#L60-L78). Mirrors
  `SkillLoadoutUi.ValidateSetup()` / `GameplayInputSurface.ValidateSetup()`.

## Mechanisms Reused vs. Introduced

Reused:
- `PlayerRoot`'s existing public HP/MP read surface — no new Game Logic state,
  fields, or events.
- The shared `GameUI` `UIDocument` and its `PanelSettings` (same panel
  `SkillLoadoutUi` and `GameplayInputSurface` already use).
- The template-instantiate-then-`root.Add()` pattern already used for the
  skill picker modal.
- The `PlayGround.SkillUi` assembly and its `Assets/Scripts/SkillUi/` (C#) +
  `Assets/Scripts/Ui/<Feature>/` (UXML/USS) file layout.

Introduced:
- One new controller, `ResourceBarUi`, and one new UXML/USS template pair.
  Justification: this is a genuinely new feature surface (no existing
  resource-display code to extend), and it's additive enough (~1 small
  component) that a new assembly would be ceremony without benefit — see
  comparison below.
- Namespace: the file goes in `namespace PlayGround.SkillUi` (matching the
  asmdef's actual `rootNamespace`), not the `PlayGround.Skills` namespace the
  two existing files in this assembly use
  ([SkillLoadoutUi.cs:7](../../Assets/Scripts/SkillUi/SkillLoadoutUi.cs#L7),
  [GameplayInputSurface.cs:6](../../Assets/Scripts/SkillUi/GameplayInputSurface.cs#L6)).
  That existing choice reads as a historical carryover from the GameLogic
  `Skills` namespace; reusing it for an unrelated HP/MP widget would be
  actively misleading. Not a blocking issue, just noted so it doesn't look
  like an oversight.

## Design Validation

- *One-way dependency*: `ResourceBarUi` only reads `PlayerRoot` properties and
  writes to its own `VisualElement`s. It calls nothing in `PlayGround.Sim` and
  sends no commands, so the dependency direction holds trivially (it's even
  simpler than `SkillLoadoutUi`, which also sends edit commands).
- *UXML/USS/C# split*: satisfied by using `ui:ProgressBar` — its fill width is
  driven by the control's own `value`/`lowValue`/`highValue` data properties,
  which C# sets; the actual fill visual (`.unity-progress-bar__progress`
  width) is computed internally by the control, not by our code writing
  `style.width`. Colors/position/size stay in USS.
- *Click-through*: the plan's USS applies `picking-mode: ignore` to the
  container and every descendant (including `ProgressBar`'s internal parts),
  so the bars can never become the pointer-event target. Acceptance criteria
  in subtask 003 explicitly re-verify this in play mode.
- *Debug overlay collision*: resolved by choosing bottom-left instead of
  top-left.
- *Lifecycle*: `OnEnable` instantiates the template once and adds it to
  `root`; `OnDisable` removes it — matches `GameplayInputSurface`'s
  create-on-enable/remove-on-disable shape for its own runtime-added surface
  element ([GameplayInputSurface.cs:28-41](../../Assets/Scripts/SkillUi/GameplayInputSurface.cs#L28-L41)).
  `Update()` only writes four numeric properties and one string per bar, no
  allocation of elements, matching the cooldown-label precedent.
- *Fail-fast*: `Awake()` resolves `playerRoot` (serialized or
  `FindAnyObjectByType`, same fallback `SkillLoadoutUi` uses) and the template
  asset, then throws if either is missing.

## Minimal/Additive vs. Refactor Comparison

There is no existing resource-display code path, so there's nothing to
refactor away — this is inherently additive. The only real design choice is
*where* the new code lives.

- **Additive approach (chosen): extend `PlayGround.SkillUi`.**
  - Resulting data flow: `PlayerRoot` (existing state) → new `ResourceBarUi`
    (read-only) → two `ProgressBar` elements on the existing shared panel.
  - New concepts/types introduced: one `MonoBehaviour`, one UXML template, one
    USS file. No new data type for HP/MP — reuses `Resource`/`PlayerRoot` as-is.
  - Copies/translations added: none. The controller reads `PlayerRoot`
    properties directly each frame; no intermediate DTO.
  - Long-term cost: the `PlayGround.SkillUi` assembly now hosts two unrelated
    feature controllers (skill loadout, resource HUD) sharing one file
    layout convention. Low cost — the assembly's own doc line already
    describes it loosely as UI-feature-controller-owning, not
    skill-loadout-exclusive, and `ui.md` explicitly anticipates a "future HUD
    feature" following this same projection rule.
  - Decision: **choose additive.**
    Reason: a second UI assembly for one small always-on widget would add
    asmdef ceremony and a second place to look for "the UI feature
    controllers" without removing any duplication — it fails the "does the
    refactor remove complexity" bar. Reuse-first wins here because the
    existing assembly, panel, and file conventions already fit this feature
    with zero modification.

- **Refactor approach (rejected): split UI into per-feature assemblies now
  (e.g. introduce `PlayGround.Hud` alongside `PlayGround.SkillUi`).**
  - Resulting data flow: same read path, just under a second assembly with
    its own reference to `PlayGround.GameLogic`.
  - Existing concepts/types changed or removed: none — this is a pure split,
    not a consolidation, so it doesn't reduce any existing duplication.
  - Copies/translations removed or avoided: none.
  - Long-term benefit: marginally clearer naming (an assembly literally named
    for HUD content). Speculative — there's only one HUD widget being added.
  - Decision: rejected for now per "don't design for hypothetical future
    requirements" — nothing today needs the isolation a second assembly would
    buy.

## Default Decision Rule Applied

No two representations of the same domain concept are introduced here (HP/MP
state still lives solely in `PlayerRoot`/`Resource`), so the "collapse to one
source of truth" rule doesn't trigger — there is already exactly one source of
truth, and this plan only adds a reader.

## Tasks

1. [001-resource-bar-template.md](001-resource-bar-template.md) — UXML +
   USS for the HP/MP bar visuals.
2. [002-resource-bar-controller.md](002-resource-bar-controller.md) —
   `ResourceBarUi` C# controller.
3. [003-scene-wiring-and-verification.md](003-scene-wiring-and-verification.md) —
   editor wiring in the sample scene and play-mode verification (user/editor
   steps).

Dependency order: 001 → 002 → 003 (each depends on the previous).

## Open Questions / Notes

- Exact colors/sizes in subtask 001 are a starting proposal (red HP, blue MP,
  bottom-left, stacked vertically at roughly the same visual weight as the
  skill bar). These are USS-only values and trivial to retint later; not
  blocking.
- Confirmed with the user: mob/enemy floating health bars are explicitly out
  of scope for this plan.
