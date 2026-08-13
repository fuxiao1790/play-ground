# 001 — Restore the depleted-health regen guard

## Why

**Pre-existing bug, not caused by this plan.** Commit `5cddbdee` *"remove
branching"* deleted the zero guard from `ResourceRegenSystem`:

```csharp
-  if (health.ValueRO.Current <= 0f || health.ValueRO.RegenPerSecond <= 0f)
-  {
-      continue;
-  }
```

Two consequences on master today:

1. `ResourceRegenDoesNotReviveDepletedHealth`
   (`Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs:150`) creates a
   target with `healthCurrent: 0f, healthRegenPerSecond: 4f`, ticks one second,
   and asserts `Health.Current == 0`. With the guard gone it reads `4`. The test
   cannot pass.
2. Any actor with `HealthRegenPerSecond > 0` effectively cannot die through the
   mirrored-health path. `ResourceRegenSystem` is
   `[UpdateAfter(CombatApplyFinalizeSingleSystem)]`, so within one frame the
   damage lands and regen immediately lifts the value off zero.
   `MobRoot.MirrorResourcesFromProxy` (`MobRoot.cs:391`) then always reads a
   positive value, and `Resource.Depleted` never fires. Latent only for as long
   as authored regen stays at zero.

It lands first because it is one line, a test already asserts it, and
[index.md](./index.md) Decision 4 reasons about regen ordering.

## Change

`Assets/Scripts/System/Targets/ResourceRegenSystem.cs`. Restore the semantics
**without** restoring the branch, respecting the original commit's intent:

```csharp
foreach (RefRW<Health> health in SystemAPI.Query<RefRW<Health>>())
{
    float alive = math.select(0f, 1f, health.ValueRO.Current > 0f);
    health.ValueRW.Current = math.min(
        health.ValueRO.Max,
        health.ValueRO.Current + health.ValueRO.RegenPerSecond * deltaTime * alive);
}
```

`math.select` lowers to a branchless blend, so the "remove branching" change is
preserved while the semantics come back.

The `RegenPerSecond <= 0f` half of the old guard is **not** restored — it was a
work-skip, not a correctness rule, and multiplying by zero is already correct.
Only the zero-health half changes behaviour.

The mana loop is unchanged. Mana at zero is an ordinary resting state, not death,
and `math.clamp` already handles it correctly.

## The deltaTime guard

The same commit also removed:

```csharp
-  if (deltaTime <= 0f) { return; }
```

Leave it removed. With `deltaTime == 0` every term multiplies to zero and the
result is a no-op write, so it is a work-skip rather than a correctness rule.
Worth knowing it was deliberate rather than missed — the pause work
(`.agent/pause-feature/`) drives zero-dt frames, so if a future profile shows
this loop mattering while paused, that early-out is the fix and it is safe.

## Acceptance Criteria

- `ResourceRegenDoesNotReviveDepletedHealth` passes.
- `ResourceRegenRunsAfterCombatDamage`
  (`ProjectileCollisionSimulationTests.cs:161`) still passes — regen from a
  positive value is unaffected.
- The mana regen tests still pass.
- No branch is reintroduced in the health loop.

## Dependencies

None. Independent of everything else in this plan; land it on its own.

## Scope

Tiny — one expression.
