# 003 — Managed submission flips to requests; caster plumbing; remove event buffers

Flip every public spawn entry point from *writing an event* to *appending a request*,
add the caster handle, plumb it from the caster, and delete the now-dead managed event
buffer channel.

## CombatRoot submission methods

Convert all public spawn entry points (per the "all public spawns" decision) so they
append a `CombatSpawnRequest` instead of writing an event buffer. Add an
`Entity caster = default` parameter (defaulted so ownerless/test callers are
unchanged):

- `Spawn(ProjectileSpawnRequest request, CombatFaction faction, int seedContactGateTargetId = 0, Entity caster = default)`
- `Spawn(AoeSpawnRequest request, CombatFaction faction, Entity caster = default)`
- `Spawn(ProjectileAoeSpawnRequest request, CombatFaction faction, Entity caster = default)`
  (delegates to the AOE overload, forwarding `caster`)
- `SpawnRegisteredProjectile(..., Entity caster = default)`
- `SpawnRegisteredAoe(..., Entity caster = default)`

Each method keeps its **submit-time** work:
- `EnsureRuntimeReady`, validation.
- Template registration for the request-carrying overloads (`RegisterSpawnTemplate`) —
  this stays managed and pre-tick (invariant 1).
- Managed id/jitter/stat allocation (`nextProjectileId`, `nextAoeId`, `spawnedAoes`) —
  unchanged (invariant 7).

Then, instead of `GetBuffer<...Event>(scopeEntity).Add(event)`, do
`GetBuffer<CombatSpawnRequest>(scopeEntity).Add(new CombatSpawnRequest { ... Caster = caster })`.

Delete the now-unused private event builders that only fed the buffer
(`ProjectileEventFor`, `AppendAoeEvent`, `AppendAoeSpawnEvent`) — their body moved into
`SpawnIntakeSystem` in task 002. Keep the command/template builders
(`ProjectileCommandFor`, `AoeCommandFor`, `SpawnTemplateFor`, etc.) which feed
registration.

Return value: keep returning the pre-allocated base id (no caller captures it, but it
stays non-breaking).

## Caster plumbing

- `SkillSpawnTranslator.Spawn(...)` gains an `Entity caster` parameter and forwards it
  to `SpawnRegisteredProjectile` / `SpawnRegisteredAoe`.
- `SkillDriver` resolves its owner once (e.g. `GetComponent<ICombatTarget>()` in
  `Awake`, cached) and passes `owner?.CombatTargetProxy ?? Entity.Null` into
  `SkillSpawnTranslator.Spawn`. This is the "same as health" proxy binding.
- Ownerless callers (tests calling `CombatRoot.Spawn(...)` directly, one-off spawns)
  rely on the `caster = default` (`Entity.Null`) default.

## Remove the dead managed event channel

Once nothing writes the scope event buffers:

- `CombatScopeOwner.Acquire`: remove
  `AddBuffer<ProjectileSpawnEvent>` / `AddBuffer<ImpactAoeSpawnEvent>` /
  `AddBuffer<LingeringAoeSpawnEvent>` from the scope.
- `ProjectileSpawnExpansionSystem.OnUpdate`: remove the scope-buffer branch (the
  `_scopeQuery` `GetBuffer<ProjectileSpawnEvent>` count/copy/clear). Drain only
  `EventQueue`. Remove the now-unused `_scopeQuery` and its `ProjectileSpawnEvent`
  requirement.
- `AoeSpawnExpansionSystem.OnUpdate`: same for both the impact and lingering lanes.
- Update any other reader of the scope event buffers. Known: `CombatRoot.ActiveAoeCount`
  counts `GetBuffer<ImpactAoeSpawnEvent>` + `GetBuffer<LingeringAoeSpawnEvent>` on the
  scope for `Counters`; since those buffers are gone, drop those terms (pending
  intake-side events now live in the lane queues, not the scope). Verify no other
  `GetBuffer<...SpawnEvent>(scope)` callers remain via a grep before deleting the
  buffers.

## Acceptance criteria

- Grep confirms no remaining `GetBuffer<ProjectileSpawnEvent>` /
  `GetBuffer<ImpactAoeSpawnEvent>` / `GetBuffer<LingeringAoeSpawnEvent>` on a scope
  entity, and no `AddBuffer<...SpawnEvent>` in `CombatScopeOwner`.
- Runtime cast (SkillDriver) and public `Spawn(...)` overloads produce entities via the
  request → intake → queue → expansion path.
- Player casts carry a non-null caster proxy in the request; ownerless spawns carry
  `Entity.Null`.
- Determinism unchanged (same `SourceId`/`JitterSeed` reach expansion).

## Dependencies

001 + 002 (request buffer must exist and intake must consume it before managed stops
writing events).

## Scope / complexity

Medium-large. Touches `CombatRoot`, `SkillSpawnTranslator`, `SkillDriver`,
`CombatScopeOwner`, both expansion systems.
