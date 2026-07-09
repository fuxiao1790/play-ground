# Implementation Context

## Architectural Decisions
- Refactor to one authored source of truth for unit base stats: `UnitStatSheet`.
- `UnitStatSheet` is ScriptableObject-only authored data; code-built mobs use runtime-created clones.
- Offensive stats map 1:1 to existing `SkillStatSnapshot`; do not change compiler/fold shape.
- Health authority remains unchanged: GameObject owns max health, ECS owns current health.

## Global Invariants
- Max health is seeded into ECS target health once through the existing target proxy path.
- Current health flows from ECS back to roots through `ReceiveCombatTick`.
- Required serialized references are validated once during setup.
- Stat reads happen during setup/compile, not per-frame.
- Do not add defensive stats or second offensive representation.

## Ownership Boundaries
- `UnitStatSheet` owns immutable base stats: max health, move speed, offense fields.
- Player and mob roots read vitals/movement from the sheet.
- `SkillDriver` reads the caster sheet when compiling runtime skill definitions.
- Runtime HP, hurt, death, and target shape behavior stay on roots.

## Data Flow
- Sheet -> root `CombatMaxHealth` -> `CombatTargetProxy.Create` -> ECS `TargetHealth.Max`.
- ECS health result -> `ReceiveCombatTick` -> root mirror.
- Sheet -> `SkillStatAggregator.Aggregate(loadout, sheet)` -> `SkillSetCompiler`.
- Sheet -> root movement setup or mob wander speed.

## Lifecycle / Allocation Rules
- Authored sheet assets are never mutated at runtime.
- `MobRoot.ConfigureAuthoring` may create a `UnitStatSheet` clone and set vitals on that clone.
- Avoid per-frame allocation or scene searches in combat paths.

## ECS / Job / Threading Constraints
- This change should not alter ECS archetypes, jobs, queues, or structural-change timing.
- Target proxy lifecycle and combat result application remain unchanged.

## Determinism Requirements
- No new random/runtime ordering behavior.

## Producer / Consumer Separation
- Do not route stat data through combat hit/spawn/VFX event lanes.
- Do not widen existing combat damage or spawn contracts.

## Reused Mechanisms
- `SkillStatSnapshot`, `StatModifierAccumulator`, `SkillSetCompiler`.
- `CombatTargetProxy` max-health seeding through `ICombatTarget.CombatMaxHealth`.
- Existing root setup validation style.

## Introduced Mechanisms
- `PlayGround.Common.Stats.UnitStatSheet`.
- Runtime-only `UnitStatSheet.SetRuntimeValues(float maxHealth, float moveSpeed)` for code-built mobs.

## Validation Requirements
- Compile/build after code tasks.
- Search for removed root fields/usages.
- Task 005 requires Unity editor asset creation/wiring; do not hand-write `.asset` files.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Common/Stats/UnitStatSheet.cs`
- `Assets/Scripts/Skills/SkillStatSnapshot.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/Player/PlayerRoot.cs`
- `Assets/Scripts/Mob/MobRoot.cs`
- `Assets/ScriptableObjects/Units/` and player/mob prefabs for editor wiring.
