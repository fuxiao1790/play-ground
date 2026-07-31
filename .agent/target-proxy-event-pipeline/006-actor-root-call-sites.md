# 006 — PlayerRoot / MobRoot Call-Site Updates

## Change

`Assets/Scripts/Player/PlayerRoot.cs` and `Assets/Scripts/Mob/MobRoot.cs`:

- `Register(CombatTargetRegistry<ICombatTarget>)`: `skillDriver?.BindCaster(combatTargetProxy)`
  → `skillDriver?.BindCaster(this)` (matches 005's new signature).
- Add `private bool hasRegisteredProxy;`, set `true` in `Register(...)` right after
  `targetRegistry.Register(this)`.
- `QueueCombatTargetProxyDelete()` guard changes from
  `if (combatTargetProxy != Entity.Null)` to `if (hasRegisteredProxy)`.
- `MobRoot.SoftDie()` (~line 315-343), which is a second path into delete separate from
  the main `Update()`/`QueueCombatTargetProxyDelete` path, needs the same
  `hasRegisteredProxy`-based guard treatment wherever it currently checks
  `combatTargetProxy != Entity.Null` before queuing/performing deletion.

## Why The Guard Must Change

`QueueCombatTargetProxyDelete()` currently only sets the delete flag when
`combatTargetProxy != Entity.Null`. Under deferred creation (002), an actor that dies
before its first create-apply tick (e.g. spawned already at zero HP) has
`combatTargetProxy == Entity.Null` at the moment of death — the guard would silently
drop the delete request entirely, even though `CombatTargetProxy.Create` already
enqueued a `TargetProxyCreateEvent` for it. That create event still resolves later
(`TargetProxyCreateApplySystem` doesn't know the actor died), producing an orphaned
proxy entity with a `TargetCompanion` pointing at a torn-down managed target, which
nothing ever cleans up. Gating on `hasRegisteredProxy` (true from the moment
`Register` ran, regardless of whether the entity has resolved yet) instead of
`combatTargetProxy != Entity.Null` ensures the delete path always fires, and
`CombatTargetProxy.Delete` (002) then correctly either cancels the still-pending create
or enqueues a real `TargetProxyDeleteEvent`, whichever applies.

## Acceptance Criteria

- An actor with a still-pending (unresolved) proxy that dies before its first world
  tick correctly cancels its pending create — no orphaned entity ever appears.
- An actor with a resolved proxy that dies still enqueues a real delete event exactly
  as before.
- `MobRoot.SoftDie()` and the main `Update()`/`LateUpdate()` delete path both go through
  the same `hasRegisteredProxy`-gated logic — no divergent behavior between the two
  paths.

## Dependencies

Depends on 002 (Delete's cancel-pending-create behavior) and 005 (`BindCaster` new
signature).

## Scope

Small-medium. Localized changes in two files, but the guard fix touches two separate
call paths in `MobRoot` (`SoftDie` and the normal death path) that must be checked for
consistency.
