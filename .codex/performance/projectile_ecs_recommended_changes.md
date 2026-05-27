# Projectile ECS Recommended Changes

## Summary

The current projectile implementation has a good separation between MonoBehaviour-facing gameplay code and ECS simulation code. The main issues are not the basic ECS layout itself, but lifecycle management, simulation order, rendering data flow, and long-run high-churn behavior.

The most important change is to stop creating projectile entities forever. Deactivating projectiles with an enableable component is a good direction, but inactive entities need to be reused or occasionally trimmed. Destroying entities every frame can cause structural-change cost, so the recommended approach is pooling/reuse first, cleanup second.

---

## Priority 0 — Must Fix Before Long Stress Tests

### 1. Reuse inactive projectile entities instead of always creating new ones

**Problem**

`ProjectileRoot.Spawn()` always creates a new entity. Expired or consumed projectiles are only deactivated by disabling `ProjectileActiveTag` and setting lifetime to zero. If the game constantly spawns and despawns projectiles, the total number of projectile entities will grow forever.

**Why this matters**

Even if disabled projectiles are excluded from active queries, they still exist in chunks. Over time this increases memory usage, chunk count, and cleanup cost. In long bullet-hell stress tests, this can slowly degrade performance or produce misleading profiler results.

**Recommended change**

Use disabled projectile entities as a pool.

Spawn flow:

```text
1. Try to find/reuse an inactive projectile entity owned by this ProjectileRoot scope.
2. If one exists:
   - reset ProjectileComponent
   - clear ProjectileContactGateElement buffer
   - enable ProjectileActiveTag
3. If none exists:
   - create a new entity
   - add required buffers/components
```

Expire/hit flow:

```text
1. Set RemainingLifetime = 0
2. Disable ProjectileActiveTag
3. Leave entity available for reuse
```

Optional cleanup flow:

```text
If inactive pool size is much larger than needed:
    destroy extras in small batches outside the hottest path
```

**Do not rely on immediate destroy as the main path** for high-churn projectiles. Destroying entities is a structural change. It is acceptable as an occasional trimming mechanism, but not ideal as the main per-frame despawn path.

---

### 2. Track despawn/recycle count correctly

**Problem**

`despawnedProjectiles` exists in runtime counters, but it is not incremented anywhere.

**Recommended change**

Increment despawn/recycle count when a projectile transitions from active to inactive.

Possible places:

- collision deactivation
- lifetime expiration
- a dedicated projectile recycling system

Preferred design:

```text
ProjectileDeactivateRequest buffer/event
    -> main/root drains it
    -> updates counters
```

or

```text
ProjectileRecycleSystem counts disabled transitions
```

Do not increment every frame for already-inactive projectiles. Only count the active -> inactive transition.

---

### 3. Fix `Step(float deltaTime)` semantics

**Problem**

`Step(float deltaTime)` accepts a `deltaTime`, but the value is not actually used. The method updates the default `SimulationSystemGroup`, which uses the world's normal time data.

**Why this matters**

This makes manual tests misleading. A caller may assume they are stepping the projectile simulation by a specific amount, but the actual systems are using Unity's world time.

**Recommended options**

Option A — remove the parameter:

```csharp
public void Step()
```

Option B — implement true manual stepping:

```text
1. Set or override world time for the step.
2. Update only the projectile-related systems or a dedicated projectile system group.
3. Restore time if needed.
```

Option C — avoid manual stepping and rely on normal Unity player-loop simulation.

---

## Priority 1 — Correctness / Gameplay Semantics

### 4. Reconsider system order: lifetime currently runs before collision

**Current behavior**

The order is effectively:

```text
ProjectileSimulationSystem
ProjectileTrackingSystem
ProjectileMovementSystem
ProjectileChildSpawnSystem
ProjectileLifetimeSystem
ProjectileContactGateSystem
ProjectileCollisionSystem
```

**Problem**

