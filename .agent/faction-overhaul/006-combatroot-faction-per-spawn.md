# 006 — One `CombatRoot`: faction-per-spawn, no faction field/static

## Goal
Turn `CombatRoot` into faction-agnostic infrastructure: faction is supplied per spawn
call and flows onto the event; the root no longer has a `faction` field, a `ByFaction[]`
static, or faction-from-tag defaults.

## Background
`CombatRoot.faction` is read by: `ByFaction`/`TryGetByFaction`
([CombatRoot.cs:31,95-99,106-135](../../Assets/Scripts/System/Common/CombatRoot.cs#L31)),
event/command builders (stamp `Faction = faction`), proxy binding (now 001),
teardown filters (L788-835), and `ApplyTaggedDefaults` (L950-967). External readers are
only `PlayerSkillDriver` (handled in 007). `TryGetByFaction` has no external callers.

## Changes
1. **Spawn API gains a `CombatFaction faction` argument** and stamps it onto the event:
   - `int Spawn(ProjectileSpawnRequest, CombatFaction faction, int seedContactGateTargetId = 0)`
   - `int SpawnRegisteredProjectile(Hash128, Vector2 pos, Vector2 dir, int count, CombatFaction faction, int seed = 0)`
   - `int Spawn(AoeSpawnRequest, CombatFaction faction)`
   - `int Spawn(ProjectileAoeSpawnRequest, CombatFaction faction)` (forwards faction)
   - `int SpawnRegisteredAoe(Hash128, Vector2 pos, int count, CombatFaction faction)`
   - `ProjectileEventFor` / `AoeEventFor` take `faction` and set `Faction = faction`.
   - Template/command builders (`ProjectileCommandFor`, `AoeCommandFor`, `TimedSpawnFor`)
     no longer read a root faction; set `Faction = CombatFaction.None` (the registry
     normalizes faction anyway, and expansion re-stamps the real faction from the event,
     index I2). In `SpawnTemplateFor` also normalize `HitPayload.StackEffect.Faction` to
     `None` for fully faction-agnostic dedupe (index I3).
2. **Remove faction state:** delete the `faction` field, `ByFaction`, `TryGetByFaction`,
   and the `Faction` property. `BindWorld` calls
   `targetRegistry.ConfigureProxyBinding(entityManager, canTargetFilter?)` per 001
   (no faction arg; drop the `canTargetFilter` proxy gate too).
3. **Teardown:** `OnDestroy`/`DestroyScopedEntities` destroy **all** projectile/AOE
   entities (no faction filter); `ActiveAoeCount` counts all factions. Remove the
   `GetProjectileFaction`/`GetAoeFaction` filter helpers (or keep but stop filtering).
   Render-registry removal already keys by `renderId` (005).
4. **`ApplyTaggedDefaults`:** remove the faction assignment and the
   player/mob tag→faction branch. Keep only the harmless object-layer/target-layer
   defaults if still desired, or drop them (faction no longer derives from tags). Keep a
   single discovery tag for the one root (e.g. reuse `PlayerProjectileRoot` as the
   unified-root tag, or introduce one) so tag-based `Find` in callers resolves it.
5. **`CanTarget`/targetLayers/targetTag (collapse, do not keep inert):** these are the
   old per-root friendly-fire gate at proxy creation. Faction replaces them, so remove
   their targeting role: drop the `canTargetFilter` proxy gate (001) and stop deriving
   targeting layers/tag from the root. `TargetMask` is **not** a collision filter today
   (see index "Friendly-fire filter" analysis); leave its removal + the `MobProjectileAttack`
   `targetMask:` argument and request `TargetMask` field to 008 once the mask tests are
   reconciled, so this task does not churn request payloads mid-flight.

## Acceptance Criteria
- `CombatRoot` has no `faction` field, no `ByFaction`/`TryGetByFaction`, no `Faction`
  property.
- Every public spawn entry requires a `CombatFaction`; the resulting event carries it.
- Destroying the single root tears down all spawned combat entities regardless of faction.
- Registered templates are faction-`None` (including `StackEffect.Faction`).
- Compiles with callers updated in 007.

## Dependencies
005 (render registry keyed by renderId). Precedes 007.

## Scope
Medium–large, mostly within one file.
