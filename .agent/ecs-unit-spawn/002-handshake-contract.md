# 002 — Handshake contract, and taking managed state out of the create system

## Why

All the new data in one piece, plus the change that makes the deferred spawn
protocol possible: `TargetProxyCreateApplySystem` stops resolving managed targets
and emits a result for the push phase instead.

## Change

### `ICombatTarget` — three default members

`Assets/Scripts/System/Targets/ICombatTarget.cs`, beside the existing
`ReceiveSpawnRejected` (`ICombatTarget.cs:123-125`), which already establishes
the default-empty-member pattern:

```csharp
// True when this target's actor should be despawned by the simulation once its
// health reaches zero. False for actors that manage their own death (the player).
bool CombatDespawnOnDeath => false;

// Pushed by CombatActorSpawnBridge during presentation, on the frame the proxy
// entity was created. The actor is expected to record it and go live on its next
// Update(), not to go live here.
void OnCombatSpawned(Entity proxy)
{
}

// Pushed by CombatDespawnBridge during presentation, on the frame the simulation
// decided this actor died and before its proxy entity is destroyed. The actor is
// expected to record it and act on its next Update().
void OnCombatDespawned()
{
}
```

The "record, do not act" contract is stated in the interface itself, because it
is the whole protocol and an implementor that acts immediately breaks it without
failing to compile.

Defaults mean `PlayerRoot`, `SkillDriver`, and every test target compile and
behave unchanged. Only `MobRoot` overrides (task 005).

### `DespawnOnDeathTag`

`Assets/Scripts/System/Targets/CombatTargetProxy.cs`, beside `TargetProxyTag`:

```csharp
// ECS Lifecycle: opt-in death-handling tag; added at proxy creation when the
// registering target reports CombatDespawnOnDeath, never added or removed
// afterwards, destroyed with the proxy. Absent from the player proxy.
public struct DespawnOnDeathTag : IComponentData
{
}
```

### Two new events

`Assets/Scripts/System/Targets/TargetProxyEvents.cs`:

```csharp
// ECS Lifecycle: transient proxy-creation result; appended by
// TargetProxyCreateApplySystem during simulation, consumed and discarded by
// CombatActorSpawnBridge during presentation of the same frame.
public struct TargetProxySpawnResult : IBufferElementData
{
    public int Token;
    public Entity Proxy;
}

// ECS Lifecycle: transient despawn notification; appended by
// CombatDespawnOnDeathSystem during simulation, consumed and discarded by
// CombatDespawnBridge during presentation of the same frame.
public struct CombatDespawnEvent : IBufferElementData
{
    public Entity Proxy;
}
```

See Decision 4 in [index.md](./index.md) for why `CombatDespawnEvent` is separate
from `TargetProxyDeleteEvent` rather than folded into it.

`TargetProxyCreateEvent` (`TargetProxyEvents.cs:8-20`) gains one field:

```csharp
public byte DespawnOnDeath;
```

`byte` rather than `bool`, matching the codebase's existing flag style
(`TargetedSpawnEvent.HasAcquiredTarget`).

### `CombatTargetProxy.Create`

Fills the new field (`CombatTargetProxy.cs:103-115`):

```csharp
DespawnOnDeath = target.CombatDespawnOnDeath ? (byte)1 : (byte)0,
```

Everything else about `Create` — the token, `pendingCreates`,
`pendingTokenByTarget`, `TryTakePendingCreate`, `IsPendingCreateCancelled` —
stays exactly as it is. The handshake is unchanged; only the phase that resolves
it moves.

### `TargetProxyCreateApplySystem` — no more managed state

Current body (`TargetProxyCreateApplySystem.cs:34-60`) does three managed things
inside `SimulationSystemGroup`. `Docs/architecture/phase-order.md` §*Phase
Ownership* says simulation "may not read managed target companions or live Unity
objects", so this is an existing violation the protocol requires fixing anyway.

Replace with:

