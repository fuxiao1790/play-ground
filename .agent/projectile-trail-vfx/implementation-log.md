# Implementation Log

## Status
Complete

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-authoring-projectile-trail-slot.md | Complete | Added authored `TrailStepDistance` (`0.5f` default) and validation warning. |
| 002-runtime-and-registration-wiring.md | Complete | Threaded `TrailStepDistance` through runtime definition and skill-built command. |
| 003-ecs-trail-component-and-archetypes.md | Complete | Added `StepDistance` and spawn-seeded `LastEmitPosition`; common path resets both on reuse. |
| 004-movement-system-emission.md | Complete | Replaced per-tick emission with squared-distance gate and mutable trail state. |
| 005-test-fixture-archetype-updates.md | Complete | Updated every direct fixture in worlds that schedule movement; created missing VFX singleton owners in two affected test worlds. Static audit passed; Unity tests deferred. |
| 006-docs-updates.md | Complete | Updated VFX, projectile, and skill authoring references for distance-gated pacing. |

## Completed Tasks
- `001-authoring-projectile-trail-slot.md`: changed `BasicAttackPrefab`, `SkillValidationWarning`, and `SkillLoadoutValidator`; `IsValidTemplate` untouched.
- `002-runtime-and-registration-wiring.md`: changed runtime definition, `SkillDriver`, and projectile command; direct `CombatRoot` path unchanged.
- `003-ecs-trail-component-and-archetypes.md`: changed projectile component definition and both spawn-apply lanes; common path overwrites trail data on reuse.
- `004-movement-system-emission.md`: changed only `ProjectileMovementSystem`; uses existing fail-loud VFX singleton and queue dependency pattern.
- `005-test-fixture-archetype-updates.md`: updated direct movement fixtures in both pool-cleanup classes, continuous simulation, and spawn pipeline. Collision/tracking fixtures do not schedule movement, so remain unchanged. Added dispatch-system initialization to two worlds now running the fail-loud VFX producer.
- `006-docs-updates.md`: updated VFX request, VFX-system, projectile-system, and skill-system documentation.

## Distance-Gated Pacing Amendment
- Complete. `trailStepDistance` defaults to `0.5f`, flows through runtime/spawn data, seeds `LastEmitPosition` at spawn/reuse, and gates with squared distance after the `TrailId` fast path.

## Blockers
- None.

## Validation Summary
- `git diff --check`: passed.
- Static data-flow, fixture, and documentation searches: passed.
- Distance-gate source inspection: `TrailId <= 0` fast path precedes `math.lengthsq`; `LastEmitPosition` is assigned only after enqueue and reseeded by `WriteCommon`.
- `dotnet build PlayGround.Sim.csproj --no-restore`: could not reach project code because existing Unity package source `Unity.RenderPipelines.Core.Runtime/RenderGraph/Compiler/PassesData.cs` fails with `CS8168` and `CS8347` under installed .NET SDK 10.0.300.
- `dotnet build PlayGround.Sim.CompileCheck.csproj --no-restore`: unavailable because generated `Temp/obj/PlayGround.Sim.CompileCheck/project.assets.json` is absent.
- Unity tests deferred to user per project rule; no new result XML reviewed.
