# 003 — Instance counting at spawn-apply

**Scope:** medium. **Depends on:** 001.

Every entity that carries a template key gets it written at exactly one place per
domain. Each of those places emits `-1` for the keys the slot was carrying and `+1`
for the keys it now carries.

## Key-carrying fields

| Domain | Component | Field | Registry |
|---|---|---|---|
| Projectile | `ProjectileHitComponent` | `OnHitSpawn.TemplateKey` (+ `.Kind`) | per `Kind` |
| Projectile | `TimedSpawnComponent` | `TemplateKey` (+ `.ChildKind`) | per `ChildKind` |
| Projectile | `CombatHitPayload` | `StackEffect.DetonationKey` | projectile |
| AOE | `AoeHitSpawnComponent` | `OnHitSpawn.TemplateKey` (+ `.Kind`) | per `Kind` |
| AOE | `TimedSpawnComponent` | `TemplateKey` (+ `.ChildKind`) | per `ChildKind` |
| AOE | `CombatHitPayload` | `StackEffect.DetonationKey` | projectile |
| Targeted | `CombatHitPayload` | `StackEffect.DetonationKey` | projectile |

`StackEffectSnapshot.DetonationKey` is always a projectile detonation
(`spawn-template-registry.md:509`), so it always counts against the projectile
registry.

Targeted entities carry no `OnHitSpawnRef` and no `TimedSpawnComponent` —
`TargetedSpawnCommand.OnHitSpawn` (`TargetedSpawnPipeline.cs:58`) is never
materialized. See open question 1 in [index.md](./index.md); count only
`DetonationKey` for targeted.

## Sites

1. `ProjectileSpawnApplyUtility.WriteCommon`
   (`ProjectileDiscreteSpawnApplySystem.cs:280`) — used by **both** projectile lanes
   (`ProjectileDiscreteSpawnApplySystem.cs:244`,
   `ProjectileContinuousSpawnApplySystem.cs:242`). One edit covers both.
2. `AoeSpawnApplyUtility.WriteCommon` (`AoeSpawnApplySystem.cs:510`) — used by both
   AOE lanes (`:219`, `:468`).
3. `TargetedSpawnApplySystem` inline write (`TargetedSpawnApplySystem.cs:178`).

## Shared emit helper

Put this next to `SpawnTemplateRefDelta` so all four sites share it:

```csharp
public static class SpawnTemplateRefEmit
{
    public static void Enqueue(
        IntervalChildKind kind,
        Hash128 key,
        int delta,
        NativeQueue<SpawnTemplateRefDelta>.ParallelWriter deltas)
    {
        if (key.Equals(default(Hash128))) return;
        deltas.Enqueue(new SpawnTemplateRefDelta { Kind = kind, Key = key, Delta = delta });
    }
}
```

The `default(Hash128)` guard is what makes cold-created slots free: a fresh archetype
entity has zeroed components, so its "old" keys are all default and emit nothing.

## Wiring

Each apply system reads `SpawnTemplateRegistryState` from the scope singleton
directly and throws if missing — do not use `TryGetSingleton` or `RequireForUpdate`
(memory `fail-loud-singletons`). Pass `state.Deltas.AsParallelWriter()` into the job
and through to `WriteCommon` as a new parameter.

`ProjectileSpawnJob` and the AOE apply jobs are `IJob`, `PoolTrimJob` (004) is
`ScheduleParallel`. Use `.ParallelWriter` uniformly so all four sites share one helper
signature.

In `WriteCommon`, read the existing component values **before** overwriting:

```csharp
ProjectileHitComponent oldHit = hits[index];
TimedSpawnComponent oldTimed = timedSpawns[index];
StackEffectSnapshot oldStack = hitPayloads[index].StackEffect;

SpawnTemplateRefEmit.Enqueue(oldHit.OnHitSpawn.Kind, oldHit.OnHitSpawn.TemplateKey, -1, deltas);
SpawnTemplateRefEmit.Enqueue(oldTimed.ChildKind, oldTimed.TemplateKey, -1, deltas);
SpawnTemplateRefEmit.Enqueue(IntervalChildKind.Projectile, oldStack.DetonationKey, -1, deltas);

// ... existing writes ...

SpawnTemplateRefEmit.Enqueue(cfg.HitPayload.OnHitSpawn.Kind, cfg.HitPayload.OnHitSpawn.TemplateKey, +1, deltas);
// +1 for the timed key only when hasTimedSpawner; WriteCommon already zeroes
// TimedSpawnComponent otherwise, so mirror that branch exactly.
SpawnTemplateRefEmit.Enqueue(IntervalChildKind.Projectile, stack.DetonationKey, +1, deltas);
```

The `+1` for `TimedSpawnComponent` must follow the same `hasTimedSpawner` branch that
decides `timedSpawns[index] = hasTimedSpawner ? cfg.TimedSpawn : default;`
(`ProjectileDiscreteSpawnApplySystem.cs:346`). If the two diverge, the count drifts.

## Acceptance criteria

- Reusing a pooled slot with an identical command produces a net delta of 0 per key.
- Cold-created slots emit only `+1` deltas.
- A slot reused with a *different* template moves the count from the old key to the new.
- Delta emission order does not matter — the drain in 005 sums them.
- No template map is read or written from any apply system.
- Projectile discrete and continuous lanes stay behaviourally identical.
