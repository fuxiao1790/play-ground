# 003 — Spawn events

**Scope:** small. **Depends on:** 001.

Every spawn emits one acquire event. Paired with [004](./004-pool-cleanup-decrement.md),
which emits one release event per despawn.

Spawn and despawn code never reads, writes, or names a reference count. It enqueues a
`SpawnTemplateRefDelta` and knows nothing about what drains the queue.

## The key set lives in one place

`SpawnTemplateRefEmit` (`SpawnTemplateComponents.cs`) is the single definition of which
template keys an entity of each domain carries:

| Domain | Component | Field | Registry |
|---|---|---|---|
| Projectile | `ProjectileHitComponent` | `OnHitSpawn.TemplateKey` (+ `.Kind`) | per `Kind` |
| Projectile | `TimedSpawnComponent` | `TemplateKey` (+ `.ChildKind`) | per `ChildKind` |
| Projectile | `CombatHitPayload` | `StackEffect.DetonationKey` | projectile |
| AOE | `AoeHitSpawnComponent` | `OnHitSpawn.TemplateKey` (+ `.Kind`) | per `Kind` |
| AOE | `TimedSpawnComponent` | `TemplateKey` (+ `.ChildKind`) | per `ChildKind` |
| AOE | `CombatHitPayload` | `StackEffect.DetonationKey` | projectile |
| Targeted | `CombatHitPayload` | `StackEffect.DetonationKey` | projectile |

`AcquireX` and `ReleaseX` are the same private walk with an opposite sign, so a spawn
and its matching despawn cannot disagree about the key set, and a new key-carrying field
means editing one method instead of hunting every spawn and death site.

Both halves read the entity's **components**, never the spawn command. Branches that
zero a component at materialization — a non-timed source clears `TimedSpawnComponent` —
therefore need no mirroring, and the default-key guard in `Enqueue` makes them free.

`StackEffectSnapshot.DetonationKey` is always a projectile detonation
(`spawn-template-registry.md:509`), so it always counts against the projectile registry.
Targeted entities carry no `OnHitSpawnRef` and no `TimedSpawnComponent` —
`TargetedSpawnCommand.OnHitSpawn` (`TargetedSpawnPipeline.cs:58`) is never materialized.
See open question 1 in [index.md](./index.md).

## Sites

1. `ProjectileSpawnApplyUtility.WriteCommon` — used by **both** projectile lanes
   (`ProjectileDiscreteSpawnApplySystem.cs:244`,
   `ProjectileContinuousSpawnApplySystem.cs:242`). One edit covers both.
2. `ImpactAoeSpawnApplySystem` reuse job — impact archetypes have no
   `TimedSpawnComponent`, so it passes `default`.
3. `LingeringAoeSpawnApplySystem` reuse job — emits after `timedSpawns[i]` is written,
   which is why the acquire lives in the job rather than in the shared `WriteCommon`.
4. `TargetedSpawnApplySystem` reuse job.

Each apply system reads `SpawnTemplateRegistryState` directly and throws if missing — no
`TryGetSingleton` guard, no `RequireForUpdate` skip (memory `fail-loud-singletons`) — and
passes `state.Deltas.AsParallelWriter()` into its job.

## Acquire only when the slot goes live

`AoeSpawnApplyUtility.SpawnStateFor(cmd, isLingering: false)` returns
`active: collision` (`AoeSpawnApplySystem.cs:610-616`), so an impact AOE with nothing to
collide against is materialized **already dead**. It never reaches a death site, so
acquiring for it would strand the count forever. Every acquire is therefore gated on
`spawnState.Active`. Projectiles and targeted chains are always materialized active, so
the gate is a no-op there — it is written anyway to keep the rule uniform.

## Acceptance criteria

- No apply system reads, writes, or names a refcount map.
- A materialized entity emits exactly one acquire per non-default key it carries.
- An entity materialized inactive emits nothing.
- No apply site inspects the slot's previous occupant — that entity released its own
  keys when it died.
- Projectile discrete and continuous lanes stay behaviourally identical.
