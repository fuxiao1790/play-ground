# 001 — `TargetFaction` = target's own allegiance

## Goal
Make a target proxy carry the target's **own** faction instead of the firing faction
that was allowed to hit it, so one registry can serve both sides and collision can do
`self.Faction != target.Faction`.

## Background
Today the registry stamps every proxy with the root's faction
([CombatTargetProxy.cs:79](../../Assets/Scripts/System/Common/CombatTargetProxy.cs#L79),
[CombatTargetRegistry.cs:19-33](../../Assets/Scripts/System/Common/CombatTargetRegistry.cs#L19-L33)):
a mob registered through the player root becomes `Player`. With one root + one registry
that is ambiguous.

## Changes
1. **`ICombatTarget`** ([ICombatTarget.cs:54](../../Assets/Scripts/System/Common/ICombatTarget.cs#L54)):
   add `CombatFaction CombatFaction => CombatFaction.None;` (default for compatibility).
2. **`PlayerRoot`**: implement `CombatFaction => CombatFaction.Player;`.
3. **`MobRoot`**: implement `CombatFaction => CombatFaction.Mob;`.
4. **`CombatTargetRegistry<T>`**: drop the per-registry `faction` field and the
   `proxyFaction` parameter of `ConfigureProxyBinding`. `proxyBindingReady` becomes
   `manager != default`. `TryCreateProxy` calls `CombatTargetProxy.Create(entityManager,
   target, target.CombatFaction)`.
   - Also drop the `targetFilter`/`CanTarget` gate so **all** registered targets get a
     proxy (faction now filters at collision time). Keep the `IsCombatTargetActive`
     guard.
5. **`CombatTargetProxy.Create`**: keep the `(EntityManager, ICombatTarget, CombatFaction)`
   signature (tests pass faction explicitly); it already writes
   `new TargetFaction { Value = faction }` — now fed the target's own faction.

## Acceptance Criteria
- A mob proxy has `TargetFaction.Value == Mob`; a player proxy has `Player`.
- `ConfigureProxyBinding` no longer takes a faction; one registry creates proxies for
  both players and mobs.
- No production call passes a firing-faction into proxy creation.
- Compiles; no other `ICombatTarget` implementer is forced to change (default `None`).

## Dependencies
None. Precedes 002/003/004 (their skip test relies on own-faction `TargetFaction`).

## Scope
Small. 5 files, mechanical.
