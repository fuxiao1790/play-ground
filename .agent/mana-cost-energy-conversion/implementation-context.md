# Implementation Context

## Architectural Decisions
- `spawnEnergyCost` becomes one folded `manaCost` source. No duplicate field.
- Interval trigger link converts folded child mana into a plain `EnergyThreshold`.
- Player mana is seeded into ECS like health. Consumption and gating are deferred.

## Global Invariants
- Fold skill stats at compile time in `SkillSetCompiler.BuildRuntime`, never per frame.
- Burst timed-spawn jobs only read a baked float threshold.
- Preserve serialized asset values with `FormerlySerializedAs`.

## Ownership Boundaries
- `UnitStatSheet` authors player maximum mana.
- `TargetMana` is seeded at target proxy creation and owned by ECS until destruction.
- `PlayerMana` is the managed mirror.

## Data Flow
- Authored mana cost -> stat modifier fold -> runtime definition mana cost -> trigger conversion -> timed spawn threshold.

## ECS / Job / Threading Constraints
- Do not add per-frame stat folding or cross-entity mana writes to the parallel timed-spawn job.
- `TargetMana` shares the target-proxy lifecycle of `TargetHealth`.

## Reused Mechanisms
- `SkillStat`, `StatModifierAccumulator`, and support modifier interfaces.
- `TargetHealth` and `PlayerHealth` lifecycle/mirror patterns.

## Introduced Mechanisms
- `IntervalSpawnTrigger` owns the mana-to-energy conversion.
- `TargetMana` and `PlayerMana`.

## Validation Requirements
- Run relevant Unity EditMode/PlayMode tests after each task when available.
- Preserve old serialized values and verify no non-historical documentation references to `spawnEnergyCost` remain.

## Files / Systems Mentioned By The Plan
- Skills definitions, runtime definitions, compiler, supports, interval triggers, validation tests.
- Unit stat sheet, combat target proxy/interface, player root, player health analogue.
