# 003 — ECS Component And Archetype Wiring

## Scope

Add the per-instance trail VFX component and thread it through both
projectile lanes' spawn archetypes and the shared `WriteCommon` materialization
path.

## Changes

### `Assets/Scripts/System/Projectiles/ProjectileEcsComponents.cs`

Add, alongside the other lifecycle-commented components in this file:

```csharp
// ECS Lifecycle: base projectile component; added by spawn materialization; reset on reuse
// (LastEmitPosition reseeds to the new spawn position so a reused slot never draws a segment
// back to its previous occupant's last trail point). Visual-only trail request data — TrailId
// 0 means no trail is authored and ProjectileMovementSystem's emit call no-ops before ever
// touching LastEmitPosition. StepDistance is the authored minimum travel distance between
// emitted segments; LastEmitPosition is the one piece of runtime state the emitter mutates
// every frame the trail is active.
public struct ProjectileTrailVfxComponent : IComponentData
{
    public int TrailId;
    public float Width;
    public float StepDistance;
    public float2 LastEmitPosition;
}
```

Requires `using Unity.Mathematics;` in this file for `float2` (check whether
it is already imported — `ProjectileTrackingComponent` in the same file already
uses `float2`, so it likely is).

### `Assets/Scripts/System/Projectiles/ProjectileDiscreteSpawnApplySystem.cs`

Add `typeof(ProjectileTrailVfxComponent)` to the `_archetype` built in
`OnCreate` (order doesn't matter for `EntityArchetype`, but place it near
`CombatRenderKindId` since both are resolved-id components).

Add to `ProjectileSpawnJob`:

```csharp
public ComponentTypeHandle<ProjectileTrailVfxComponent> TrailVfxHandle;
```

Wire it in `OnUpdate`'s job construction:

```csharp
TrailVfxHandle = GetComponentTypeHandle<ProjectileTrailVfxComponent>(false),
```

In `Execute`, fetch the array and pass it to `WriteCommon`:

```csharp
NativeArray<ProjectileTrailVfxComponent> trailVfx =
    chunk.GetNativeArray(ref TrailVfxHandle);
```

and add `trailVfx` to the `ProjectileSpawnApplyUtility.WriteCommon(...)` call
argument list (see below for the new parameter).

### `Assets/Scripts/System/Projectiles/ProjectileContinuousSpawnApplySystem.cs`

Identical treatment: add `typeof(ProjectileTrailVfxComponent)` to its
`_archetype`, add `TrailVfxHandle` to `ProjectileContinuousSpawnJob`, wire it in
`OnUpdate`, fetch the array in `Execute`, pass it to `WriteCommon`.

### `ProjectileSpawnApplyUtility.WriteCommon` (in `ProjectileDiscreteSpawnApplySystem.cs`)

Add one parameter:

```csharp
NativeArray<ProjectileTrailVfxComponent> trailVfx,
```

(placed in the parameter list near `batchIds`, both are per-command resolved-id
writes) and inside the method body:

```csharp
trailVfx[index] = new ProjectileTrailVfxComponent
{
    TrailId = cfg.TrailVfxId,
    Width = cfg.TrailWidth,
    StepDistance = cfg.TrailStepDistance,
    // Seed to the spawn position, not zero/default. A reused slot's previous occupant may have
    // left LastEmitPosition anywhere on the map; seeding it to cfg.Position means the first
    // distance check measures travel since spawn, not a bogus jump from the last occupant's
    // final trail point.
    LastEmitPosition = cfg.Position
};
```

Both call sites (discrete and continuous `Execute`) must pass their local
`trailVfx` array at the same `trailVfx` parameter position.

## Acceptance Criteria

- Both projectile archetypes (`ProjectileDiscreteSpawnApplySystem`,
  `ProjectileContinuousSpawnApplySystem`) include `ProjectileTrailVfxComponent`.
- Spawning a projectile from a command with `TrailVfxId != 0` results in that
  entity's `ProjectileTrailVfxComponent.TrailId`/`Width`/`StepDistance` matching
  `cfg.TrailVfxId`/`cfg.TrailWidth`/`cfg.TrailStepDistance`, and
  `LastEmitPosition` equal to `cfg.Position`.
- Spawning from a command with default `TrailVfxId == 0` (e.g. any command
  built via `CombatRoot.ProjectileCommandFor`, or an untrailed skill) leaves the
  entity with `TrailId == 0`.
- Reused (pooled) entities get their `ProjectileTrailVfxComponent` fully
  overwritten on every spawn, same as every other component `WriteCommon`
  writes — no stale trail id survives across reuse.

## Dependencies

Depends on 002 (`ProjectileSpawnCommand.TrailVfxId`/`TrailWidth` must exist).
Feeds 004 (`ProjectileMovementSystem` reads `ProjectileTrailVfxComponent` off
the entity) and 005 (hand-built test archetypes must add this component too).
