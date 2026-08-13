# 003 — `CombatActorSpawnBridge`

## Why

Phase 4 of the spawn protocol: the entity exists, and this is where the actor
finds out. Picks up the managed handshake that task 002 removed from simulation.

## Change

New file `Assets/Scripts/System/Presentation/CombatActorSpawnBridge.cs`,
namespace `PlayGround.System.Combat.Presentation` (same as `SpawnRejectionBridge`).

```csharp
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateBefore(typeof(TargetProxyDeleteApplySystem))]
public partial class CombatActorSpawnBridge : SystemBase
```

```csharp
protected override void OnUpdate()
{
    CompleteDependency();

    // for each scope, for each TargetProxySpawnResult:
    if (!CombatTargetProxy.TryTakePendingCreate(result.Token, out ICombatTarget target))
    {
        // Cancelled between Update() and now: nobody is waiting for this entity.
        EntityManager.DestroyEntity(result.Proxy);
        continue;
    }

    EntityManager.AddComponentObject(result.Proxy, new TargetCompanion { Target = target });
    target.CombatTargetProxy = result.Proxy;
    target.OnCombatSpawned(result.Proxy);

    // then clear the buffer
}
```

Three lines lifted verbatim from `TargetProxyCreateApplySystem.cs:37-59`, now
running one phase later where managed access is legal
(`coding-standards.md` §*Hybrid ECS/Scene Rule*).

`SpawnRejectionBridge` (`Assets/Scripts/System/Presentation/SpawnRejectionBridge.cs`)
is the shape to copy: presentation system, drain a result lane, resolve the
managed target, call a default `ICombatTarget` member, clear.

### Orphan destruction

The cancellation path is new and is the one behaviour change in this task. Task
002 explains why it moved: simulation creates the entity before anyone can know
the create was cancelled, so the bridge destroys it.

`TryTakePendingCreate` (`CombatTargetProxy.cs:275-289`) already removes the token
on success and `CombatTargetProxy.Delete` already clears pending tokens on cancel
(`CombatTargetProxy.cs:140-144`), so a cancelled token simply misses and the
orphan is destroyed here. No new bookkeeping.

Destroying directly rather than queueing a `TargetProxyDeleteEvent` is correct:
nothing else has seen this entity — it was created this frame, has no
`TargetCompanion`, and no bridge has resolved it. Queueing would defer a
structural change by one phase for no observer's benefit.

### Ordering

`[UpdateBefore(TargetProxyDeleteApplySystem)]` because both this bridge and the
delete system do structural work on proxies, and a proxy created this frame must
not be visited by the delete pass on a stale event.

No ordering against `CombatApplyBridge` is required — a proxy created this frame
has no combat results yet. Leave it unconstrained rather than adding an
attribute that implies a dependency that does not exist.

### What the bridge does not do

It does **not** enable the GameObject. `OnCombatSpawned` records; the spawner
enables on the next `Update()` (task 005, Decision 3 in [index.md](./index.md)).
Enabling here would put the actor live in phase 4, skipping the frame boundary
the protocol exists to create.

## Acceptance Criteria

- A rented, disabled actor has `CombatTargetProxy` assigned and
  `OnCombatSpawned` called in the same frame its create event was submitted.
- `TargetCompanion` is attached with a non-null `Target`.
- A create cancelled between submission and push destroys the orphan entity and
  leaves no `TargetCompanion`.
- The result buffer is cleared every frame.
- Nothing is enabled or made visible by this system.
- No `DynamicBuffer` reference is held across an `EntityManager` structural call.

## Dependencies

[002](./002-handshake-contract.md). Land together — 002 alone leaves
`CombatTargetProxy` never assigned.

## Scope

Small — roughly the size of `SpawnRejectionBridge`.
