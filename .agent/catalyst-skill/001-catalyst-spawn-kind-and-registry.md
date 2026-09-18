# 001 — Catalyst spawn kind, registry, and cast gate

## Goal

Make `Catalyst` a first-class spawn kind end to end in the plumbing layer:
registry map, ref counting, scope buffer, `CombatRoot` API, and the root-cast
mana gate. No archetype and no behavior yet.

## Changes

`Assets/Scripts/System/Spawning/IntervalChildTemplates.cs`
- `IntervalChildKind.Catalyst = 4`.

`Assets/Scripts/System/Catalysts/CatalystSpawnPipeline.cs` (new)
- `CatalystSpawnCommand` — the registry template: faction-independent authored
  body data (shape fields, motion pattern + params, duration, max count,
  trigger `OnHitSpawnRef` + aim, render/sound ids, `DespawnOnOwnerLoss`).
  Per-instance fields (`Owner`, position, faction, ids, seed) stay default in
  the stored template, exactly as the other domains do.
- `CatalystSpawnEvent : IBufferElementData` — `Kind`, `TemplateKey`, `Owner`,
  `Position`, `AimDirection`, `Faction`, `SourceId`, `JitterSeed`.
  `Owner` is the caster proxy entity and has no counterpart in the other spawn
  events; it is the group key half and the anchor target.

`Assets/Scripts/System/Spawning/SpawnTemplateComponents.cs`
- `CatalystSpawnTemplate { NativeHashMap<Hash128, CatalystSpawnCommand> Map }`
  with the same registry contract comment as the other three.
- `SpawnTemplateRegistryState.CatalystCounts`.
- `SpawnTemplateRefEmit.AcquireCatalyst/ReleaseCatalyst(in CatalystTriggerComponent, deltas)`
  — a catalyst carries exactly one key, its trigger `OnHitSpawnRef`.
- `SpawnTemplateValidation.EnsureValidChildKind` — leave `Catalyst` **rejected**.
  A catalyst is never a child of an interval or on-hit link; the throw is the
  guard that keeps it that way.

`Assets/Scripts/System/Core/CombatScopeOwner.cs`
- Owned catalyst map + counts, added to the scope entity, disposed in
  `DisposeMaps`, plus `entityManager.AddBuffer<CatalystSpawnEvent>(ownedScope)`.

`Assets/Scripts/System/Spawning/SpawnTemplateRefCountSystem.cs`
- Route `IntervalChildKind.Catalyst` deltas to `CatalystCounts`; include the map
  in the reclaim sweep.

`Assets/Scripts/System/Core/CombatRoot.cs`
- `Hash128 RegisterSpawnTemplate(in CatalystSpawnCommand template)` — content
  hash, insert-if-absent, owner count, same shape as the projectile overload.
- `UnregisterSpawnTemplate` kind switch gains `Catalyst`.
- `int SpawnRegisteredCatalyst(Hash128 key, Vector2 origin, CombatFaction faction, Entity caster, float manaCost, int castToken)`
  — appends one `ExternalSpawnRequest` with `Kind = Catalyst`.
- Reject an unhandled `CatalystMotionPattern` here, at registration, so no job
  ever branches on an unknown pattern.

`Assets/Scripts/System/Spawning/ExternalSpawnGateSystem.cs`
- `AppendInternalSpawn` gains a `Catalyst` branch appending `CatalystSpawnEvent`
  with `Owner = request.Caster`. Mana is spent by the existing gate, so refresh
  casts pay every time.

## Acceptance criteria

- `CombatRoot.RegisterSpawnTemplate(in CatalystSpawnCommand)` returns a stable
  key for identical content and a different key when any authored field
  changes; re-registering the same content does not grow the map.
- Unregister plus a zero instance count reclaims the entry on the next
  late-simulation sweep; a nonzero instance count retains it.
- A submitted catalyst cast with sufficient mana produces exactly one
  `CatalystSpawnEvent` on the scope with `Owner` set to the caster proxy, and
  clears the request buffer.
- An insufficient-mana catalyst cast produces a `SpawnRejectedEvent` with the
  cast token and no `CatalystSpawnEvent`.
- `SpawnTemplateValidation.EnsureValidChildKind` still throws for `Catalyst`.

## Tests to run (EditMode, `CatalystRegistryTests`)

- `RegisterCatalystTemplate_SameContent_ReturnsSameKey`
- `RegisterCatalystTemplate_ChangedField_ReturnsNewKey`
- `UnregisterCatalystTemplate_NoInstances_Reclaims`
- `CatalystCast_WithMana_AppendsSpawnEventWithOwner`
- `CatalystCast_WithoutMana_EmitsRejectionOnly`
- `EnsureValidChildKind_Catalyst_Throws`

## Dependencies

None. This task unblocks every other one.

## Scope

Small. Mechanical mirroring of three existing registry paths plus one gate
branch. No jobs, no archetype.
