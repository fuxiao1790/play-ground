# Implementation Context

## Architectural Decisions
- Replace travel-spawn interval timing in place with one energy-accrual model. No compatibility mode, translation shim, new component, or parallel path.
- Trigger owns `energyPerSecond`; compiled child skill owns `spawnEnergyCost`, mapped to threshold.
- Timing stays in the slim per-source timed-spawn configuration and is excluded from template hashing.

## Global Invariants
- The Burst parallel per-entity tick keeps energy local and deterministic child ids derive from `TickIndex`.
- Per-tick threshold jitter reuses the current deterministic source-id hash.
- Clamp thresholds to a positive minimum; retain a 256-tick update cap; non-positive gain rate emits nothing.
- Timed spawn setup requires a positive rate, threshold, jitter seed, and template key.
- Child-set properties must not come from the parent trigger or set.

## Ownership Boundaries
- Triggers author gain rate and jitter percent. Child definitions author spawn cost.
- Template registry, spawn events/commands, expansion, and apply paths retain their existing payloads and behavior.
- Authoring assets must be changed only in the Unity editor, never as YAML.

## Data Flow
- Trigger + child definition -> `SkillSetCompiler` runtime setup -> `SkillDriver` / `CombatRoot` `TimedSpawnComponent` -> `TimedSpawnSystem` -> unchanged child event pipeline.

## ECS / Job / Threading Constraints
- `TimedSpawnJob` remains Burst, parallel, and free of cross-entity reads or managed access.
- Preserve existing enableable pooling and structural-change behavior.

## Determinism Requirements
- Threshold jitter is evaluated for `tickIndex + 1` before every emission using the existing hash.
- Child deterministic ids remain the emitted tick index.

## Reused Mechanisms
- Existing timed-spawn component/state, template registry split, deterministic jitter hash, `MaxTicksPerUpdate`, jitter-seed sentinel, and spawn pipeline.

## Introduced Mechanisms
- `spawnEnergyCost` authoring field and energy vocabulary replacing interval/cooldown vocabulary.

## Validation Requirements
- Validate each task with targeted static checks and relevant Unity edit/play tests when available.
- Stop on failed validation or architectural ambiguity. Task 005 is user Unity-editor re-authoring, not a code or YAML edit.

## Files / Systems Mentioned By The Plan
- Timed spawn components/system; projectile spawn request/apply; CombatRoot; skill definitions, triggers, compiler, driver; validation and spawn-pipeline tests; skill-system and spawn-template-registry docs.
