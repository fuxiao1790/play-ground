# Implementation Context

## Architectural Decisions
- Split AOE spawn intent by variant: impact AOE and lingering AOE use separate event structs, queues, scope buffers, expansion systems, command lists, apply lanes, and collision lanes.
- Variant is decided at authoring from child lifetime: lifetime > 0 means lingering; otherwise impact.
- Keep the existing two AOE archetypes: impact has no CombatLifetimeComponent; lingering has CombatLifetimeComponent.

## Global Invariants
- Do not add a runtime template lookup to determine AOE variant.
- Do not merge impact and lingering archetypes, apply systems, or collision systems.
- Preserve projectile-vs-AOE producer routing pattern, extended to projectile vs impact AOE vs lingering AOE.

## Ownership Boundaries
- Game logic/authored builders set IntervalChildKind and StackDetonationKind.
- CombatRoot appends managed scope-buffer events and owns scope setup.
- ECS producers enqueue spawn events into expansion-system queues.
- Expansion systems drain event queues/scope buffers and write AoeSpawnCommand lists.
- Apply systems consume only their matching variant command list.

## Data Flow
- Producers route by Kind to ImpactAoeSpawnEvent or LingeringAoeSpawnEvent.
- ImpactAoeSpawnExpansionSystem writes ImpactCommands.
- LingeringAoeSpawnExpansionSystem writes LingeringCommands.
- ImpactAoeSpawnApplySystem consumes impact commands; LingeringAoeSpawnApplySystem consumes lingering commands.

## Lifecycle / Allocation Rules
- Each expansion system owns a persistent NativeQueue and NativeList.
- Per-frame drained events use TempJob NativeArray and are completed/disposed at the start of the next update.
- Scope buffers are drained and cleared each expansion update.

## ECS / Job / Threading Constraints
- Producers combine their scheduled job handles into each target expansion system ProducerHandle.
- Expansion systems complete ProducerHandle before draining queues.
- Keep NativeQueue writers and command-list ownership lane-local.

## Determinism Requirements
- Preserve current event drain order within each lane: native queue events first, then scope-buffer events in scope order.
- Preserve existing scatter/id math and VFX enqueue behavior.

## Producer / Consumer Separation
- Spawn events stay spawn-only. Combat hit/tick results and VFX requests remain distinct paths.
- AOE variant is carried by IntervalChildKind / StackDetonationKind; producers do not inspect template lifetime.

## Reused Mechanisms
- ProjectileSpawnEvent / ProjectileSpawnExpansionSystem lane pattern.
- Shared static cores used by AOE collision and apply.
- Existing ProducerHandle chaining and TempJob disposal pattern.

## Introduced Mechanisms
- ImpactAoeSpawnEvent and LingeringAoeSpawnEvent.
- ImpactAoeSpawnExpansionSystem and LingeringAoeSpawnExpansionSystem.
- AoeExpansionCore shared expansion helper.
- AoeVariant helper methods for lifetime-to-kind classification.

## Validation Requirements
- No Scripts references to AoeSpawnEvent or AoeSpawnExpansionSystem after split.
- No authoring or producer code emits bare AOE kind.
- Unity test/build validation must compile after tasks 001-004 and task 006 updates.

## Files / Systems Mentioned By The Plan
- Assets/Scripts/System/Common/IntervalChildTemplates.cs
- Assets/Scripts/System/Status/StackEffectSnapshot.cs
- Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs
- Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs
- Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs
- Assets/Scripts/System/Aoe/AoeCollisionCore.cs
- Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs
- Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs
- Assets/Scripts/System/Common/CombatRoot.cs
- Assets/Scripts/System/Common/CombatEcsComponents.cs
- Assets/Scripts/System/Common/TimedSpawnSystem.cs
- Assets/Scripts/System/Common/CombatApplyFinalizeSystem.cs
- Assets/Scripts/System/Common/CombatApplyFinalizeSingleSystem.cs
- Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs
- Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs
- Assets/Scripts/System/Status/StatusProcessSystem.cs
- Assets/Scripts/Skills/PlayerSkillDriver.cs
- Assets/Scripts/Skills/SkillSpawnTranslator.cs
