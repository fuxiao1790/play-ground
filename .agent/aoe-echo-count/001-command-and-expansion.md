# 001 — Command + Expansion (ECS core)

The heart of the change: make `AoeSpawnExpansionSystem` fan + scatter, mirroring
`ProjectileSpawnExpansionSystem`, and delete the degenerate same-center loop.

## Changes

### `AoeSpawnPipeline.cs` — `AoeSpawnCommand`
- Rename `public int Count;` → `public int EchoCount;`.
- Add `public float ScatterRadius;`.
- Both are behavioral template fields (part of the content hash). Do not add them
  to `AoeSpawnEvent` — the event stays thin, like `ProjectileSpawnEvent`.

### `AoeSpawnExpansionSystem.cs` — `AoeExpansionJob.Execute`
Replace the current body (lines ~156-186) so that, per event:
- Remove the hoisted `CombatCollisionMath.ComputeWorldBounds(command.Position, …)`
  call before the loop.
- `int echoCount = math.max(1, command.EchoCount);`
- Seed once per command: `var rng = new Random(command.JitterSeed != 0 ? command.JitterSeed : 1u);`
- For each `i in [0, echoCount)`:
  - `spawned = command; spawned.AoeId = AoeIdFor(in command, i);`
  - Compute position:
    ```csharp
    float2 pos = command.Position;
    if (command.ScatterRadius > 0f)
    {
        float angle = rng.NextFloat(0f, 2f * math.PI);
        float dist  = command.ScatterRadius * math.sqrt(rng.NextFloat());
        math.sincos(angle, out float s, out float c);
        pos += new float2(c, s) * dist;
    }
    spawned.Position = pos;
    ```
  - Recompute bounds per copy:
    ```csharp
    CombatCollisionMath.ComputeWorldBounds(
        pos, command.Radius, command.HalfExtents, command.RotationRadians, command.ShapeType,
        out float2 boundsMin, out float2 boundsMax);
    spawned.BoundsMin = boundsMin;
    spawned.BoundsMax = boundsMax;
    ```
  - Keep the existing `HasTimedSpawner` stamping (Faction/SourceId from `spawned`).
  - `Stream.Write(spawned);`
  - VFX: enqueue at the **scattered** `pos` (currently uses `evt.Position`):
    ```csharp
    VfxPending.Enqueue(new VfxPendingSpawn {
        TypeId = command.TypeId, Trigger = 0, Position = pos, AreaSize = command.AreaSize });
    ```
- `AoeIdFor` is unchanged and must stay independent of `rng` (id determinism).

## Acceptance criteria
- `AoeSpawnCommand` has `EchoCount` + `ScatterRadius`; `Count` no longer exists.
- Expansion writes `echoCount` commands, each with its own scattered position and
  matching bounds; VFX position matches the AOE position.
- `ScatterRadius == 0` writes all copies at `command.Position` (overlap) — no
  special-case branch, just a zero offset.
- Same `JitterSeed` ⇒ identical scattered layout across runs.
- Job remains a Burst `IJob`; no new component access; VFX producer chaining
  unchanged.

## Dependencies
None (but downstream tasks 002/003 must land together to compile — `EchoCount`
rename breaks call sites).

## Scope
Small, high-care. Two files. The math is ~15 lines; the risk is in preserving
job-safety and determinism.
