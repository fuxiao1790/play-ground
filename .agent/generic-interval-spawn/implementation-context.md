# Implementation Context

## Architectural Decisions
- Merge `ProjectileIntervalSpawnTrigger` and `AoeIntervalSpawnTrigger` into one `IntervalSpawnTrigger`.
- Keep runtime/ECS data shapes unchanged: projectile children use `RuntimeChildSpawnSetup`; AOE children use `RuntimeAoeIntervalSpawnSetup`.
- Compiler dispatch keys on the compiled child runtime type, not on separate trigger variants.

## Global Invariants
- Pulse AOE sources have no lifetime for interval ticking and remain no-op with validator warning.
- Lingering AOE and projectile sources can use interval spawn.
- One trigger asset must support projectile-to-projectile, projectile-to-AOE, AOE-to-projectile, and AOE-to-AOE.
- Existing projectile interval script and asset GUIDs must travel through rename.

## Ownership Boundaries
- This work touches authoring, compile, validation, tests, and skill-system docs only.
- ECS/runtime systems and setup structs stay unchanged.

## Data Flow
- `SkillSetCompiler.Compile` compiles parent, finds trigger chains, compiles child slot recursively, then writes the matching interval setup onto the parent runtime definition.
- Validator checks source and target tags from trigger properties, plus pulse-AOE source guard for interval triggers.

## Lifecycle / Allocation Rules
- No new runtime allocation path or entity lifecycle change is introduced.
- Existing compiled setup objects and jitter seed behavior remain.

## ECS / Job / Threading Constraints
- No ECS/job/threading code should change for this task.

## Determinism Requirements
- Keep `nextChildJitterSeed` increment behavior for successful projectile or AOE interval setup creation.

## Producer / Consumer Separation
- Do not route spawn follow-up through damage or managed target callbacks.
- Keep runtime spawn setup fields as current producer/consumer contract.

## Reused Mechanisms
- Existing recursive child compilation.
- Existing compiled child type checks for projectile vs AOE.
- Existing pulse-AOE source validation and compiler guard.

## Introduced Mechanisms
- No new mechanism; this is consolidation only.

## Validation Requirements
- Confirm old trigger symbols no longer compile-time referenced.
- Build the Unity C# project if possible.
- Run or report inability to run Unity EditMode/PlayMode tests listed by the task.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Skills/Trigger/ProjectileIntervalSpawnTrigger.cs`
- `Assets/Scripts/Skills/Trigger/AoeIntervalSpawnTrigger.cs`
- `Assets/ScriptableObjects/Triggers/ProjectileIntervalSpawnTrigger.asset`
- `Assets/ScriptableObjects/Triggers/AoeIntervalSpawnTrigger.asset`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/SkillLoadoutValidator.cs`
- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`
- `Docs/reference/game-logic/skill-system.md`
