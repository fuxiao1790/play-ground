# Implementation Context

## Architectural Decisions
- Replace legacy hit-count stacking with target-local float hit energy; keep only HitEnergy vocabulary in final runtime.
- Skill owns positive `TriggerEnergy`; each compiled edge owns a distinct `AccumulatorId`.
- `RuntimeHitEnergyTrigger` is source-attached composition, not a `RuntimeSkillDefinition` subtype.
- Registered spawn template is output source of truth; accumulated energy controls timing/count only.

## Global Invariants
- Only adjacent source node funds its next node.
- Reused Skill/SkillSet assets still compile to independent runtime nodes and edge identities.
- Triggered nodes remain excluded from direct-cast roots.
- In-flight entities keep copied snapshot values after recompilation.
- Use one positive floor of `1e-3f` for authored/runtime hit-energy values.
- Interval energy and mana behavior remain separate and unchanged.

## Ownership Boundaries
- Game logic owns authoring and compilation.
- ECS owns target-local high-count accumulation and activation.
- Presentation reads compact managed progress only.

## Data Flow
- `Skill.TriggerEnergy` -> `RuntimeSkillDefinition.TriggerEnergy` -> `HitEnergyPayload`.
- Accepted hit deposits `EnergyPerHit`; complete `EnergyRequired` emits registered triggered template and banks remainder.

## Lifecycle / Allocation Rules
- Finalization deposits energy and refreshes expiry.
- Activation system expires state, consumes thresholds, and emits spawns next simulation update.
- No managed allocation or authoring lookup per hit.
- Maximum 32 accumulator entries per target and 256 activations per target/update; preserve overflow.

## ECS / Job / Threading Constraints
- Snapshot and ECS values stay unmanaged and Burst-safe.
- Reuse existing native spawn lanes and entity archetypes; add no event lane or structural-change path.
- Preserve process-before-finalize ordering.

## Determinism Requirements
- Each compiled edge receives a unique id independent of asset identity, values, runtime type, or template key.
- Queue order is not a gameplay contract.

## Producer / Consumer Separation
- Source runtime owns only its outgoing hit-energy edge.
- Payload carries only copied primitives/ids needed by ECS.
- Registered triggered template owns damage, area, count, crit, launch, and descendant behavior.

## Reused Mechanisms
- Skill compiler and adjacent recursive graph.
- Direct-root filtering, fire-time payload propagation, bounded target buffer, frame ordering, native spawn lanes, and template registry.

## Introduced Mechanisms
- `TriggerEnergy`, `HitEnergyTrigger`, `RuntimeHitEnergyTrigger`, `AccumulatorId`, `HitEnergyPayload`, `TargetHitEnergy`, `HitEnergySpawn`, `HitEnergyActivationSystem`, and `HitEnergyProgress`.

## Validation Requirements
- Add EditMode coverage for compiler semantics, fractional multipliers, edge isolation, ECS threshold/remainder/retention/cap behavior, and asset migration.
- Agents do not run Unity tests. User exports `Logs/TestResults-EditMode-HitEnergy.xml`; agent reviews XML before claiming pass.
- Use static searches and code inspection after each task.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Skills/Skill.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeSkillDefinition.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- combat spawn payloads and projectile/AOE/targeted propagation
- `CombatApplyFinalizeSingleSystem.cs`
- legacy status processor and managed combat progress presentation
- Skill and trigger assets plus contracts/docs named by task 005
