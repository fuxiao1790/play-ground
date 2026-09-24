---
name: aoe-pipeline
description: Delete AoeHitSpawnComponent and the on-hit emission from AoeCollisionCore and both AOE collision/apply systems
---

# 005 — AOE Pipeline

## Goal
Delete `AoeHitSpawnComponent` entirely (its only field is the on-hit ref), remove the on-hit
emission call from `AoeCollisionCore.RunCollision`, and drop the archetype/query/handle/lane
wiring that existed only to carry it through `LingeringAoeCollisionSystem`,
`ImpactAoeCollisionSystem`, and both spawn-apply systems in `AoeSpawnApplySystem.cs`.

## Dependencies
Task 003 complete (`SpawnTemplateRefEmit.AcquireAoe`/`ReleaseAoe` already shrunk).

## Files to Modify
- `Assets/Scripts/System/Aoes/AoeEcsComponents.cs`
- `Assets/Scripts/System/Aoes/AoeRuntimeEvents.cs`
- `Assets/Scripts/System/Aoes/AoeSpawnPipeline.cs`
- `Assets/Scripts/System/Aoes/AoeCollisionCore.cs`
- `Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoes/ImpactAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Lifetime/CombatLifetimeSystem.cs` (`AoeLifetimeJob` — same discovery as
  noted in task 004; already fixed alongside the Projectile half since both live in one file)

## Step-by-Step

1. **`AoeEcsComponents.cs`**
   - Delete the `AoeHitSpawnComponent` struct entirely (its only field was `OnHitSpawn`).

2. **`AoeRuntimeEvents.cs`**
   - `AoeSpawnRequest`: delete the `OnHitSpawnRef onHitSpawn = default` constructor parameter and
     the `OnHitSpawn { get; }` property; drop the `OnHitSpawn = onHitSpawn` assignment in the
     constructor body.

3. **`AoeSpawnPipeline.cs`**
   - `AoeSpawnCommand`: delete the `public OnHitSpawnRef OnHitSpawn;` field.

4. **`AoeCollisionCore.cs`**
   - `RunCollision`: delete the `in AoeHitSpawnComponent hitSpawn` parameter, and the
     `EnqueueOnHitSpawn(...)` call inside the target loop (right after `EnqueueHitEvent`).
   - Delete the `EnqueueOnHitSpawn` method entirely.
   - `Deactivate`: delete the `in AoeHitSpawnComponent hitSpawn` parameter; its call to
     `SpawnTemplateRefEmit.ReleaseAoe` drops the corresponding argument per task 003's shrunk
     signature.
   - `RunCollision`'s remaining parameters `projectileEventWriter`, `impactAoeEventWriter`,
     `lingeringAoeEventWriter`, `targetedEventWriter` become unused once `EnqueueOnHitSpawn` is
     gone — delete them from the signature too.

5. **`LingeringAoeCollisionSystem.cs`**
   - Query builder in `OnCreate`: delete `.WithAll<AoeHitSpawnComponent>()`.
   - `OnUpdate`: delete the `projectileLane`/`impactAoeLane`/`lingeringAoeLane`/`targetedLane`
     singleton `RefRW` acquisitions and their `ProducerHandle` combine lines (keep `hitDispatch`
     and `vfx` lanes — `vfx` is used for hit VFX, unrelated to on-hit spawn).
   - `LingeringAoeCollisionJob`: delete the `HitSpawnHandle` field and the `hitSpawns` array/its
     `chunk.GetNativeArray` line; delete the four writer fields that only fed `EnqueueOnHitSpawn`.
   - Update the `AoeCollisionCore.RunCollision(...)` call to match the new (shorter) signature from
     step 4 — drop the `hitSpawns[i]` argument and the four writer arguments.

6. **`ImpactAoeCollisionSystem.cs`**
   - Mirror every change from step 5 (identical shape, no `HitGate`/`TimedSpawn` handling since
     impact AOEs carry neither).

7. **`AoeSpawnApplySystem.cs`**
   - `ImpactAoeSpawnApplySystem.OnCreate`: delete `typeof(AoeHitSpawnComponent)` from
     `_impactArchetype`.
   - `ImpactAoeSpawnJob`: delete `HitSpawnHandle` field and its `hitSpawns` array; drop the
     `hitSpawns` argument from the `AoeSpawnApplyUtility.WriteCommon(...)` call; the
     `SpawnTemplateRefEmit.AcquireAoe(hitSpawns[i], default, hitPayloads[i], Deltas)` call drops
     the `hitSpawns[i]` argument per task 003.
   - `LingeringAoeSpawnApplySystem.OnCreate`: delete `typeof(AoeHitSpawnComponent)` from
     `_lingeringArchetype`.
   - `LingeringAoeSpawnJob`: same `HitSpawnHandle`/`hitSpawns` removal, same `WriteCommon`/
     `AcquireAoe` argument drop.
   - `AoeSpawnApplyUtility.WriteCommon`: delete the `NativeArray<AoeHitSpawnComponent> hitSpawns`
     parameter and the `hitSpawns[index] = HitSpawnFor(cfg);` line.
   - `AoeSpawnApplyUtility.NeedsCollision(in AoeSpawnCommand cmd)`: delete the
     `|| cmd.OnHitSpawn.Enabled` term. Condition becomes
     `cmd.HitPayload.DirectDamageEnabled || cmd.HitPayload.StackEffect.Enabled`.
   - Delete the `HitSpawnFor(in AoeSpawnCommand cmd)` helper (now unused).

## Behavior to Preserve
- Hit VFX dispatch (`vfx`/`CombatAoeVfxDispatchSingleton`) — untouched, unrelated queue.
- `AoeHitGateComponent`/lingering repeat-hit tick gating — untouched.
- Direct damage and stack-effect collision-active gating — unchanged.

## Behavior to Change
- An impact/lingering AOE with no direct damage and no stack effect no longer gains
  collision-active state from a (never-populated) on-hit spawn flag — no-op for live content.

## Acceptance Criteria
- `AoeHitSpawnComponent` no longer exists.
- No reference to `OnHitSpawn`/`OnHitSpawnRef`/`EnqueueOnHitSpawn`/`HitSpawnFor` remains in any
  file listed above.
- Both `ImpactAoeCollisionSystem`/`LingeringAoeCollisionSystem` and both spawn-apply jobs in
  `AoeSpawnApplySystem.cs` compile against `AoeCollisionCore`'s new signature.

## Validation
- `grep -n "AoeHitSpawnComponent\|OnHitSpawn" Assets/Scripts/System/Aoes/*.cs` returns nothing.
- Compile check deferred to the user.