A projectile can move into a target on its final frame, then `ProjectileLifetimeSystem` disables it before collision runs. That means final-frame hits can be missed.

**Recommended order**

Usually better:

```text
ProjectileSimulationSystem
ProjectileTrackingSystem
ProjectileMovementSystem
ProjectileContactGateSystem
ProjectileCollisionSystem
ProjectileChildSpawnSystem     // depends on desired behavior
ProjectileLifetimeSystem
```

Alternative:

```text
Movement -> Collision -> Lifetime
```

The important rule is:

```text
If a projectile was alive at the start of the frame and moved this frame, it should usually be allowed to collide this frame.
```

---

### 5. Decide whether child spawning should happen on the final lifetime frame

**Current behavior**

Child spawn runs before lifetime decrement. A projectile with almost no lifetime remaining can spawn children and then immediately expire.

**This may be valid if intentional.**

If not intentional, change the child spawn guard to account for the upcoming lifetime decrement:

```csharp
if (projectile.RemainingLifetime - DeltaTime <= 0f)
{
    return;
}
```

Recommended decision:

```text
If child spawn represents a timed behavior while the projectile exists:
    do not spawn after the projectile's lifetime would end.

If child spawn represents an on-expire split/explosion behavior:
    handle it explicitly as an expiration event instead.
```

---

### 6. Make hit and child-spawn replay order deterministic if order matters

**Problem**

`ProjectileHitElement` and `ProjectileChildSpawnRequestElement` contain an `Order` field, but the drain methods replay buffers in their existing order without explicitly sorting.

ECB parallel-writer sort keys help playback ordering, but relying on this alone is fragile if gameplay correctness depends on deterministic replay order.

**Recommended change**

Before invoking managed events, sort pending hits and child-spawn requests by a deterministic key.

Example concept:

```csharp
pendingHits.Sort((a, b) => a.Context.ProjectileId.CompareTo(b.Context.ProjectileId));
```

Better key:

```text
frame/order group
projectile id
target id or spawn tick
```

Also consider replacing the current event order key:

```csharp
((uint)projectileId << 12) ^ (uint)(targetId & 0x0FFF)
```

because target IDs/tick IDs above 4095 alias into the same low 12 bits.

---

### 7. Enforce unique target IDs

**Problem**

The target buffer can contain multiple targets with the same `TargetId`, while `targetsById[target.TargetId] = target` overwrites previous entries.

**Recommended change**

Enforce uniqueness in `ProjectileTargetRegistry.Register()` or centralize target ID assignment.

Recommended rule:

```text
A ProjectileTargetRegistry should reject duplicate TargetId values.
```

Possible behavior:

```text
- log error in development builds
- ignore duplicate registration
- or replace only if explicitly intended
```

---

## Priority 2 — Rendering Path Improvements

### 8. Avoid copying and scanning all projectile components on the main thread every frame

**Current behavior**

`DrawProjectiles()` copies active `ProjectileComponent` data to a temporary array, then scans that array once for every render type.

Cost shape:

```text
copy active projectiles to main thread
+ activeProjectileCount * renderTypeCount filtering
+ matrix generation on main thread
```

This is acceptable as a bridge, but it is not ideal for 20k–50k projectile workloads.

**Recommended change**

Generate render transforms in jobs, grouped by projectile type.

Possible design:

```text
ProjectileRenderBuildSystem
    - reads active projectiles
    - writes per-type transform buffers or NativeLists

ProjectileRoot.LateUpdate
    - submits Graphics.DrawMeshInstanced calls using already-built matrices
```

If staying with `Graphics.DrawMeshInstanced`, the draw submission remains main-thread, but filtering and matrix generation can move out of the main thread.

Longer-term options:

```text
- Graphics.DrawMeshInstancedIndirect
- BatchRendererGroup
- Entities Graphics, if compatible with the project
- custom compute/GPU-driven rendering for extreme counts
```

---

