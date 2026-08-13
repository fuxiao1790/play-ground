# 005 — GameObject side: rent disabled, enable on confirm, act on despawn next frame

## Why

Where the one-frame deferrals become real. Everything before this is plumbing
that changes nothing observable; this task is the behaviour change and the only
place a missed line produces a mob that never enables or never dies.

## Change A — `MobPool.Rent` returns a disabled instance

`Assets/Scripts/Spawn/MobPool.cs:17-32`. Remove two lines:

```csharp
-  mob.gameObject.SetActive(true);
-  mob.InitializeForSpawn();
```

Parenting and positioning stay. Both removed calls move to `MobRoot.BeginLife()`,
invoked by `SpawnController` a frame later.

`InitializeForSpawn` must move with `SetActive`, not run early: it re-enables the
collider, hurtbox, renderer, and `body.simulated` (`MobRoot.cs:252-271`), which
would put a physics body and a visible sprite in the world during the frame the
actor is supposed to be dormant.

## Change B — `SpawnController` tracks in-flight spawns

`Assets/Scripts/Spawn/SpawnController.cs`.

`Spawn()` (`:79-106`) — after renting, request the proxy explicitly and park the
instance:

```csharp
MobRoot mob = pool.Rent(prefab, position);      // now disabled
if (mob == null) { return; }

WireMob(mob);
CombatTargetProxy.Create(combatRoot.EntityManager, mob, mob.CombatFaction);
pendingSpawns.Add(mob);
```

`CombatTargetProxy.Create` is called **directly**, not through
`registry.Register`. `CombatTargetRegistry.TryCreateProxy`
(`CombatTargetRegistry.cs:60-70`) guards on `target.IsCombatTargetActive`, and a
disabled GameObject reports `false` (`MobRoot.cs:109`), so the registry path
silently creates nothing. Do not relax that guard — it correctly stops a dead or
disabled target from resurrecting a proxy for every other caller.

`WireMob` (`:108-124`) keeps its `BindCombatRoot` / `BindVfxRoot` / `SetTarget`
/ `SoftDied` wiring unchanged. It is safe on a disabled instance.

`Update()` (`:49-58`) gains a confirm pass **before** the behaviour tick:

```csharp
ActivateConfirmedSpawns();
behaviourRuntime.Tick(this, Time.deltaTime);
ReclaimDead();
```

```csharp
private void ActivateConfirmedSpawns()
{
    for (int i = pendingSpawns.Count - 1; i >= 0; i--)
    {
        MobRoot mob = pendingSpawns[i];
        if (mob == null)
        {
            pendingSpawns.RemoveAt(i);
            continue;
        }

        if (!mob.HasPendingSpawnConfirmation)
        {
            continue;
        }

        mob.BeginLife();
        pendingSpawns.RemoveAt(i);
        ActiveCount++;
    }
}
```

`ActiveCount` increments **here**, on confirmation, not at request time.

`CanSpawn` (`:29`) must count in-flight requests, or the cap breaks:

```csharp
public bool CanSpawn => isActiveAndEnabled && combatRoot != null
    && ActiveCount + pendingSpawns.Count < cap;
```

Without the `pendingSpawns.Count` term, `ContinuousStreamBehaviour` at 50/s
(`ContinuousStreamBehaviour.cs:8`) submits an entire frame's spawns against a
count that cannot rise until the next frame, and blows straight past the cap.
This is the concrete reason Decision 3 in [index.md](./index.md) puts the pending
list on the spawner rather than the actor.

`pendingSpawns` is a `List<MobRoot>` allocated once (`coding-standards.md`
§*Allocation Rule*), reverse-iterated so removal during the walk is safe.

`OnMobSoftDied` and `ReclaimDead` (`:126-151`) are unchanged. `ActiveCount--`
still happens in `ReclaimDead`.

## Change C — `MobRoot` defers both ends

### Spawn side

```csharp
public bool CombatDespawnOnDeath => true;
public bool HasPendingSpawnConfirmation { get; private set; }

public void OnCombatSpawned(Entity proxy)
{
    // Record only. The spawner enables us on its next Update().
    HasPendingSpawnConfirmation = true;
}

public void BeginLife()
{
    HasPendingSpawnConfirmation = false;
    gameObject.SetActive(true);
    InitializeForSpawn();
}
```

`CombatTargetProxy` is assigned by the bridge before `OnCombatSpawned` is called
(task 003), so nothing here needs to set it.

`Register(registry)` still adds the mob to the registry list for AI targeting
(`TryAcquireEnemyTarget`, `MobRoot.cs:518-547`). A disabled mob in that list is
harmless — candidates are filtered on `IsCombatTargetActive` (`MobRoot.cs:535`),
which is false while disabled.

