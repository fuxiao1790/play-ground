# Plan: Decouple Render Resources from Faction

## Summary

Replace the two shared render components `(CombatRenderFaction, CombatRenderTypeId)` with a single
`CombatRenderBatchId`. Move render resource ownership (resources, layer, bounds) from `CombatRoot`
instance dictionaries to a managed `IComponentData` singleton entity `CombatRenderResourceRegistry`.
After this change the render system holds no reference to any `CombatRoot` MonoBehaviour at runtime.

## Constraints & Invariants

| Constraint | Source |
|---|---|
| Shared components partition chunks; `SetSharedComponentFilter` is the reuse-pool filter mechanism | `AoeSpawnApplySystem.cs:284`, `ProjectileSpawnApplySystem.cs:206` |
| `Mesh`, `Material`, `MaterialPropertyBlock` are managed Unity objects; cannot go in unmanaged `IComponentData` | `BatchedSpriteRenderer.cs:238–275` |
| Structural changes (Add/RemoveSharedComponent) run on main thread only, deferred through ECB | ecs-notes.md §Structural Changes |
| Archetype shape must be stable post-creation; add/remove during pool lifecycle is forbidden | ecs-notes.md §High-Churn Patterns |
| Resources must have `Destroy()` called at teardown to release GPU memory | `BatchedSpriteRenderer.cs:263–274` |
| The same reuse pool query (`WithDisabled<Active>`) must continue to isolate chunks by visual type | `AoeSpawnApplySystem.cs:241–287`, `ProjectileSpawnApplySystem.cs:198–209` |
| ECS world is initialized before scene MonoBehaviour `Awake`; system `OnCreate` runs first | Unity lifecycle |

## Mechanisms Reused vs. Introduced

**Reused**
- Shared component for chunk partitioning — same pattern; one component (`CombatRenderBatchId`) instead of two.
- ECB cold-create path — one `AddSharedComponent` instead of two; shape otherwise identical.
- `IJobChunk` disabled-slot reuse jobs — unchanged; only the filter changes.

**Introduced**
- `CombatRenderBatchId { int Value }` — globally unique render identity via `(faction << 16) | typeId` namespacing.
- `CombatRenderResourceRegistry` — managed `IComponentData` class on a singleton entity holding
  `Dictionary<int, CombatRenderResourceEntry>`.
- `CombatRenderResourceEntry` — record carrying `{ CombatSpriteRenderResources Resources, int Layer, float BoundsHalfExtent }`.

**Justification for new types**: `CombatRenderBatchId` replaces two components with one; it does not introduce
a second representation. `CombatRenderResourceRegistry` replaces the per-MonoBehaviour dictionaries with
a single ECS-owned registry; both old dicts and new registry cannot coexist long-term without confusion
about the source of truth.

## Additive vs. Refactor Comparison

**Additive**
- Resulting data flow: old two-component filter stays; new singleton added alongside; render system reads both old roots and new registry.
- New concepts introduced: `CombatRenderBatchId`, `CombatRenderResourceRegistry` alongside `CombatRenderFaction` + `CombatRenderTypeId`.
- Copies/translations: two representations of render identity live simultaneously.
- Long-term cost: three shared component types; two resource lookup paths; unclear source of truth.

**Refactor (chosen)**
- Resulting data flow: single `CombatRenderBatchId` shared component; single singleton registry lookup.
- Types changed/removed: `CombatRenderFaction` deleted; `CombatRenderTypeId` deleted; per-faction root lookup deleted.
- Copies/translations removed: render system no longer reaches into MonoBehaviour at runtime.
- Long-term benefit: render path is faction-free; one structural dimension on archetype; clean foundation for render-rework.

**Decision: refactor.** Two representations of render identity describe the same concept. The old ones
serve no purpose once the new ones are in place.

## Design Validation

| Invariant | Validation |
|---|---|
| Reuse pool isolation | `batchId = (faction<<16)\|typeId` is injective over the current faction×typeId space; pool chunks remain isolated. Verified: player faction=1,typeId=1 → 65537; mob faction=2,typeId=1 → 131073. No collision. |
| Chunk cardinality unchanged | Each unique batchId maps 1:1 to the old `(faction,typeId)` pair. Same number of chunk sets. Intent caveat holds: no player/mob pool merge. |
| Managed objects stay managed | `CombatRenderResourceRegistry` is a class-based `IComponentData`; `Mesh`/`Material`/`MaterialPropertyBlock` remain on the managed heap regardless. No violation. |
| Destroy() on teardown | `CombatRoot.OnDestroy` removes entries from registry then calls existing `DestroyRenderResources()`. Order: remove first (registry won't hold stale refs), destroy second. |
| Singleton exists before CombatRoot.Awake | `CombatBatchedRenderSystem.OnCreate` creates singleton. Unity ECS world initializes before scene `Awake`. Defensive check added in `CombatRoot` in case ordering ever changes. |
| Archetype stable post-creation | `CombatRenderBatchId` is added at cold-create time (same as the two old components). No add/remove during pool lifecycle. |
| Render system holds no MonoBehaviour ref | After step 003, `CombatBatchedRenderSystem` reads only the ECS singleton. `TryGetByFaction` call removed. Verified by acceptance criterion. |

## Task List

| # | File(s) | Description |
|---|---|---|
| [001](001-new-types.md) | `CombatRenderComponents.cs` | Add `CombatRenderBatchId`, `CombatRenderResourceEntry`, `CombatRenderResourceRegistry` |
| [002](002-render-system.md) | `CombatBatchedRenderSystem.cs` | Create singleton in `OnCreate`; rewrite `OnUpdate` to read registry + filter by `CombatRenderBatchId` |
| [003](003-combat-root.md) | `CombatRoot.cs` | Compute batchId, populate/remove registry entries, cache registry ref, drop exposed resource properties |
| [004](004-spawn-systems.md) | `AoeSpawnApplySystem.cs`, `ProjectileSpawnApplySystem.cs` | Switch dead-slot queries and cold-create to `CombatRenderBatchId`; collapse spawn keys |
| [005](005-delete-old-types.md) | `CombatRenderComponents.cs`, tests | Delete `CombatRenderFaction` + `CombatRenderTypeId`; update test files |

**Landing order**: 001 → 002 → 003 → 004 → 005. Steps 002–004 must all land before the system is
functional end-to-end. Step 005 is safe once 002–004 are complete and verified.

## Open Questions / Dependencies

- **Downstream**: [render-rework](../render-rework/intent.md) depends on `CombatRenderBatchId` and
  `CombatRenderResourceRegistry` produced here. Land this task first.
- **Spawn-reuse failure**: not addressed here; see spawn-reuse investigation.
- **Faction bit width**: `faction << 16` supports up to 65535 faction values and 65535 type IDs per
  faction. Sufficient for the current two-faction setup; revisit if either grows past 16 bits.