### 9. Destroy all generated render resources

**Problem**

Template/render-type resources create meshes and materials, but `OnDestroy()` only destroys the default `projectileMaterial` and `projectileMesh` fields.

**Recommended change**

Destroy all unique meshes and materials stored in `renderResourcesByType`.

Concept:

```csharp
private void DestroyRenderResources()
{
    var destroyedMeshes = new HashSet<Mesh>();
    var destroyedMaterials = new HashSet<Material>();

    foreach (var resources in renderResourcesByType.Values)
    {
        if (resources.Mesh != null && destroyedMeshes.Add(resources.Mesh))
        {
            Destroy(resources.Mesh);
        }

        if (resources.Material != null && destroyedMaterials.Add(resources.Material))
        {
            Destroy(resources.Material);
        }
    }

    renderResourcesByType.Clear();
}
```

Call this from `OnDestroy()`.

---

## Priority 3 — ECS Data Layout / Cache Improvements

### 10. Split optional behavior out of `ProjectileComponent`

**Problem**

`ProjectileComponent` contains data for many features:

```text
movement
collision shape
damage
lifetime
tracking
child spawning
pierce/repeat-hit behavior
```

This is convenient, but every system touches a large component even if most projectiles do not use all features.

**Recommended gradual split**

Start with optional features:

```csharp
public struct ProjectileTrackingComponent : IComponentData
{
    public float RangeSquared;
    public float TurnSpeedRadians;
    public float QueryCooldownRemaining;
    public float QueryIntervalSeconds;
    public int TrackedTargetId;
    public int TrackedTargetIndex;
}

public struct ProjectileChildSpawnerComponent : IComponentData
{
    public int SpawnerId;
    public float IntervalSeconds;
    public float CooldownRemaining;
    public int TickIndex;
}
```

Then tracking systems only iterate projectiles that actually have tracking data, and child-spawn systems only iterate projectiles that actually spawn children.

Suggested migration order:

```text
1. Split tracking data.
2. Split child-spawn data.
3. Consider splitting collision shape only if profiling shows component bandwidth matters.
```

Do not over-split immediately. Extra archetypes and structural changes also have a cost.

---

### 11. Add specialized queries for tracking and child-spawn projectiles

If you do not want to split components yet, at least add enableable tags:

```csharp
public struct ProjectileTrackingTag : IComponentData, IEnableableComponent {}
public struct ProjectileChildSpawnerTag : IComponentData, IEnableableComponent {}
```

Then systems can avoid iterating all active projectiles just to early-return.

Recommended only if many projectiles do not use these features.

---

### 12. Keep contact gates only if gate count stays small

**Current design**

Each projectile has a dynamic buffer of contact gates. For every candidate target, collision checks whether that target is gated by linearly scanning the buffer.

This is fine if most projectiles gate only a few targets.

**Watch out for**

```text
piercing projectile + many targets + long repeat-hit cooldown
```

That can make each projectile's contact gate buffer large.

**Recommended profiling metric**

Track:

```text
average contact gates per active projectile
max contact gates on one projectile
```

If these stay low, keep the current design.

---

## Priority 4 — Collision and Math Hardening

### 13. Guard against zero-sized rectangles before normalizing axes

`RectangleRectangle()` computes axes using normalized edge vectors. If a rectangle has zero width or height, normalization can produce invalid values.

Recommended guard:

```text
If halfExtents.x <= epsilon or halfExtents.y <= epsilon:
    treat as line/circle fallback
    or skip collision
    or clamp to minimum extent
```

---

### 14. Rename and separate epsilon constants

Current constant:

```csharp
MinimumDirectionLengthSquared
```

is used for more than direction length. It is also used as a geometric epsilon in segment intersection and point-on-segment checks.

Recommended constants:

```csharp
public const float MinimumDirectionLengthSquared = 0.000001f;
public const float GeometryEpsilon = 0.000001f;
public const float GeometryEpsilonSquared = GeometryEpsilon * GeometryEpsilon;
```

