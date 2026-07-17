# Implementation Context

## Architectural Decisions
- ECS owns a closed VFX data-shape set: `Basic` and `Timed`.
- Each graph is registered with exactly one shape; the encoded VFX id carries `(shape, local index)`.
- `AoeVfxIds` stays five plain `int` fields.
- Presentation stays single-source: one `CombatAoeVfxDispatchSystem`, one `CombatVfxRoot`, one dispatcher, one `ProducerHandle`.

## Global Invariants
- `0` remains the no-VFX sentinel.
- `Basic` payload remains positions plus area sizes only.
- `Timed` payload adds duration and tick interval.
- Shape buffer contracts are declared once in `VfxDataShapeTable`.
- Registration hard-fails on missing, mistyped, or unexpected extra `GraphicsBuffer` properties.

## Ownership Boundaries
- `CombatVfxRoot` allocates ids and owns VFX resources.
- `CombatAoeVfxDispatcher` validates graph contracts and uploads staged buffers.
- ECS producer jobs write typed requests to singleton-owned native queues.
- Presentation dispatch drains queues after completing the shared producer handle.

## Data Flow
- Emitters read an `AoeVfxIds` slot id and route by `DecodeShape(id)`.
- Requests enter per-shape native queues.
- Presentation buckets each shape by decoded local index.
- Root dispatches each graph slice through its shape-specific resource.

## Lifecycle / Allocation Rules
- Native queues and scratch lists are persistent singleton fields and disposed by the dispatch system.
- GPU buffers are owned by `AoeVfxTypeResources`, grow by doubling, and are released on dispose.
- Buffers are transient per-dispatch payloads; VFX graphs must sample them during initialization.

## ECS / Job / Threading Constraints
- Producers use `NativeQueue<T>.ParallelWriter`.
- All producer handles combine into the singleton `ProducerHandle`.
- Presentation completes `ProducerHandle` once before reading any queue.
- Bucketing jobs run on the main thread with Burst-compatible structs.
- No managed `VisualEffect` calls from ECS jobs.

## Determinism Requirements
- Queue ordering from parallel writers is not meaningful.
- Counting-sort grouping is by decoded per-shape local id.

## Producer / Consumer Separation
- Emitters enqueue visual-only requests.
- Dispatch system drains and updates stats.
- Damage/spawn events remain separate from VFX request paths.

## Reused Mechanisms
- Existing singleton queue/scratch ownership pattern.
- Existing `CombatVfxRoot` resource ownership.
- Existing dispatcher upload and `SendEvent` path.
- Existing authored AOE lifetime/tick interval fields.
- Existing pulse slot and `AoePulseVfxSystem`.

## Introduced Mechanisms
- `VfxDataShape`, `VfxSpawnRequest`, `TimedVfxSpawnRequest`, `VfxDataShapeTable`.
- Encoded `VfxId` helpers.
- `VfxTimingData` component.
- Per-shape queues, scratch lists, resources, validation, and dispatch.
- `VfxEmit.Enqueue` helper.

## Validation Requirements
- Compile after each task.
- Verify ids round-trip by shape/local index.
- Verify Basic registrations remain compatible.
- Verify Timed registrations upload timing buffers.
- Verify docs match shipped model.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Vfx/AoeVfxEcsComponents.cs`
- `Assets/Scripts/System/Vfx/VfxDataShapes.cs`
- `Assets/Scripts/System/Vfx/VfxEmit.cs`
- `Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs`
- `Assets/Scripts/System/Vfx/CombatAoeVfxDispatcher.cs`
- `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`
- `Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoes/AoeSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Aoes/AoeCollisionCore.cs`
- `Assets/Scripts/System/Aoes/ImpactAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoes/AoePulseVfxSystem.cs`
- `Assets/Scripts/System/Lifetime/CombatArmingSystem.cs`
- `Assets/Scripts/System/Lifetime/CombatLifetimeSystem.cs`
- `Assets/Scripts/System/Lifetime/CombatDeathUtility.cs`
- `Assets/Scripts/Skills/Validator/BasicAoePrefab.cs`
- `Assets/Scripts/Skills/Validator/LingeringAoePrefab.cs`
- `Assets/Scripts/Skills/SkillDefinition.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeAoeDefinition.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Docs/reference/simulation/vfx-system.md`
