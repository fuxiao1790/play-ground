# Implementation Context

## Architectural Decisions
- `SkillDriver` owns mutable runtime loadouts, validation, compilation, root cooldowns, revisions, and edit result events.
- `SkillLoadoutUi` is presentation only: it reads driver state and submits `SkillLoadoutEditCommand` values.
- Keep `CompileAndRegister()` a single private zero-argument method because an existing PlayMode test reflects and invokes it with no parameters.

## Global Invariants
- Accepted edits are revision-gated and the driver applies at most one pending edit per `Tick`.
- Node indices are stable; clearing a skill retains its node.
- Unchanged direct roots retain cooldown progress across an accepted edit. The edited root resets only for skill/support/cap edits; trigger edits never reset cooldown.
- Loadout restore compiles fresh cooldown state and must not preserve prior session state.
- On success `LoadoutChanged` precedes `EditResolved`; a rejection emits only `EditResolved`.
- UI never mutates gameplay state locally or makes ECS decisions.

## Ownership Boundaries
- Authored skills, supports, triggers, and loadouts are shared template assets and never mutate at runtime.
- Runtime loadout clones belong to `SkillDriver`.
- UI owns picker focus and pending presentation state only.

## Data Flow
- UI queues one edit command; `SkillDriver.Tick` validates, compiles, swaps state, increments revision, and emits events.
- UI refreshes from driver state and releases a pending picker only after `EditResolved`.

## Lifecycle / Allocation Rules
- UI per-frame work is limited to cached, low-count presentation updates.
- Do not add allocations or work to high-count ECS combat paths.

## ECS / Job / Threading Constraints
- This task set is managed Game Logic/UI work; do not create ECS entities or add managed access to ECS jobs.

## Determinism Requirements
- Keep existing node order and single-command tick ordering.

## Producer / Consumer Separation
- UI is the command producer. `SkillDriver` is the sole validator/compiler and cooldown authority.

## Reused Mechanisms
- `SkillSlotState`, `FindRootSlotForNode`, `SkillDefinitionTags.HasAny`, driver revision/events, and the UI `Update()` refresh path.

## Introduced Mechanisms
- Driver preservation flags communicate edit cooldown reset context to zero-argument compilation.
- `SkillSupport.SupportedSkillTags` has a base default of `Any`; stat modifier supports keep their specialized authored value.

## Validation Requirements
- Run each task's specified static and PlayMode validation when available.
- Preserve the reflective `CompileAndRegister()` test contract.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/SkillUi/SkillLoadoutUi.cs`
- `Assets/Scripts/Skills/Support/SkillSupport.cs`
- `Assets/Scripts/Skills/Support/StatModifierSupport.cs`
- `Docs/ui.md`