This avoids confusing squared and non-squared tolerances.

---

### 15. Consider swept collision for very fast projectiles

Current collision is discrete: it checks the projectile at its current position after movement.

This can tunnel if:

```text
projectile speed * deltaTime > target thickness + projectile size
```

Possible fixes:

```text
- increase projectile radius for collision padding
- use ray/sweep from previous position to current position
- store PreviousPosition in ProjectileComponent
- use substeps for very fast projectiles
```

Recommended minimal change:

```csharp
public float2 PreviousPosition;
```

Then movement does:

```csharp
projectile.PreviousPosition = projectile.Position;
projectile.Position += projectile.Velocity * DeltaTime;
```

Collision can then use either discrete collision or swept collision depending on projectile type.

---

## Priority 5 — Scope / Multi-root Safety

### 16. Avoid updating the entire default simulation group from one ProjectileRoot

`ProjectileRoot.Step()` updates the default `SimulationSystemGroup`. If multiple systems are in the world, this can update unrelated simulation systems.

Recommended approach:

```text
- create a dedicated ProjectileSimulationSystemGroup
- put projectile systems into that group
- manually update only that group if manual stepping is needed
```

Or avoid manual stepping entirely and let Unity's player loop run the systems normally.

---

### 17. Review multi-root behavior

The implementation supports a `Scope` entity, which is good. However, systems still iterate all active projectiles, then each projectile references its scope.

This is fine for a small number of roots.

If many projectile roots exist, consider:

```text
- one shared projectile root for player projectiles
- one shared projectile root for enemy projectiles
- avoid dozens/hundreds of separate scope entities unless needed
```

---

## Suggested Implementation Order

### Phase 1 — Stability

```text
1. Add inactive projectile reuse/pooling.
2. Clear contact gate buffer on reuse.
3. Fix despawn/recycle counter.
4. Fix Step(deltaTime) or remove the misleading parameter.
5. Destroy all generated render resources.
```

### Phase 2 — Gameplay correctness

```text
1. Move collision before lifetime expiration, or explicitly allow final-frame collision.
2. Decide final-frame child-spawn semantics.
3. Sort hit/child-spawn replay if deterministic order matters.
4. Enforce unique target IDs.
```

### Phase 3 — Performance

```text
1. Move render transform generation/filtering into jobs.
2. Split tracking into an optional component/tag.
3. Split child-spawn into an optional component/tag.
4. Add broadphase if target count grows beyond small numbers.
```

### Phase 4 — Advanced collision

```text
1. Add PreviousPosition.
2. Add swept collision for fast projectiles.
3. Harden zero-size shape handling.
4. Separate geometry epsilon constants.
```

---

## Recommended Final Shape

A strong medium-term design would look like this:

```text
MonoBehaviour side:
    ProjectileRoot
        - owns scope entity
        - registers targets
        - drains hit/spawn events
        - submits render batches

ECS side:
    ProjectileSimulationSystem
        - clears per-frame event buffers

    ProjectileTrackingSystem
        - only tracking projectiles

    ProjectileMovementSystem
        - active projectile kinematics

    ProjectileContactGateSystem
        - active projectile hit cooldowns

    ProjectileCollisionSystem
        - detects hits
        - writes hit events
        - disables/recycles consumed projectiles

    ProjectileChildSpawnSystem
        - only child-spawning projectiles
        - writes child-spawn events

    ProjectileLifetimeSystem
        - expires remaining projectiles

    ProjectileRenderBuildSystem
        - builds per-type matrices or render records in jobs
```

Entity lifecycle:

```text
new entity only when pool is empty
active projectile during simulation
disable active tag when expired/consumed
reuse disabled projectile for future spawn
trim excess inactive entities only occasionally
```

This keeps the hot path mostly free from structural changes while avoiding unbounded entity growth.
