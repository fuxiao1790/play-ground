# 001 — Compiler keyed by slot index

## Structural role
`SkillSetCompiler` + `PlayerSkillDriver.ParseChains` are the compile phase that
turns the authored slot list into the runtime definition tree. Today they encode
chain identity by `SkillSet` *asset reference*, which collapses repeated assets
into a single instance and forces the `selfRef` recursion guard. This task makes
**slot position** the sole identity, matching the design doc's "slots are
independent compilation units."

## Ownership / data flow
- Input: `IReadOnlyList<LoadoutSlot>` (ordered slots).
- `ParseChains` emits `TriggerChain { int causeIndex; TriggerLink link; int effectIndex; }`.
- `SkillSetCompiler.Compile(int slotIndex, …)` builds from `slots[slotIndex]` and
  recurses on chains where `causeIndex == slotIndex`.
- Root detection: a slot index that never appears as an `effectIndex`.

## Change
- `TriggerChain` (in `Assets/Scripts/Skills/Trigger/TriggerLink.cs` or wherever it
  is declared): replace `SkillSet cause/effect` with `int causeIndex/effectIndex`.
  Keep `link`.
- `PlayerSkillDriver.ParseChains`: record indices; pass the slot list through to
  the compiler so indices resolve.
- `SkillSetCompiler.Compile`: signature takes the slot list + a slot index (or a
  small resolved `RuntimeSlot[]` view). Iterate chains by `causeIndex`.
  **Delete the `selfRef` branch** (lines ~33-40) and the empty-chain special
  case — forward-only adjacency (`effectIndex == causeIndex + 2`) cannot cycle,
  so recursion strictly increases the index and always terminates.
- `PlayerSkillDriver.CompileAndRegister`: root detection by index set; build one
  compiled `RuntimeSkillDefinition` per root slot index.
- Effect-set membership (`effectSets`) becomes an index set.

## Structural notes
- Document in a comment on `Compile`: "Adjacency is forward-only (`i → i+2`);
  recursion terminates by strictly increasing slot index. No cycle is possible,
  so no recursion guard is needed."
- Do not reintroduce asset-reference comparisons anywhere in chain wiring.

## Acceptance criteria
- A loadout with the **same** AOE skill-set asset in two stage slots compiles to
  **two independent** `RuntimeAoeDefinition` instances with the correct nested
  `StackTriggerSetup` (`LingerA → LingerB → Impact`), not a collapsed single
  instance pointing straight at `Impact`.
- Distinct-asset chains compile unchanged.
- Existing skill EditMode tests pass; new test added in 006.

## Dependencies
None. Independently mergeable.

## Scope
Small–medium. Pure compile-side refactor; no ECS or snapshot changes.
