# Implementation Context

## Architectural Decisions
- `StackTrigger` is sole authoring and compile owner for stacking detonation accrual settings and wrapper construction.
- Delete `StackingSupport`, `ConversionSupport`, their compiler path, bespoke validation, and obsolete warning enum value.
- No compatibility shim; Low/High support presets are intentionally dropped.

## Global Invariants
- `RuntimeStackingDetonation.DebuffKey` remains registration-minted, never authored; every compiled wrapper instance remains distinct.
- Effect nodes compile to their inner runtime definition. Their outgoing triggers attach to that inner definition; wrappers never spawn.
- Wrap target before stamping incoming mana multiplier. Inner detonation carries mana cost.
- A node is a root only when it has no incoming trigger by position.
- Stack targets must be `Projectile | Aoe`; generic trigger tag validation replaces bespoke checks.

## Ownership Boundaries
- Trigger assets own authored trigger settings.
- Runtime wrappers retain detonation metadata; `SkillDriver` remains responsible for key registration and snapshot materialization.
- ScriptableObjects hold reusable authored data, not mutable per-instance state.

## Data Flow
- Applicator runtime --`StackTrigger`--> compile target inner runtime --> construct `RuntimeStackingDetonation` --> stamp mana factor --> assign applicator's `StackingDetonation`.
- Nested outgoing links on target attach while target is still inner runtime.

## Lifecycle / Allocation Rules
- No ECS lifecycle or allocation changes. Runtime wrapper allocation remains per compiled chain instance.

## ECS / Job / Threading Constraints
- No ECS/job changes.

## Determinism Requirements
- Preserve per-wrapper registration identity and existing mana aggregation.

## Producer / Consumer Separation
- No event or consumer changes.

## Reused Mechanisms
- Public trigger configuration fields, `CompileInternal`, generic source/target tag validation, positional root detection, and existing driver wrapper traversal.

## Introduced Mechanisms
- None.

## Validation Requirements
- Do not run Unity tests. User must run requested tests and export XML under `Logs/`; inspect that XML before claiming tests passed.
- Perform source/static checks where possible. Confirm deleted references are absent and structural compile/data-flow conditions are present.

## Files / Systems Mentioned By The Plan
- `StackTrigger`, `SkillSetCompiler`, `SkillLoadoutCompiler`, `SkillLoadoutValidator`, `SkillValidationWarning`
- `StackingSupport`, `ConversionSupport`, affected EditMode/PlayMode tests, authored assets/catalog, and five reference docs.
