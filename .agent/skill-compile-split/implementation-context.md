# Implementation Context

## Architectural Decisions
- Extract pure loadout compilation from `SkillDriver` into `SkillLoadoutCompiler`.
- Keep registration and runtime slot-state ownership in `SkillDriver`.

## Global Invariants
- Compilation runs only on equip/edit/restore paths, never per frame.
- Compiler has no fields, no static mutable state, no scene/combat references, and no side effects.
- Preserve root selection, compile ordering, warning ordering, and active-slot/index mapping.

## Ownership Boundaries
- `SkillDriver` owns runtime loadout clones, slot state, registration, and published warnings.
- `SkillLoadoutCompiler` only reads loadout nodes/capacity and returns fresh compiled data.

## Data Flow
- `SkillLoadout` + `SkillStatSnapshot` -> `CompiledLoadout` -> `SkillDriver` slot-state reconciliation -> registration -> published warnings.

## Lifecycle / Allocation Rules
- Compile-time allocations are allowed; no hot-path impact.
- `CompiledLoadout` contains roots, root node indices, active count, and mutable warning list for registration-time append.

## ECS / Job / Threading Constraints
- No ECS or simulation ownership in compiler.

## Determinism Requirements
- Same inputs preserve root selection, root order, compile order, and warning order.

## Producer / Consumer Separation
- Compiler produces runtime skill definitions and compile warnings.
- `SkillDriver` consumes them and performs combat-root/VFX registration.

## Reused Mechanisms
- `SkillLoadoutValidator.Validate`.
- `SkillSetCompiler.Compile`.
- Existing root detection, triggered-only conversion filtering, and compiler-warning traversal logic.

## Introduced Mechanisms
- `SkillLoadoutCompiler.Compile`.
- `CompiledLoadout` readonly struct.

## Validation Requirements
- Do not run tests; project rules defer tests to user.
- Use scoped diff/search checks and provide exact Unity EditMode XML test command.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Skills/SkillLoadoutCompiler.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/SkillLoadoutValidator.cs`
