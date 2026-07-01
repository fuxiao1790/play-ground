# 005 — Tests

Mirror the projectile spread/fan-out coverage for AOE echo, in the AOE sim tests.

## Changes

### `Assets/Tests/PlayMode/AoeSimulationTests.cs` (extend)
Add cases that drive a registered AOE template with `EchoCount`/`ScatterRadius`
through `AoeSpawnExpansionSystem` → `AoeSpawnApplySystem`:

1. **Fan count** — `EchoCount = N`, `ScatterRadius = 0` ⇒ exactly `N` AOE entities
   spawned, all at `command.Position` (overlap preserved).
2. **Scatter within radius** — `EchoCount = N`, `ScatterRadius = r > 0` ⇒ `N`
   entities, every position within `r` of the target (`distance <= r + epsilon`),
   and not all identical (scatter actually applied).
3. **Determinism** — same `JitterSeed` ⇒ identical set of positions across two
   expansion runs; different seed ⇒ different layout.
4. **Bounds match position** — each spawned entity's `BoundsMin/Max` equals
   `ComputeWorldBounds(itsScatteredPosition, …)`, not the target-center bounds
   (guards the per-copy bounds fix).
5. **Id uniqueness** — the `N` spawned AOEs have distinct `AoeId`s (both
   deterministic-tick and non-deterministic paths).

### Edit-mode (if a compile-time fold test exists)
- If `ModifierFoldEditModeTests` / a compiler test constructs `RuntimeAoeDefinition`
  with `.Count`, update to `.EchoCount` and add a `MultipleAoesSupport` fold
  assertion (echoCount + scatterRadius land on the runtime def).

## Acceptance criteria
- New tests fail against the current same-center implementation and pass after
  001–003.
- Backward-compat test (case 1) proves `scatterRadius == 0` reproduces prior
  positions.

## Dependencies
Depends on 001–004.

## Scope
Medium: new sim assertions; reuse existing AOE test harness/setup.
