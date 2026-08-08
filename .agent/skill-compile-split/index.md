---
name: skill-compile-split
description: Extract pure compilation from SkillDriver into its own class; registration stays with state management
---

# Skill Compile/State Split

## Summary

`SkillDriver.cs` ([Assets/Scripts/Skills/SkillDriver.cs](../../Assets/Scripts/Skills/SkillDriver.cs), 1695 lines) currently
inlines three separable jobs inside `CompileAndRegister`
([SkillDriver.cs:191-254](../../Assets/Scripts/Skills/SkillDriver.cs#L191-L254)): equipment-state edit handling, pure
compilation (root-node detection + per-root compile + compile-time warnings), and stateful registration
(`CombatRoot`/`CombatVfxRoot` type + spawn-template registration). User decision: pull the pure compilation piece
into its own class with zero fields and zero side effects. Registration mutates `CombatRoot`'s registries and
writes `TypeId`/`RenderId`/`SpawnTemplateKey`/`VfxIds`/`DebuffKey` back onto the compiled tree — that is state
management and stays on `SkillDriver`.

New type: `SkillLoadoutCompiler.Compile(SkillLoadout loadout, SkillStatSnapshot snapshot) -> CompiledLoadout`,
in `Assets/Scripts/Skills/SkillLoadoutCompiler.cs`, same namespace/assembly as `SkillDriver`.

## Constraints & Invariants

- Compilation must not run per frame — only on equip/edit. Source:
  [skill-system.md#Layer-2-Orchestration](../../Docs/reference/game-logic/skill-system.md#L136-L146) ("does not run
  per frame"), [skill-system.md#Layer-1](../../Docs/reference/game-logic/skill-system.md#L117) ("No per-frame stat
  math; scaling is rebaked on equipment swaps"). The extraction is a same-caller-count refactor, so this holds
  unchanged.
- Root MonoBehaviour should "construct or bind focused helper classes" and should not "own all gameplay decisions
  directly." Source: [coding-standards.md#Root-Component-Rule](../../Docs/coding-standards.md#L6-L22).
- `SkillLoadoutCompiler` must stay inside the Game Logic layer (same assembly as `SkillDriver`/`SkillSetCompiler`),
  not cross into Sim/Ui. Source: [layers/game-logic.md#Owns](../../Docs/layers/game-logic.md#L13-L14) (lists
  `SkillDriver`, `SkillLoadout`, `SkillSetCompiler`, `SkillSpawnTranslator` together),
  [folder-structure.md](../../Docs/folder-structure.md#L79-L80) (`PlayGround.GameLogic.asmdef`).
- Runtime `SkillLoadout`/`SkillSet` clones are owned by `SkillDriver`, not by helper classes. Source:
  [skill-loadout-editing.md#Ownership](../../Docs/contracts/skill-loadout-editing.md#L14). `SkillLoadoutCompiler`
  must only read `loadout.Nodes`/`loadout.MaxRootSets`, never store a reference to the loadout or mutate it.
- No per-frame allocation constraint applies here — compile/registration are not in the documented hot-path list.
  Source: [coding-standards.md#Allocation-Rule](../../Docs/coding-standards.md#L293-L316) (hot paths listed are
  spawn/simulation/render, not compile).
- Existing tests call `SkillSetCompiler.Compile` and `SkillLoadoutValidator.Validate` directly (7 EditMode/PlayMode
  files, confirmed by grep); `SkillDriver.CompileAndRegister` is private and untested directly. Neither call is
  changed by this task, so no test breakage expected from those call sites.

## Mechanisms Reused vs. Introduced

Reused, unchanged:
- `SkillSetCompiler.Compile(nodes, nodeIndex, snapshot)` — already a pure static per-root compiler.
- `SkillLoadoutValidator.Validate(loadout)` — already a pure static validator.
- `SkillValidationWarning`, `RuntimeSkillDefinition` — unchanged shapes.

Introduced:
- `SkillLoadoutCompiler` (static class) + `CompiledLoadout` (`readonly struct`, matching the existing
  `CompileDefinitionResult` pattern in
  [SkillSetCompiler.cs:429](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L429)). Justification: no existing type
  owns "detect root nodes, compile each, collect compile-time warnings" as one pure step — today it is inline in a
  stateful `MonoBehaviour` method with no way to call or test it without a live `SkillDriver`.

## Design Validation

- Per-frame rule: `SkillLoadoutCompiler.Compile` is called from the same two sites `CompileAndRegister` already
  runs from (`Start`, and edit/restore paths) — call count and timing unchanged.
- Root Component Rule: `SkillDriver` becomes the caller that binds `SkillLoadoutCompiler` output to slot-state and
  registration; it stops containing the root-detection/compile-loop/warning-walk logic directly.
- Layer/ownership: `SkillLoadoutCompiler` takes `SkillLoadout` by reference for read-only access
  (`.Nodes`, `.MaxRootSets`) and returns a freshly allocated `CompiledLoadout` each call — it stores no reference
  anywhere, holds no fields, and never touches `CombatRoot`/`CombatVfxRoot`. Ownership of the runtime loadout stays
  on `SkillDriver`.

## Minimal/Additive vs. Refactor Comparison

**Minimal/additive**: Leave `CompileAndRegister` as one method; add comments marking which lines are "pure" vs
"state."
- Resulting data flow: unchanged — one ~260-line method mixing both concerns.
- New concepts/types: none.
- Copies/translations added: none.
- Long-term cost: compile logic stays untestable without a scene/MonoBehaviour; every future skill-system change
  keeps landing in the same oversized file; the exact problem raised in this conversation is not solved.

**Refactor (chosen)**: extract `SkillLoadoutCompiler` (pure) called by `SkillDriver.CompileAndRegister` (state).
- Resulting data flow: `nodes + snapshot -> CompiledLoadout -> (slot-state reconcile in SkillDriver) -> Register*`.
  One additional typed hop; no additional copies — the root-indices array, compiled-defs array, and warnings list
  are each still built exactly once, just behind a method boundary instead of inline.
- Existing concepts changed: `CompileAndRegister` shrinks to orchestration only (call compiler, reconcile slot
  state, call `Register*`).
- Long-term benefit: compile logic becomes callable/testable without a `MonoBehaviour`; `SkillDriver.cs` loses the
  root-detection loop, `HasTriggeredOnlyConversionSupport`, and `AppendCompilerWarnings` (~120 lines); matches the
  already-written Root Component Rule.
- Decision: **refactor**. Reason: matches an existing documented convention and the explicit split requested; the
  additive option leaves the stated problem unsolved.

## Default Decision Rule

Only one representation of "compiled loadout" exists after this task: `CompiledLoadout`, returned by
`SkillLoadoutCompiler.Compile`. `SkillDriver` keeps no parallel/duplicate compile path — the old inline blocks are
deleted in the same change that wires in the new call, not kept behind a flag or left dead.

## Tasks

1. [001-add-skill-loadout-compiler.md](001-add-skill-loadout-compiler.md) — add `SkillLoadoutCompiler.cs` (additive,
   nothing calls it yet).
2. [002-rewire-skilldriver-compile-and-register.md](002-rewire-skilldriver-compile-and-register.md) — wire
   `SkillDriver.CompileAndRegister` to call it and delete the now-dead inline logic.

## Open Questions

- `SkillSetCompiler.cs:15` (`nextChildJitterSeed`) is a mutable static counter inside the *already-separate*
  compile step, minting jitter seeds the same way `SkillDriver`'s `nextStackingDebuffKey` mints debuff keys. Under
  a strict "0 state" reading it's a violation, but it predates this task and sits in a file the user did not ask to
  change. **Scoped out of this task** — flagged here rather than silently touched or silently ignored. Revisit
  separately if the "0 state" rule is meant to reach `SkillSetCompiler` too.
- `CompiledLoadout.Warnings` is typed `List<SkillValidationWarning>` rather than an array, because
  `SkillDriver.CompileAndRegister` needs to keep appending to the *same* list during registration
  (`SpawnChainDepthExceeded` warnings are added inside `RegisterSpawnTemplatesRecursive`, which runs after compile
  — see [SkillDriver.cs:685-689](../../Assets/Scripts/Skills/SkillDriver.cs#L685-L689)). Returning a mutable list
  from a "pure" function is a return-value shape, not statefulness of the class — no field, no shared/cached
  instance, freshly allocated per call. Flagging in case you'd rather `SkillLoadoutCompiler` return an array and
  have `SkillDriver` copy it into a new list itself (one extra allocation, stricter output-immutability).
