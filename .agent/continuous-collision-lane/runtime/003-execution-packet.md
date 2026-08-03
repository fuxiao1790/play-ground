# Task Execution Packet

## Task

003-authoring-flag-and-exclusivity.md

## Goal

Carry authored sweep membership through all skill projectile spawn paths; reject sweep + tracking after modifiers; emit severity-aware validation plus tracking-only tunneling advisory.

## Files Allowed To Modify

- `Assets/Scripts/Skills/SkillDefinition.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/SkillValidationWarning.cs`
- `Assets/Scripts/Skills/SkillLoadoutValidator.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/System/Projectiles/ProjectileSpawnRequest.cs`
- `Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs`
- `Assets/Scripts/System/Core/CombatRoot.cs`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- All allowed files; `Skills/Modifiers/BehaviorContexts.cs`; `Skills/SkillSlotState.cs`; spawn template component/hash code.

## Behavior To Preserve

- Existing non-swept projectile behavior and all direct/interval/on-hit/stack spawn paths.
- Existing dynamic ECS spawn rejection lane.

## Behavior To Change

- `continuousCollision` becomes authored data copied to integer command routing field.
- Final tracking + sweep is an error and driver locally refunds/refuses slot fire.
- Tracking-capable too-fast projectile creates advisory warning at compilation.

## Relevant Global Context

- Sweep has no `SkillStat` or behavior context setter; do not add either.
- Compiler is sole owner of speed/tunneling check; simulation must not receive lint constant or math.
- Command boolean convention is int 0/1, matching `HasTimedSpawner`.
- Error severity must distinguish blocked conflict from advisory warning through existing validation display path.
- Child/template data must preserve sweep through direct cast, interval child, on-hit child, stack detonation.

## Dependencies Confirmed

- Task 001 swept types and constant present.
- `ProjectileBehaviorContext.EnableTracking` allows a modifier to make tracking final at compile time.
- `SkillSlotState.RefundFire()` exists; existing driver rejection loop calls it.
- `ProjectileSpawnCommand`, request, child config, driver templates, and CombatRoot request-to-command translation exist.

## Step-By-Step Instructions

1. Add serialized `continuousCollision` to `ProjectileDefinition`; OnValidate reports directly-authored sweep + tracking error.
2. Add `ContinuousCollision` and `SpawnBlocked` to runtime definition; preserve no support-modifiable sweep path.
3. In `BuildRuntime`, after modifiers produce final tracking config, copy sweep; mark conflict blocked and emit `ContinuousCollisionCannotTrack` Error.
4. Add editor/compiler-only `SmallestExpectedTargetRadius = 0.35f`; use stated two-tick conservative minimum-extent formula only when tracking enabled; emit `TrackingProjectileMayTunnel` Warning.
5. Add validation severity defaulting existing warnings to Warning; update warning construction/validator shape as needed.
6. Driver rejects blocked runtime projectile using local existing refund semantics, before any spawn; propagate `ContinuousCollision` through all direct/child/template construction.
7. Add optional request/child-config constructor/property field and command `int ContinuousCollision`; copy it through CombatRoot and template construction/hashing if command fields are explicitly normalized/hashed.

## Acceptance Criteria

- All four paths preserve membership.
- Conflict is Error, fires no spawn, and refunds.
- No tunneling math in ECS simulation; lint constant unreachable from `PlayGround.Sim`.
- Advisory only when tracking enabled.
- Authoring OnValidate catches direct conflict.
- No sweep stat/context setter.

## Validation Required

- Static path tracing/search verification and diff check. Compile check may remain baseline-blocked by known `TargetProxyUpdateEvent` error; report honestly.

## Hard Boundaries

- No edits outside allowed files unless direct compile import is required.
- Do not implement spawn-lane fan-out or apply/movement/collision systems from later tasks.
