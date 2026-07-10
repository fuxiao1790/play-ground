# 002 — Mob spawn table + pool

**Scope:** medium. **Depends on:** 001 (`InitializeForSpawn`). **Blocks:** 004.

## New file: `Assets/Scripts/Spawn/MobSpawnTable.cs` (ScriptableObject)
Weighted prefab selection, ported from the deleted `MobSpawnPool`
(git `801e7423~1:Assets/Scripts/Spawn/MobSpawnPool.cs`).
- `[CreateAssetMenu(menuName = "Play Ground/Spawn/Mob Spawn Table")]`
- `[Serializable] struct Entry { public MobRoot prefab; [Min(1)] public int weight; }`
- `Entry[] entries`; `bool HasEntries`.
- `MobRoot ChoosePrefab(System.Random rng)` — total-weight roll, skip null prefabs
  (copy the old algorithm verbatim).
- `void Configure(MobRoot[] prefabs)` (weight 1 each) for tests.

## New file: `Assets/Scripts/Spawn/MobPool.cs` (plain runtime class)
Per-prefab instance recycling. Owns a hidden **inactive** root transform so instantiated
prefabs' `Awake` does not fire (and does not pre-create a stray proxy) until first real spawn.
- ctor `MobPool(Transform poolRoot)` — `poolRoot` is created inactive by the controller.
- `Dictionary<MobRoot, Stack<MobRoot>> free` keyed by prefab (source identity).
- `MobRoot Rent(MobRoot prefab, Vector2 position)`:
  1. pop a free instance for `prefab`, or `Object.Instantiate(prefab, poolRoot)` and tag it
     with its source prefab (small `PooledMob` component or a dictionary of instance→prefab).
  2. `transform.position = position; SetActive(true); mob.InitializeForSpawn();`
  3. return it (controller then wires combat — see 004).
- `void Return(MobRoot mob)`: `SetActive(false)` (MobRoot.OnDisable cleans proxy+unregister),
  reparent to `poolRoot`, push onto its prefab's free stack.
- `void Prewarm(MobSpawnTable table, int count)`: pre-instantiate `count` inactive instances
  spread across the table's prefabs. **Not optional** — the controller calls this at setup so
  steady-state gameplay does zero `Instantiate` (coding-standards Allocation Rule + "pool
  creation should happen during setup/preload"). Lazy `Rent`-time instantiation remains only as
  an overflow safety net, not the expected path.

## Notes / risks
- **Source-prefab identity**: pooled instances must return to the correct free stack. Store
  the origin prefab per instance (a lightweight `PooledMob { MobRoot Origin }` component added
  on instantiate is simplest and survives disable).
- Instantiate under the **inactive** `poolRoot` so `Awake` is deferred; `Rent` activates then
  initializes — this preserves invariant (3) ordering.

## Acceptance criteria
- `Rent` twice then `Return` one and `Rent` again yields the **same** instance (no new
  Instantiate) for the same prefab.
- Renting a mob leaves it active with a fresh per-life state (health = max, colliders on).
- `ChoosePrefab` distribution respects weights and never returns a null-prefab entry.
