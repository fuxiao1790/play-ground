# 002 — CombatTargetProxy: Enqueue-Only Bodies

## Change

Rewrite the bodies of `Assets/Scripts/System/Targets/CombatTargetProxy.cs`'s mutating
methods to enqueue events instead of calling `EntityManager` mutation APIs directly.
**Public signatures stay the same** except `Create`'s return type
(`Entity` → `bool`, since no entity is available synchronously):

- `Create(EntityManager, ICombatTarget, CombatFaction) : bool` — if
  `target.CombatTargetProxy != Entity.Null` (already has a live proxy), enqueue a
  `Push`-kind `TargetProxyUpdateEvent` instead (reproduces the current "reuse existing"
  branch at lines 79-84). If `pendingTokenByTarget.ContainsKey(target)` (a create is
  already in flight for this target), no-op and return `false` — required re-entrancy
  guard, see Invariants below. Otherwise: mint `token = ++nextCreateToken`, store
  `pendingCreates[token] = target` and `pendingTokenByTarget[target] = token`, resolve
  the scope entity, `GetBuffer<TargetProxyCreateEvent>(scopeEntity).Add(...)` with
  faction/position/shape/health/mana seeded exactly as today's `Create` does (lines
  88-105), return `true`.
- `Delete(ICombatTarget target)` — keep the existing synchronous
  `target.CombatTargetProxy = Entity.Null` reset (line 132, unchanged timing). If
  `pendingTokenByTarget.TryGetValue(target, out token)`, remove it from both
  `pendingTokenByTarget` and `pendingCreates` (cancels an in-flight create — see
  Invariants). If the proxy Entity is non-null, enqueue a `TargetProxyDeleteEvent`
  instead of calling `entityManager.DestroyEntity` directly. The `EntityManager, Entity`
  overload (`Delete(EntityManager, Entity)`) also becomes enqueue-only, for symmetry and
  because tests use it directly.
- `Push(EntityManager, Entity, ICombatTarget)` / `Push(ICombatTarget)` — build
  `TargetPosition`/`TargetCollisionShape` exactly as today (`BuildPosition`/`BuildShape`,
  unchanged helper logic), enqueue a `Push`-kind `TargetProxyUpdateEvent` instead of
  calling `SetComponentData` directly.
- `PushResourceMaxes(...)` (both overloads) — enqueue a `PushResourceMaxes`-kind event
  with the same Max/regen values currently written directly (lines 236-246).
- `SetHealth`/`SetMana` — each enqueues **two** ordered events: a
  `PushResourceMaxes`-kind event, then a `SetHealth`/`SetMana`-kind event carrying
  `CurrentValue` — reproducing today's "push maxes, then clamp Current" call order
  (lines 189-193, 206-210) via FIFO append order into the single shared buffer (see 001).
- `TryReadResources`, `Exists`, `TargetKey(Entity)` — **unchanged**. These are reads or
  pure utility functions, not managed→ECS writes, out of scope per the user's stated
  scope decision.

Add to `CombatTargetProxy`:
```csharp
private static int nextCreateToken;
private static readonly Dictionary<int, ICombatTarget> pendingCreates = new();
private static readonly Dictionary<ICombatTarget, int> pendingTokenByTarget = new();
```
These are consumed by `TargetProxyCreateApplySystem` (task 003) via new internal
accessors, e.g. `internal static bool TryTakePendingCreate(int token, out ICombatTarget target)`
(removes and returns) and `internal static bool IsPendingCreateCancelled(int token)` — or
equivalent; exact accessor shape is an implementation detail for 003 to consume safely
from a single-threaded main-thread system.

## Invariants To Preserve

- **Re-entrancy guard (double-Create).** `CombatTargetRegistry.ConfigureProxyBinding`
  re-runs `TryCreateProxy` over *all* registered targets (`CombatTargetRegistry.cs:35-38`),
  including ones with an already-in-flight, not-yet-applied create. Without the
  `pendingTokenByTarget` guard, this mints a second token/entity for the same target,
  and whichever create-apply resolves second silently orphans the first entity. This
  guard is required, not optional.
- **Delete-cancels-pending-create.** An actor that dies before its first create-apply
  tick (e.g. spawned already fatally damaged) must not leave an orphaned proxy entity
  once its already-enqueued create event resolves later. `Delete` removing the pending
  token entry, combined with `TargetProxyCreateApplySystem` (003) checking cancellation
  before creating the entity, closes this gap.

## Acceptance Criteria

- Every existing call site in `PlayerRoot.cs`/`MobRoot.cs` compiles unchanged except
  where `Create`'s return value is used (none currently store the `Entity` return value
  directly outside `CombatTargetRegistry`, which is handled in 004).
- No `EntityManager.SetComponentData`/`CreateEntity`/`DestroyEntity` calls remain in
  `CombatTargetProxy.cs`'s mutating methods — all mutation happens via
  `GetBuffer<T>(scopeEntity).Add(...)`.
- Calling `Create` twice on the same target before a world tick does not mint two
  tokens.
- Calling `Delete` on a target with a pending (unresolved) create removes the pending
  entry and does not throw.

## Dependencies

Depends on 001 (event struct definitions and scope buffers must exist).

## Scope

Medium-large. Touches most of an existing ~280-line file; logic per method is
mechanical (build the same data, enqueue instead of write) but the token/pending-dictionary
bookkeeping and its two invariants need care and targeted unit coverage.
