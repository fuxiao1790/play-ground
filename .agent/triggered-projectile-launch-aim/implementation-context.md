# Implementation Context

## Superseding Requirement

After tasks `001`-`006` were implemented, user revised wave behavior. Current code's
"replace only shot index `0`" behavior is obsolete. Required result: successful
launch acquisition uses aimed direction as angular origin for whole radial nova.
Shot `i` direction is `Rotate(aimDirection, 360 * i / count)`; shot `0` points at
target. Disabled/failed acquisition retains current authored pattern. Follow-up
tasks `007`-`009` own implementation, tests, and docs.

## Architectural Decisions
- Author launch-aim policy on `TriggerLink`, not `SkillDefinition`. Root/player casts never enable this policy.
- One shared enum `ProjectileLaunchAimMode`: `None = 0`, `NearestHostile = 1`, plus authored non-negative acquisition range.
- Policy copied at compile: `TriggerLink` -> `RuntimeProjectileDefinition` (only when incoming trigger's compiled target is a projectile) -> `ProjectileSpawnCommand`/template. Not added to `ProjectileSpawnEvent`.
- Acquisition happens once per `ProjectileSpawnEvent`/wave, before count expansion and before discrete/continuous lane split. Successful acquisition rotates whole radial nova from aimed direction; shot index `0` points directly at target.
- `TargetedAcquisition` refactored/renamed to domain-neutral `CombatTargetAcquisition`; reused by both targeted chains and projectile launch aim (no duplicate nearest-hostile algorithm).
- Launch aim and homing/tracking are independent. Continuous projectiles can launch-aim but structurally cannot home (no tracking component). Discrete may use either/both.
- Excludes `ContactGateSeedTargetId` from acquisition (on-hit/stack waves can't select their temporarily-forbidden target).

## Global Invariants
- No new entity component, archetype, pool, or system. Launch policy lives only in authoring/runtime/template data, not entity state.
- `event -> expansion -> command -> apply` flow preserved; expansion owns direction/count/spread/jitter/IDs/lane routing; apply only allocates/reuses entities.
- Spawn-template registry immutable during simulation tick; policy fields participate in existing whole-struct `SpawnTemplateHash`.
- Missing hash singleton, non-positive range, `CombatFaction.None`, no hostile in range, or target coincident with spawn position => safely retain existing trigger direction/pattern (no zero-vector normalization, no exceptions).
- Burst-compatible, allocation-free, one query per event/wave (not per projectile).
- Jobs reading persistent hash containers depend on `BuildHandle` and publish read handle into `ConsumerHandle`.

## Ownership Boundaries
- Game logic (editor/compiler) owns trigger authoring and compilation; ECS only consumes copied plain data (no live TriggerLink/SkillDefinition/GameObject/Transform/Collider/managed target refs in ECS/jobs).
- `TriggerLink` is authored source of truth; `RuntimeProjectileDefinition` and command template are immutable snapshot copies, not competing authorities.

## Data Flow
trigger fields (mode+range on `TriggerLink`) -> compiled onto `RuntimeProjectileDefinition` only for the trigger's compiled projectile target (not root) -> `SkillIntervalTemplateBuilder` copies into command-shaped `ProjectileSpawnCommand`/template -> `ProjectileSpawnExpansionSystem` resolves template, runs one `CombatTargetAcquisition` query per event if mode=NearestHostile, builds aim-oriented radial nova on success or existing authored pattern on failure -> existing discrete/continuous command lists -> existing apply systems (unchanged).

## Lifecycle / Allocation Rules
- Acquisition only initializes velocity once at spawn; no per-frame steering, no target entity storage, no post-spawn tracking added by launch aim itself.
- Successful aimed nova uses deterministic radial math and does not consume normal side-spray/forward jitter RNG. Disabled/failed acquisition keeps existing RNG behavior unchanged.

## ECS / Job / Threading Constraints
- Reuse `TargetSpatialHashSingleton` (`AoeOccupiedCells`) and existing nearest-hostile collision-shape query via `CombatTargetAcquisition.TrySelectNthNearest` (rank 0, excluded = `ContactGateSeedTargetId`).
- Combine expansion job dependency with hash `BuildHandle`; publish expansion's read handle into hash `ConsumerHandle` after scheduling.
- Missing singleton must be handled gracefully (stripped test worlds) — fallback to existing pattern, no throw.

## Determinism Requirements
- Deterministic shot index `0` is radial angular origin; every wave direction is rotated from it by `360/count`. Wave count unchanged. Same-faction targets skipped. Contact-gate seed target excluded, next-nearest hostile chosen instead.
- Disabled/no-target/missing-hash/zero-range/coincident-target path must produce byte-for-byte identical output vs. launch aim disabled.

## Producer / Consumer Separation
- `ProjectileSpawnExpansionSystem` (producer) owns wave direction/pattern/acquisition; apply systems (`ProjectileDiscreteSpawnApplySystem`, `ProjectileContinuousSpawnApplySystem`) remain consumers that only allocate/reuse entities — unchanged by this feature.

## Reused Mechanisms
- `TriggerLink` authored edge; `SkillSetCompiler` per-edge runtime compilation.
- `RuntimeProjectileDefinition -> ProjectileSpawnCommand` snapshot path; command-shaped template registration + `SpawnTemplateHash`.
- `ProjectileSpawnExpansionSystem` as sole owner of wave direction/pattern.
- `TargetSpatialHashSingleton.AoeOccupiedCells` + existing nearest-hostile collision-shape query (via renamed `CombatTargetAcquisition`).
- Existing faction and contact-gate target keys; existing discrete/continuous command lists/pools.

## Introduced Mechanisms
- `ProjectileLaunchAimMode` enum (`None=0`, `NearestHostile=1`).
- Trigger-authored launch-aim mode + range fields, copied into runtime projectile and projectile template.
- Rename/move `Assets/Scripts/System/Targeted/TargetedAcquisition.cs` -> domain-neutral `CombatTargetAcquisition` (relocate out of Targeted-specific ownership; update callers in `ExternalSpawnGateSystem`, `TargetedResolveSystem`, targeted tests).

## Validation Requirements
- Agent must NOT run Unity tests. User runs named EditMode/PlayMode tests and exports XML under `Logs/`. Any pass/fail claim requires XML review — do not claim tests passed without seeing exported results.
- Where compile/static verification is possible (code review, grep-based checks, reasoning about acceptance criteria), do that per task; explicitly state when validation could not be run and why.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Targeted/TargetedAcquisition.cs` (rename target), `TargetedResolveSystem.cs`, `ExternalSpawnGateSystem.cs` (find via grep), targeted tests.
- `Assets/Scripts/Skills/Trigger/TriggerLink.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`, `SkillIntervalTemplateBuilder` (find via grep)
- `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs`
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteSpawnApplySystem.cs`, `ProjectileContinuousSpawnApplySystem.cs`
- `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs`
- `Assets/Scripts/System/Spawning/SpawnTemplateComponents.cs`, `Assets/Scripts/System/Core/CombatRoot.cs`
- Tests: `SkillValidationEditModeTests`, `SpawnCommandUnificationTests`, `ProjectileSpawnPipelineTests`
- Docs (task 006 only): `Docs/reference/game-logic/skill-gameplay-system.md`, `Docs/contracts/skill-runtime-snapshots.md`, `Docs/contracts/spawn-events-and-commands.md`, `Docs/reference/simulation/projectile-system.md`, `Docs/reference/simulation/spawn-template-registry.md`, `Docs/flows/spawn-event-to-entity.md`
