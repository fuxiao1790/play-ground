# Implementation Context

## Architectural Decisions
- Retire the three flat modifier-kind interfaces in favor of seven per-stat nested interface families.
- Keep `StatModifierAccumulator` as the sole support-side stat fold: `(base * preMultiplier + added) * (1 + increased) * postMultiplier`.
- Trigger-edge mana cost stays separate from the accumulator; each link resolves `(1 + manaCostIncreasedPercent) * manaCostMultiplier`.
- Do not move serialized support fields. New support mana fields use neutral defaults.

## Global Invariants
- Support modifiers apply only within their compiled skill set.
- Explicit interface implementations are mandatory whenever identical signatures target different stats.
- Preserve all existing resolved values when new fields are at defaults.
- Do not use reflection or add runtime test hooks.

## Ownership Boundaries
- Skills, supports, triggers, compiler, and their EditMode tests are Game Logic concerns.
- ScriptableObject asset inspection/authoring is a user Unity-editor step; do not hand-edit asset YAML.

## Data Flow
- `SkillSetCompiler.CollectSupportModifiers` dispatches stat-specific interfaces into the existing sinks and accumulator.
- Trigger links provide a per-edge factor used for initial active-chain and triggered-cost calculations, plus interval energy thresholds.

## Lifecycle / Allocation Rules
- No ECS, allocation, threading, or lifecycle changes are in scope.

## ECS / Job / Threading Constraints
- None affected; retain the current Game Logic-only implementation boundary.

## Determinism Requirements
- Preserve existing support iteration and trigger-tree multiplication order.

## Producer / Consumer Separation
- No combat spawn, ECS simulation, or presentation paths are changed.

## Reused Mechanisms
- `StatModifierAccumulator`, `AddedSink`, `IncreasedSink`, `MultiplierSink`.
- Existing compiler collection loop and trigger recursion.

## Introduced Mechanisms
- Seven stat-specific modifier interface families with ten nested interfaces total.
- `TriggerLink.manaCostIncreasedPercent` and `ResolveManaCostFactor()`.
- `RuntimeSkillDefinition.IncomingManaCostFactor`.

## Validation Requirements
- Run relevant Unity EditMode tests headlessly when possible.
- Search all `Assets/` for obsolete bare modifier interfaces and old incoming-multiplier property.
- Task 005 is user-owned editor verification; report it as requiring the user rather than editing assets.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Skills/Support/ModifierKindInterfaces.cs` and nine support classes.
- `Assets/Scripts/Skills/SkillSetCompiler.cs`, `Trigger/TriggerLink.cs`, `Trigger/IntervalSpawnTrigger.cs`, `Runtime/RuntimeSkillDefinition.cs`.
- `Assets/Tests/EditMode/ModifierFoldEditModeTests.cs`, `SkillValidationEditModeTests.cs`.
- `Docs/reference/game-logic/skill-system.md`, `Docs/flows/resource-spend-gate.md`.
