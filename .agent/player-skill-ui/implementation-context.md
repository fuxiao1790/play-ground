# Implementation Context

## Architectural Decisions

- Refactor to one normalized `SkillLoadout` topology. Do not add a runtime
  snapshot/store or UI-owned equipment model.
- `SkillDriver` owns a deep runtime clone of the authored loadout and accepts
  commands. It validates, compiles, registers, then atomically swaps.
- Use UI Toolkit. The v1 `3 skills / 2 links / 3 supports` cap belongs only to
  UI policy; all game-logic collections remain unbounded.

## Global Invariants

- Definitions and authored assets stay immutable. Runtime mutation touches only
  per-session clones; in-flight ECS data remains a copied snapshot.
- A node's incoming trigger comes from its left neighbor. Nodes with incoming
  triggers are triggered-only; nodes without one are direct-cast roots.
- Invalid choices never commit and no conflicting state is silently cleared.
- Picker does not pause time. It blocks gameplay input while simulation and VFX
  continue. Pointer over the bar suppresses attack input.

## Ownership Boundaries

- Game Logic owns loadouts, validation, compilation, cooldowns, and edit commit.
- Scene/Authoring owns `UIDocument`, scene references, UI input routing, and
  authored templates.
- UI only projects state and issues commands. ECS never reads the live loadout.

## Data Flow

`SkillLoadout` asset -> `SkillDriver` runtime clone -> edit candidate ->
validate/compile/register -> atomic swap -> loadout/edit events -> UI refresh.

## Lifecycle / Allocation Rules

- Clone/validate/compile only on accepted edit, never per frame.
- UI polls no more than three root cooldowns per frame. Picker allocations occur
  only when opening or catalog changes.

## ECS / Job / Threading Constraints

- All UI callbacks and loadout mutation run on Unity main thread.
- Combat entities retain copied plain values, IDs, hashes, and template keys.
- Do not alter high-count ECS paths for this feature.

## Reused Mechanisms

- `SkillLoadout`, `SkillSet`, `SkillLoadoutValidator`, `SkillSetCompiler`,
  `SkillDriver`, Input System Player/UI maps, and ScriptableObject authoring.

## Introduced Mechanisms

- `SkillLoadoutNode`, edit command/result, `SkillUiCatalog`, UI Toolkit
  presenter/controller, and UI-only visible-count policy.

## Validation Requirements

- Validate migration, topology/compatibility, atomic rejection, cooldown rules,
  input gate, picker interaction, and future-casts-only behavior.

## Files / Systems Mentioned By The Plan

- `Assets/Scripts/Skills/SkillLoadout.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/SkillLoadoutValidator.cs`
- `Assets/Scripts/Player/PlayerRoot.cs`
