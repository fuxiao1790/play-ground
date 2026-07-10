# 004 — SpawnController

**Scope:** medium. **Depends on:** 002, 003. **Blocks:** 005, 006.

## New file: `Assets/Scripts/Spawn/SpawnController.cs` (MonoBehaviour, `ISpawnSink`)
The single Scene-and-Authoring owner. All refs Inspector-serialized (invariant 4).

### Serialized fields
- `MobSpawnTable table;`
- `SpawnBehaviour behaviour;` (SO)
- `SpawnPlacement placement;` (SO)
- `SpawnPoint[] spawnPoints;`
- `[Min(0)] int cap = 300;`
- `CombatRoot combatRoot;` `CombatVfxRoot vfxRoot;` `Transform target;`
- `[Min(0)] int prewarm = 0;` `int randomSeed;`

### Runtime state
- `MobPool pool;` (built on a child inactive `poolRoot` created in `Awake`)
- `SpawnBehaviourRuntime behaviourRuntime;` (`= behaviour.CreateRuntime()`)
- `System.Random rng;`
- `int activeCount;`
- `readonly HashSet<MobRoot> pendingReclaim = new();`

### `ISpawnSink`
- `ActiveCount => activeCount;` `Cap => cap;`
- `CanSpawn => isActiveAndEnabled && activeCount < cap;`
- `Spawn()`:
  1. `MobRoot prefab = table.ChoosePrefab(rng);` (bail if null)
  2. build `SpawnContext(spawnPoints, target, rng)`; `placement.TryResolve(ctx, out pos)` (bail if false)
  3. `MobRoot mob = pool.Rent(prefab, pos);` (already active + `InitializeForSpawn`ed)
  4. `WireMob(mob); activeCount++;`

### `WireMob(mob)` — reuse existing MobRoot APIs, ORDER MATTERS (invariant 3)
Mirror `GameRoot`'s current wiring ([GameRoot.cs:52-67](../../Assets/Scripts/Game/GameRoot.cs#L52-L67)):
```
mob.Register(combatRoot.TargetRegistry);   // creates proxy — mob must be active+alive first
mob.BindCombatRoot(combatRoot);
if (vfxRoot != null) mob.BindVfxRoot(vfxRoot);
if (target != null) mob.SetTarget(target);
mob.SoftDied += OnMobSoftDied;             // dedupe: unsubscribe in OnMobSoftDied
```

### Death reclaim (deferred, avoids re-entrancy in SoftDied)
- `OnMobSoftDied(MobRoot mob)`: `mob.SoftDied -= OnMobSoftDied; pendingReclaim.Add(mob);`
- `Update()`: `behaviourRuntime.Tick(this, Time.deltaTime);` then `ReclaimDead();`
- `ReclaimDead()`: for each in `pendingReclaim` → `pool.Return(mob); activeCount--;`; clear set.

### Lifecycle — respect the Awake vs OnEnable/Start boundary (coding-standards.md)
`Awake` does **only self-owned construction + fail-fast validation** — no cross-MonoBehaviour
calls, no spawning:
- validate required serialized refs (`table`, `behaviour`, `placement`; `combatRoot` unless it
  will be `Bind`-supplied) and throw a clear setup error if missing; mirror light checks in
  `OnValidate` for editor feedback.
- create the inactive `poolRoot` child, `pool = new MobPool(poolRoot)`,
  `rng = seed==0 ? new() : new(seed)`, `behaviourRuntime = behaviour.CreateRuntime()`.
- **Prewarm the pool here** (`pool.Prewarm(table, Mathf.Max(prewarm, cap))`) so gameplay does
  zero `Instantiate` at steady state (Allocation Rule + "pool creation during setup/preload").
- `GetComponentsInChildren<SpawnPoint>(true)` once if `spawnPoints` empty (setup-only scan).

`Bind(CombatRoot, Transform)` (called by GameRoot in `Start`, task 005) only fills null fields —
a fully Inspector-wired controller ignores it.

Spawning does **not** begin until the controller has a `combatRoot` and is enabled; `Update`
early-returns while `combatRoot == null`. (Cross-object handoff stays out of `Awake`.)

## Acceptance criteria
- With a table, continuous behaviour, fixed placement, points, and combatRoot/target set,
  entering play spawns mobs up to `cap` and holds there.
- A killed mob is returned to the pool and `activeCount` decremented next frame; the freed slot
  is refilled by the stream.
- No `SoftDied` re-entrancy (reclaim is deferred to end of `Update`).
- `Awake` performs no cross-MonoBehaviour work and throws on missing required refs; no
  `Instantiate` occurs during steady-state gameplay (prewarmed).
- No SO asset holds mutable state (accumulator/active-count are on runtime objects).
