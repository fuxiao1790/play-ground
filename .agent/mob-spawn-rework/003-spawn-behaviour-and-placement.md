# 003 — Spawn behaviour + placement strategies

**Scope:** medium. **Depends on:** none. **Blocks:** 004.

The two pluggable axes ("when/how many" and "where"), plus the point marker and the shared
context/sink contracts. All under `Assets/Scripts/Spawn/`, namespace `PlayGround.Spawn`.

## `SpawnPoint.cs` (MonoBehaviour)
Authored location marker only (no timer/pool — that moved to the behaviour/controller).
- `[SerializeField, Min(0f)] float jitterRadius;`
- `Vector2 SamplePosition(System.Random rng)` — position + random offset within `jitterRadius`.
- `OnDrawGizmos` — draw the point and its jitter radius.

## `SpawnContext` (readonly struct)
Plain data passed to strategies: `IReadOnlyList<SpawnPoint> Points`, `Transform Target`,
`System.Random Rng`. (Add `Bounds PlayArea` later for arena placement.)

## `ISpawnSink` (interface, implemented by SpawnController)
What a behaviour needs from the controller, decoupled from the concrete type:
- `int ActiveCount { get; }`, `int Cap { get; }`
- `bool CanSpawn { get; }` (active && ActiveCount < Cap)
- `void Spawn()` (resolves placement + rents + wires — see 004)

## `SpawnBehaviour.cs` (abstract ScriptableObject) + runtime
Authoring config + a factory for a **per-controller mutable runtime** (so the shared SO asset
holds no mutable state):
- `abstract SpawnBehaviourRuntime CreateRuntime();`
- `abstract class SpawnBehaviourRuntime { public abstract void Tick(ISpawnSink sink, float dt); }`

### `ContinuousStreamBehaviour.cs : SpawnBehaviour`
- `[SerializeField] float spawnsPerSecond = 50f;`
- `[SerializeField, Min(1)] int maxSpawnsPerTick = 8;` (burst clamp)
- `CreateRuntime()` returns a runtime holding a float accumulator:
  - `Tick`: `accum += spawnsPerSecond * dt;` then while `accum >= 1 && spawned < maxSpawnsPerTick
    && sink.CanSpawn` → `sink.Spawn(); accum -= 1; spawned++;`
  - if `!sink.CanSpawn` (at cap), clamp `accum` to a small backlog (e.g. `Min(accum, 1)`) so it
    doesn't burst-catch-up after a big die-off.

## `SpawnPlacement.cs` (abstract ScriptableObject)
- `abstract bool TryResolve(in SpawnContext ctx, out Vector2 position);`

### `FixedPointPlacement.cs : SpawnPlacement`
- Pick a `SpawnPoint` from `ctx.Points` (uniform random via `ctx.Rng`; weighted optional
  later), return `point.SamplePosition(ctx.Rng)`. Return false if no points.
- Future siblings (documented, not built): `RingAroundTargetPlacement`,
  `ArenaEdgePlacement` — need only `ctx.Target` / `ctx.PlayArea`, no controller change.

## Acceptance criteria
- Behaviour and placement are separate SO assets selectable on the controller.
- `ContinuousStreamBehaviour` calls `sink.Spawn()` at ~`spawnsPerSecond` while under cap and
  stops at cap, without runaway backlog.
- `FixedPointPlacement` returns a jittered position at one of the controller's points.
- No mutable state on any SO asset (accumulator lives on the runtime object).