```csharp
TargetProxyCreateEvent createEvent = events[eventIndex];

Entity proxy = EntityManager.CreateEntity(CombatTargetProxy.Archetype(EntityManager));
EntityManager.SetComponentData(proxy, new TargetFaction { Value = createEvent.Faction });
EntityManager.SetComponentData(proxy, createEvent.Position);
EntityManager.SetComponentData(proxy, createEvent.Shape);
EntityManager.SetComponentData(proxy, new Health { ... });
EntityManager.SetComponentData(proxy, new Mana { ... });
if (createEvent.DespawnOnDeath != 0)
{
    EntityManager.AddComponent<DespawnOnDeathTag>(proxy);
}

results.Add(new TargetProxySpawnResult { Token = createEvent.Token, Proxy = proxy });
```

Removed from this system: the `TryTakePendingCreate` call and its `continue`, the
`AddComponentObject(proxy, new TargetCompanion { Target = target })`, and
`target.CombatTargetProxy = proxy`. All three move to `CombatActorSpawnBridge`
(task 003).

**Cancellation moves with them.** Today an event whose pending create was
cancelled is skipped and no entity is made
(`TargetProxyCreateApplySystem.cs:37-40`). After the split the entity is created
first and the bridge discovers the cancellation one phase later, so the bridge
must destroy the orphan — see task 003. This is a real behaviour change, not a
refactor: a target that unregisters between `Update()` and the push now costs one
create/destroy pair instead of zero. Acceptable at proxy-creation rates, and the
alternative (checking managed state in simulation) is the thing being removed.

The `AddComponent<DespawnOnDeathTag>` after `CreateEntity` is a second archetype
hop. Acceptable: proxy creation is already structural on a low-count path, and a
second cached archetype in `CombatTargetProxy.Archetype` is more code than the
hop is worth. If mob spawn shows up in the Entities Structural Changes profiler
module, that is the local fix.

Collect results into a reused `NativeList<TargetProxySpawnResult>` and append
after the create loop rather than fetching the buffer per event —
`EntityManager.CreateEntity` invalidates any held `DynamicBuffer` reference
(`ecs-notes.md` §*Sync Points*), and the existing code already re-fetches for
exactly this reason (`TargetProxyCreateApplySystem.cs:62`).

### Scope buffers

`CombatScopeOwner.Acquire` (`CombatScopeOwner.cs:67-89`), two lines beside the
three existing proxy event buffers:

```csharp
entityManager.AddBuffer<TargetProxySpawnResult>(ownedScope);
entityManager.AddBuffer<CombatDespawnEvent>(ownedScope);
```

No `DisposeMaps` change — buffers die with the scope entity.

## The registry guard (blocker if missed)

`CombatTargetRegistry.TryCreateProxy` (`CombatTargetRegistry.cs:60-70`) refuses
to create a proxy unless `target.IsCombatTargetActive`, and `MobRoot`'s
implementation tests `isActiveAndEnabled` (`MobRoot.cs:109`). Under the new
protocol the actor is rented **disabled**, so registry-driven creation silently
never fires and the mob never spawns.

`CombatTargetProxy.Create` itself has no such guard (`CombatTargetProxy.cs:75-117`).
So the spawn request must call it directly rather than relying on
`Register`'s side effect. Task 005 does that; this task only needs to leave
`Create` callable that way, which it already is.

Do **not** relax the guard in `TryCreateProxy`. It exists so that re-registering
a dead or disabled target does not resurrect a proxy, and that is still wanted
for every other caller.

## Acceptance Criteria

- `PlayerRoot` and every test `ICombatTarget` compile untouched and get no tag.
- A `MobRoot`-registered proxy carries `DespawnOnDeathTag`; the player's does not.
- `TargetProxyCreateApplySystem` contains no reference to `ICombatTarget`,
  `TargetCompanion`, or `CombatTargetProxy.TryTakePendingCreate`.
- One `TargetProxySpawnResult` per created entity, carrying the event's token.
- Both buffers exist on the scope entity after `CombatScopeOwner.Acquire`, and a
  second `Acquire` does not duplicate them.
- No `DynamicBuffer` reference is held across an `EntityManager` structural call.
- Every new declaration carries an `ECS Lifecycle:` comment.
- Nothing observable changes yet — with no bridge, `CombatTargetProxy` is never
  assigned, so land 002 and 003 together.

## Dependencies

None. Land with [003](./003-spawn-result-bridge.md).

## Scope

Medium — small in volume, but it moves the managed handshake across a phase
boundary and changes cancellation behaviour.
