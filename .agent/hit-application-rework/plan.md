# Hit Application Rework Plan

## Goal

Move high-volume damage application off per-hit managed replay. Keep spawn and
VFX paths unchanged for now.

Target result:

```text
collision hits -> ECS damage aggregation -> one compact damage apply per target
spawn events   -> current HitSpawn path
VFX events     -> current VFX path
```

## Current Problem

- Damage replay still scales with raw hit count.
- Profiling showed about 6.7k damage events costing multiple milliseconds on the
  main thread even with callbacks disabled.
- Per-hit damage replay does work that should not cross the ECS/Mono boundary:
  target lookup, damage rolling, hit data construction, batching, and callback
  dispatch.

## Separation Rules

- Damage is aggregate math.
- Spawn is semantic gameplay routing.
- VFX is visual routing.
- Do not route spawn/VFX through damage aggregates.
- Do not preserve per-hit damage callbacks in the stress path.

## Data Shape

Add a compact aggregate buffer on each combat scope:

```csharp
public struct CombatDamageAggregateElement : IBufferElementData
{
    public int TargetId;
    public float DirectDamageAmount;
    public int DirectHitCount;

    public int StackStatusId;
    public int StackCount;
    public int StackThreshold;

    public int StackExplosionAoeTypeId;
    public float StackExplosionDamage;
    public float StackExplosionLifetimeSeconds;
    public float StackExplosionTickIntervalSeconds;
    public AoeSpawnGeometry StackExplosionGeometry;
    public float StackExplosionAreaSize;
}
```

If multiple stack payload definitions can hit the same target/status in one
frame, key stack aggregation by target plus stack payload identity, not just
target id.

## ECS Pipeline

1. Collision systems keep target filtering, contact gates, pierce, spawn events,
   and VFX events as they work now.
2. For damage-enabled contacts, collision writes into sharded worker-local
   accumulators keyed by target index.
3. Each shard accumulates:
   - direct damage amount
   - direct hit count
   - stack count
   - stack explosion payload fields
4. Merge job reduces shards into one aggregate per target/payload key.
5. Merge job writes `CombatDamageAggregateElement` to the scope buffer.
6. Main thread drains aggregate buffer and applies damage/stacks once per target.

## Sharded Accumulators

Use target count and shard count:

```text
slot = shardIndex * targetCount + targetIndex
```

Each worker writes only to its shard, avoiding atomics on hot target slots.

Merge shape:

```text
for each target:
  sum all shard slots for that target
  write aggregate if damage or stacks exist
```

## Crit Handling

To fully aggregate direct damage before main-thread replay, crit rolling should
move into ECS/Burst with deterministic random state.

Short-term options:

- For benchmark path, aggregate base damage only if crits are disabled.
- For real path, roll crit in collision or aggregation using deterministic
  source/hit state and add rolled damage to the accumulator.

Do not use `UnityEngine.Random` in aggregate replay.

## Stack Handling

On each stack hit:

```text
aggregate.StackCount += StacksPerHit
```

Main thread applies one stack delta:

```text
newTotal = existingStacks + StackCount
explosionCount = newTotal / StackThreshold
remainingStacks = newTotal % StackThreshold
```

If explosions trigger, accumulate explosion damage:

```text
explosionDamage = StackExplosionDamage * explosionCount
```

Prefer one spawned AOE with accumulated damage over many duplicate AOEs unless
game design explicitly needs separate explosions.

## Main-Thread Apply

Replace per-hit damage replay with aggregate apply:

```text
for each CombatDamageAggregateElement:
  target = targetsById[TargetId]
  target.ApplyAggregateDamage(...)
```

Expected boundary cost:

```text
O(hit targets + stack payload keys)
```

not:

```text
O(raw damage hits)
```

## API Follow-Up

Current `ICombatTarget.ReceiveHit(s)` is per-hit shaped. Add aggregate-facing
API instead of forcing aggregates back into `CombatHitData`.

Possible shape:

```csharp
void ReceiveDamageAggregate(in CombatDamageAggregateData damage);
```

Keep old per-hit APIs only for tests, compatibility, or low-volume semantic
paths if still needed.

## Validation

- Build runtime.
- Build playmode tests.
- Add focused PlayMode coverage:
  - many hits against one target apply one aggregate direct damage result
  - many hits against many targets apply one aggregate per target
  - stack count aggregates correctly
  - multiple threshold crossings spawn one accumulated explosion payload
  - spawn/VFX paths still fire independently from damage aggregation

## Profiling Checks

Track separate markers:

- damage collision/accumulate
- damage aggregate merge
- aggregate main-thread apply
- spawn replay
- VFX dispatch

Success target:

- raw damage hit count can grow high without main-thread replay growing linearly
- main-thread damage apply scales with target count, not projectile/AOE contact
  count
