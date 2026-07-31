# 004 — Registry & CombatRoot Dead-Code Cleanup

## Change

`Assets/Scripts/System/Targets/CombatTargetRegistry.cs`:
- Delete `targetsById`, `proxyKeyByTarget` fields, the `TargetsById` property, and
  `RemoveTargetLookup(...)`.
- `TryCreateProxy` simplifies to calling `CombatTargetProxy.Create(entityManager, target, target.CombatFaction)`
  and dropping the `TargetKey`-derived bookkeeping that followed it (no longer possible
  anyway, since `Create` no longer returns a usable `Entity` synchronously).
- `Unregister` no longer calls `RemoveTargetLookup(target)`.

`Assets/Scripts/System/Core/CombatRoot.cs`:
- Delete `internal IReadOnlyDictionary<int, ICombatTarget> TargetsById => targetRegistry.TargetsById;`
  (line 94).

## Why This Is Safe (verified, not assumed)

Repo-wide grep for `TargetsById`/`proxyKeyByTarget`/`RemoveTargetLookup` confirms the
only references are: the declaration sites themselves, and `CombatRoot.cs`'s
now-unread passthrough. Nothing else in `Assets/Scripts/` or `Assets/Tests/` reads
`CombatRoot.TargetsById` or `CombatTargetRegistry<T>.TargetsById`.
`CombatApplyBridge` — the system that resolves ECS hit results back to managed
targets — does so via the live `Entity` field `result.TargetProxy` directly
(`CombatApplyBridge.cs:91`), never through this dictionary. This bookkeeping was already
dead before this refactor; the refactor just makes it impossible to keep (since it was
built from a synchronously-available `Entity` that no longer exists at `Create()` time),
which is what surfaced it.

Note: `CombatTargetSet.cs` has its own, unrelated `targetsById` field (keyed by
`ICombatTarget.TargetId`, not `Entity`) — that class is untouched by this task.
`CombatTargetProxy.TargetKey(Entity)` itself also stays — it remains a used utility
(see 002/003 — no removal there), just no longer wired into
`CombatTargetRegistry`'s dead bookkeeping.

## Acceptance Criteria

- Solution compiles with `CombatTargetRegistry.TargetsById` and
  `CombatRoot.TargetsById` removed.
- `CombatTargetRegistry<T>.Register`/`Unregister`/`ConfigureProxyBinding` behavior is
  otherwise unchanged (targets list, proxy creation triggering).

## Dependencies

Depends on 002 (`Create`'s `Entity → bool` return-type change is what makes the old
bookkeeping impossible to keep, not just unnecessary).

## Scope

Small. Deletions only, no new logic.
