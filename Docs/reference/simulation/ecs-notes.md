# ECS Performance Notes

This is the detailed ECS performance and high-churn notes doc. Use
[index.md](./index.md) for the simulation overview and aspect map.

> Source: [Your ECS Probably Still Sucks](https://gist.github.com/Dreaming381/89d65f81b9b430ffead443a2d430defc) — Dreaming381

---

## Part 1: Memory

**Spatial locality**
- Iterate actual component values, not indices
- Archetype ECS with multi-component iteration is the right model — lets you stream multiple components together

**Temporal locality**
- Cache lines evict under pressure from many data loads
- Combine related systems to avoid redundant component loads — overly granular systems hurt performance

**Temporary memory**
- Reuse allocations to prevent cache eviction from new addresses
- Strict aliasing violations (pointer recasting between types) = UB; compiler may silently misoptimize

**Low-level access**
- Expose raw memory APIs so callers can use SIMD and skip loading components they don't need

---

## Part 2: High-Ratio Optimizations

**Value groups** (Unity shared components)
- Group entities by a component value for cheap filtering without random access

**Slice aggregate values**
- Organize entities into fixed-size slices (~64 entities each)
- Store aggregate metadata per slice: bools, bitmasks, min/max
- Skip entire slices cheaply → up to 64x less work when matches are sparse

**Change detection**
- Per-slice versioning > per-entity versioning
- Combine with archetype-change detection for maximum skip coverage

**Granular bit filtering**
- 64-bit bitfield per slice, one bit per entity
- Use hardware bit ops (TZCNT, BLSR etc.) to iterate only matching entities within a slice

**Parallelism**
- Slice boundaries are natural parallel splits
- Single-threaded bottlenecks can operate on already-reduced datasets

---

## Part 3: Relationships

**Core principle**
> Optimize data *flow*, not just data layout. Relationships aren't the problem — inefficient access patterns are.

**Dynamic archetype changes**
- Allow entities to form/break relationships via archetype mutation
- Avoids reserving memory for every possible relationship state upfront

**Sticky value caching (push model)**
- EntityA *pushes* updates to dependents only on change
- Replaces cache-unfriendly random reads (pull) with sequential writes (push)
- Works well combined with change detection from Part 2

**Determinism**
- Fixed execution order makes relationship assumptions reliable
- Reduces cross-entity communication needed to stay consistent

---

## Summary

| Problem | Solution |
|---|---|
| Cache thrash from scattered reads | Archetype ECS, combined systems |
| Iterating irrelevant entities | Slice aggregates + bit filtering |
| Expensive per-entity change tracking | Per-slice versioning |
| Random-access relationship reads | Push-on-change / sticky cache |
| Parallelism granularity | Slice boundaries as work units |

---

# Unity ECS: Structural Changes & High Churn
> Sources: Unity Docs (Entities 1.0–1.4), Unity Discussions

---

## What Is a Structural Change

Any operation that reorganizes archetype chunks:
- Create / destroy entity
- Add / remove component
- Modify shared component value

**Result:** entity moves to a new chunk. Old chunk is compacted or freed.
**Constraint:** can only run on main thread (not inside jobs).

---

## Sync Points

A sync point waits for **all scheduled jobs** to complete before proceeding.
- Structural changes always cause a sync point
- Sync points idle all worker threads → major perf hit at scale
- Any direct component reference (`DynamicBuffer`, `GetComponentDataFromEntity`) is **invalidated** after a structural change

Two consecutive systems both making structural changes = **one** sync point, not two. Group them.

---

## Archetypes & Chunks

- Archetype = unique set of component types
- Chunk = 16 KiB memory block; holds all entities of one archetype
- Each chunk has one tightly-packed array per component type + entity ID array
- Entity removal: last entity in chunk fills the gap
- Archetype set stabilizes early in runtime → query caching pays off

Moving entities between archetypes frequently is expensive. Design to minimize it.

---

## Optimization Strategies

### 1. Enableable Components — fastest (0.03 ms / 1M entities)
- Implement `IEnableableComponent` on `IComponentData` or `IBufferElementData`
- `SetComponentEnabled<T>(entity, bool)` — **no structural change**, no archetype move
- Query behavior: disabled component = entity excluded from queries requiring it; included in queries excluding it
- `HasComponent<T>()` / `GetComponent<T>()` still work regardless of enabled state
- If **all** components of a type in a chunk are disabled → entire chunk is skipped
- Tradeoff: entities carry more components → more memory, potential chunk fragmentation

### 2. Batch via EntityQuery — fast (3.5 ms / 1M)
```csharp
EntityManager.AddComponent<T>(myQuery);  // chunk-level, not per-entity
```
- Pass `EntityQuery` instead of `NativeArray<Entity>` — operates at chunk granularity
- ECB equivalent: use `EntityQueryCaptureMode.AtPlayback` to defer query evaluation to playback time

### 3. ComponentTypeSet — add/remove multiple at once
```csharp
EntityManager.AddComponent(entity, new ComponentTypeSet(typeof(A), typeof(B)));
```
Fewer archetype hops per entity.

### 4. Pre-create archetypes, then instantiate
```csharp
var arch = state.EntityManager.CreateArchetype(typeof(Foo), typeof(Bar));
state.EntityManager.CreateEntity(arch, entities);  // not incremental add
```
Avoids intermediate archetype states during construction.

### 5. Entity Command Buffers (ECB)
- Queue structural changes in jobs; play back at a defined sync point
- Use `EntityCommandBufferSystem` provided by each `ComponentSystemGroup`
- Consolidates many scattered sync points into one per phase

### Performance reference (adding component to 1M entities)

| Method | Time |
|---|---|
| Enableable component | 0.03 ms |
| `EntityManager` + `EntityQuery` | 3.5 ms |
| ECB + `EntityQuery` (AtPlayback) | 3.5 ms |
| ECB + `IJobChunk` | 17 ms |
| `EntityManager` + `NativeArray` | 35 ms |
| ECB + `NativeArray` | 35 ms |
| ECB + `IJobEntity` | 170 ms |

---

## High-Churn Patterns

When entities need frequent state changes every frame:

**Prefer: never change archetype post-creation**
- Design archetypes upfront; entities keep them until destroyed
- Use enableable components for toggling states

**DynamicBuffer for transient behaviors**
- Store per-frame events/states as `IBufferElementData` elements
- Queryable with change filters, no archetype change

**Separate single-frame event entities**
- Lightweight entity with one component, destroyed next frame via ECB
- Cost: one ECB sync point per batch

**Boolean flag + iterate**
- Single `isActive` bool component; filter in-loop
- Counterintuitive: iterating `false` values in cache often faster than sparse query + cache miss

**Avoid: enableable components for rare/persistent changes**
- If state changes infrequently, add/remove is fine and saves memory

See [project-ecs-implementation.md](./project-ecs-implementation.md) for project-specific pool patterns and architectural decisions.

---

## Frame Timing for Structural Changes

Recommended phase structure:
1. **Begin Frame** — spawn new entities (no stall on jobs yet)
2. **Before Simulation** — input, time, triggers
3. **Simulation** — `Schedule` / `ScheduleParallel` jobs
4. **After Simulation** — pass results to MonoBehaviours, spawn VFX
5. **Presentation** — post-sim before render

Each phase owns its own `EntityCommandBufferSystem`. ECB plays back once per phase → one sync point per phase max.

**Gotcha:** if using cleanup components, deletion systems must run *before* systems that operate on those entities, or you'll process entities already scheduled for removal.

---

## Profiling

Unity Profiler → **Entities Structural Changes** module:
- Charts: Creating Entities / Destroying Entities / Adding Components / Removing Components
- Details pane: per-World, per-system breakdown with cost (ms) and count
