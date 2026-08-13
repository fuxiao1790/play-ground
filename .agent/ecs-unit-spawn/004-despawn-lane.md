# 004 — `CombatDespawnOnDeathSystem` and `CombatDespawnBridge`

## Why

The inversion itself: the two systems that take the death decision away from
`MobRoot`. Small enough to land together, and meaningless apart.

## Change A — `CombatDespawnOnDeathSystem` (phase 2, simulation)

New file `Assets/Scripts/System/Targets/CombatDespawnOnDeathSystem.cs`.

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(CombatApplyFinalizeSingleSystem))]
[UpdateBefore(typeof(ResourceRegenSystem))]
public partial class CombatDespawnOnDeathSystem : SystemBase
```

```csharp
foreach ((RefRO<Health> health, Entity proxy)
         in SystemAPI.Query<RefRO<Health>>()
                     .WithAll<DespawnOnDeathTag>()
                     .WithEntityAccess())
{
    if (health.ValueRO.Current > 0f)
    {
        continue;
    }

    pending.Add(proxy);      // reused NativeList, allocated in OnCreate
}
```

Then, after the walk, each pending proxy produces **two** events on the scope
entity:

```csharp
despawnBuffer.Add(new CombatDespawnEvent { Proxy = proxy });
deleteBuffer.Add(new TargetProxyDeleteEvent { Proxy = proxy });
```

The despawn event tells presentation *the actor died*; the delete event tells the
existing `TargetProxyDeleteApplySystem` *destroy this entity*. Reusing the
existing delete path is deliberate — this plan changes who asks for destruction,
not who performs it, so `layer-rules.md` §*Structural Changes* still holds with
no new destroyer.

`pending` is allocated in `OnCreate`, cleared per frame rather than reallocated
(`coding-standards.md` §*Allocation Rule*), and disposed in `OnDestroy`
(§*Native And ECS Handle Ownership*). Collecting first and appending after the
walk keeps buffer references off the query.

### The ordering is load-bearing

**`[UpdateAfter(CombatApplyFinalizeSingleSystem)]`** — damage lands there; this is
what makes the current frame's killing blow visible.

**`[UpdateBefore(ResourceRegenSystem)]`** — regen adds `RegenPerSecond * dt` after
damage in the same frame (`ResourceRegenSystem.cs:13-19`). Without this
attribute, a target with positive regen is lifted off zero before death is ever
evaluated and never dies. Task 001 restores the zero guard, which closes the same
hazard from the other side; keep both, because the guard protects actors that
reach death through their own managed `Resource` (the player) while the ordering
protects this path.

### `WithAll<DespawnOnDeathTag>()` is mandatory

`coding-standards.md` §*Hybrid ECS/Scene Rule* forbids treating proxy components
as a domain marker. Without the tag this system emits a delete for the
**player's** proxy the instant player health reaches zero, destroying the entity
the player's own death handling still needs.

### No dedupe state

Detection is phase 2; destruction is phase 4 of the same frame. No second
simulation pass sees a dead-but-present proxy, so no "already dying" flag is
needed.

Put a comment in the file saying that this depends on
`TargetProxyDeleteApplySystem` staying in `PresentationSystemGroup`. If it ever
moves after the next simulation update, this system starts emitting duplicates
every frame, and the symptom — double `OnCombatDespawned`, double pool return —
surfaces far from the cause. Task 006 puts a test on it.

## Change B — `CombatDespawnBridge` (phase 4, presentation)

New file `Assets/Scripts/System/Presentation/CombatDespawnBridge.cs`.

```csharp
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(CombatApplyBridge))]
[UpdateBefore(typeof(TargetProxyDeleteApplySystem))]
public partial class CombatDespawnBridge : SystemBase
```

```csharp
// for each scope, for each CombatDespawnEvent:
ICombatTarget target = ResolveTarget(EntityManager, evt.Proxy);
if (target == null)
{
    continue;
}

target.CombatTargetProxy = Entity.Null;
target.OnCombatDespawned();

// then clear the buffer
```

`ResolveTarget` is the same shape as `CombatApplyBridge.ResolveTarget`
(`CombatApplyBridge.cs:127-138`): exists → `HasComponent<TargetCompanion>` →
`GetComponentObject`. Both bridges and `CombatActorSpawnBridge` now need it —
lift it into one shared internal helper rather than copying it a third time.

It must **not** reuse `CombatApplyBridge.IsTargetUsable`
(`CombatApplyBridge.cs:140-143`). That helper rejects targets whose
`IsCombatTargetActive` is false, which a dying actor may already be. The
destroyed-Unity-object half of that check is still needed; the active test is not.

### Both orderings are load-bearing

**`[UpdateAfter(CombatApplyBridge)]`** — the killing blow's damage is replayed
through `ICombatTarget.ReceiveCombatTick` (`CombatApplyBridge.cs:103`) in the
same frame. Today's protocol survives the other order only because
`OnCombatDespawned` merely records; ordering it after keeps the guarantee
independent of that, which matters the moment the callback does more.

**`[UpdateBefore(TargetProxyDeleteApplySystem)]`** — the bridge reads
`TargetCompanion` off the proxy. After the delete system runs
(`TargetProxyDeleteApplySystem.cs:36-39`), it is gone.

### Why `CombatTargetProxy` is cleared before the callback

The actor acts on the recorded despawn in its next `Update()` (task 005), then
`SpawnController` returns it to the pool, which calls `SetActive(false)`, which
fires `MobRoot.OnDisable` → `CombatTargetProxy.Delete(this)`. Clearing the field
here makes that a no-op (`CombatTargetProxy.Delete` returns early on
`Entity.Null`, `CombatTargetProxy.cs:139-151`) instead of queueing a delete for
an entity destroyed a frame earlier.

That second delete would be harmless — `TargetProxyDeleteApplySystem` checks
`Exists` — but relying on a guard in another file to absorb a double-emission
this file can simply not cause is the weaker design.

It also stops `MobRoot.Update` from pushing proxy updates during the frame
between despawn and hide (`MobRoot.cs:363-372` no-ops on `Entity.Null`).

### What the bridge does not do

It does **not** hide the actor, stop physics, or raise `SoftDied`. That is the
actor's own work on its next `Update()` — the "handled in next Update()" half of
the protocol.

## Acceptance Criteria

- A tagged proxy at `Health.Current <= 0` produces exactly one
  `CombatDespawnEvent` and one `TargetProxyDeleteEvent`, in the frame the damage
  landed.
- The player's proxy is never matched.
- Regen cannot revive a tagged proxy that reached zero.
- `OnCombatDespawned` is called in the same frame, after `CombatApplyBridge` and
  before the entity is destroyed.
- `target.CombatTargetProxy` is `Entity.Null` on exit.
- An inactive-but-alive target is **not** skipped — this bridge must not use
  `IsTargetUsable`.
- Nothing is hidden, disabled, or returned to a pool by either system.
- No per-frame allocation.

## Dependencies

[003](./003-spawn-result-bridge.md).

## Scope

Small — two systems, each roughly the size of `TargetProxyDeleteApplySystem`.
