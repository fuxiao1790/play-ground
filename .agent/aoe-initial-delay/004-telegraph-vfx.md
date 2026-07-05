# 004 — Telegraph VFX (trigger 4) at spawn

## Goal
Emit the telegraph VFX immediately when a windup AOE is created, using the existing
shared VFX queue + producer chaining. Trigger `0` (spawn) and `1` (hit) are left
as-is.

## Changes
### `Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs` (`AoeExpansionCore.Expand`)
In the per-echo loop, where it already enqueues the trigger-`0` spawn VFX, add a
telegraph enqueue guarded by the delay:
```csharp
if (hasVfxWriter)
{
    vfxPending.Enqueue(new VfxPendingSpawn { TypeId = command.TypeId, Trigger = 0,
        Position = pos, AreaSize = command.AreaSize }); // existing

    if (command.InitialDelaySeconds > 0f)
    {
        vfxPending.Enqueue(new VfxPendingSpawn { TypeId = command.TypeId, Trigger = 4,
            Position = pos, AreaSize = command.AreaSize });
    }
}
```
This runs inside the existing `ImpactAoeExpansionJob` / `LingeringAoeExpansionJob`,
which already accumulate `vfx.ProducerHandle` — no new producer wiring. Applies to
both impact and lingering because both route through `AoeExpansionCore`.

> Note: the registry-minted spawn path also flows through expansion, so all AOE
> kinds (direct-cast, timed-child, on-hit-spawned, stack-detonated) get the
> telegraph as long as their template carries `InitialDelaySeconds`.

### `Assets/Scripts/System/Vfx/VfxEcsComponents.cs`
Update the `Trigger` legend comment: `0=spawn 1=hit 2=expire 3=pulse 4=delay`.

## Acceptance criteria
- Spawning an AOE with `InitialDelaySeconds > 0` and a registered `DelayEffect`
  stages exactly one trigger-`4` `VfxPendingSpawn` per echo at the echo position
  with the AOE's `AreaSize`.
- `InitialDelaySeconds <= 0` stages no trigger-`4` event.
- If no `DelayEffect` is registered for the type, `StageSpawn` drops the trigger-`4`
  event harmlessly (existing dispatcher behavior); no error.

## Dependencies
001 (command field). Independent of 002/003 but meaningless without them.

## Scope
Small.