### Despawn side

```csharp
private bool despawnPending;

public void OnCombatDespawned()
{
    // Record only. Handled on our next Update().
    despawnPending = true;
}
```

`Update()` (`MobRoot.cs:123-145`) handles it first, before anything else:

```csharp
if (despawnPending)
{
    HandleDespawn();
    return;
}
```

```csharp
private void HandleDespawn()
{
    despawnPending = false;
    isAlive = false;
    body.linearVelocity = Vector2.zero;
    wanderVelocity = Vector2.zero;
    body.simulated = false;
    if (bodyCollider != null) { bodyCollider.enabled = false; }
    hurtbox.enabled = false;
    spriteRenderer.enabled = false;
    UnregisterTargets();
    SoftDied?.Invoke(this);
}
```

That is today's `SoftDie()` (`MobRoot.cs:317-345`) minus three things:

- the `health.IsDepleted` re-entry check and `health.MirrorCurrent(0f)` early
  return (`:324-328`) — the simulation already decided; re-deriving it from
  mirrored state is precisely the inversion being undone;
- `softDeathNotified` — the bridge pushes once per death (task 004), so the
  re-entry guard has nothing left to guard;
- `QueueCombatTargetProxyDelete()` — **the line this whole plan exists to
  delete.**

### Delete

- `SoftDie()` — replaced by `HandleDespawn`. Called directly by three tests
  (`MobSpawnControllerPlayModeTests.cs:26,61,91`) and looped in a fourth
  (`:123-126`); task 006 rewrites them to kill through ECS health.
- `HandleHealthDepleted()` (`:347-350`) and the
  `health.Depleted += HandleHealthDepleted` subscription (`:234`).
- `softDeathNotified` (`:65`).
- `QueueCombatTargetProxyDelete()` (`:398-404`), `deleteProxyInLateUpdate`
  (`:62`), `hasRegisteredProxy` (`:61`), and the `LateUpdate` override
  (`:147-153`).
- The `if (!isAlive) { QueueCombatTargetProxyDelete(); return; }` branch in
  `Update` (`:129-133`), replaced by the `despawnPending` branch above.

### Keep

- `OnDisable` → `DeleteCombatTargetProxy()` (`:155-159`). No longer the death
  path, still needed for genuine teardown — scene unload, or a mob disabled
  without dying. After a death it is a no-op because the bridge cleared
  `CombatTargetProxy` (task 004).
- `MirrorResourcesFromProxy` (`:389-396`) and `SyncResourceAuthoring` (`:374-382`).
  The GameObject still mirrors ECS health for display and animation; it just no
  longer decides anything with it. Mirroring for presentation is fine; mirroring
  to derive control is what is being removed.
- `InitializeForSpawn` (`:226-275`) as it is, still called from `Awake` (`:120`)
  for scene-placed mobs and now from `BeginLife` for pooled ones.
- `isAlive` / `IsCombatTargetActive` (`:109`). `IsCombatTargetActive` must return
  `false` after despawn, or `CombatApplyBridge` replays results to a corpse.

## Scene-placed mobs

`GameRoot.cs:58-75` registers authored `MobRoot[]` instances that are already
enabled and already ran `InitializeForSpawn` in `Awake`. They go through
`registry.Register` → `TryCreateProxy`, which passes its
`IsCombatTargetActive` guard because they are active. They receive
`OnCombatSpawned`, set `HasPendingSpawnConfirmation`, and nobody consumes it —
`SpawnController` only tracks what it rented.

That is correct and needs no code, but `BeginLife` must be safe if it is ever
called on an already-live instance, and the flag must not leak into any
behaviour. Worth an explicit note in the file so the next reader does not "fix"
the unconsumed flag.

On death they are despawned by the same lane, hide, and their entity is
destroyed. Nothing reclaims them to a pool, which is right for an authored
instance.

## Acceptance Criteria

- A rented instance is inactive for exactly one frame, then enabled with a
  non-null `CombatTargetProxy`.
- No mob is ever enabled without a confirmed proxy.
- `ActiveCount + pendingSpawns.Count` never exceeds `cap`, including on the first
  frame of a burst.
- A mob damaged to zero health hides on the **following** `Update()`, raises
  `SoftDied` exactly once, and returns to the pool.
- `MobRoot` contains no call to `CombatTargetProxy.Delete` outside `OnDisable`
  and no subscription to `Resource.Depleted`.
- A mob GameObject disabled without dying still releases its proxy.
- Scene-placed mobs still spawn, fight, and die.
- No per-spawn allocation.

## Dependencies

[004](./004-despawn-lane.md).

## Scope

Large — three files, and the only task with observable behaviour change.
