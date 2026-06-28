# Plan: CombatRoot → Config Holder for ECS

## Summary

Move render resource ownership — id counter, GPU resource dictionary, lifecycle, and
component-building logic — from `CombatRoot` into `CombatRenderResourceRegistry` (the
ECS managed-component singleton). After this change `CombatRoot`'s render role is limited
to: reading authored/serialized config (sprites, scales, layers, faction), calling
`registry.Register(...)` at `Awake`, and storing the local `typeId → renderId` maps needed
by the `Spawn(Request)` path. All other callers (the skill compiler) go directly to the
registry.

## Constraints & Invariants

| Constraint | Source |
|---|---|
| `CombatRenderResourceRegistry` is a managed `IComponentData` class (singleton); it may hold managed objects (Dictionary, Mesh, Material) | `CombatRenderComponents.cs:48` |
| `BatchedSpriteRenderer.BuildResources` allocates GPU objects (Mesh, Material); must be called on main thread | Unity API rule |
| `Destroy()` must be called on GPU objects at teardown to release GPU memory | `BatchedSpriteRenderer.cs:263–274` |
| `CombatBatchedRenderSystem.OnUpdate` iterates `registry.Entries` and `SetSharedComponentFilter` per entry; registry must stay stable during presentation | `CombatBatchedRenderSystem.cs:54–57` |
| Render resources must be registered before the first spawn event that uses them; ordering: register → spawn event → apply | `combat-root-api.md §Ordering` |
| ECS world is initialized before scene MonoBehaviour `Awake`; registry singleton exists when CombatRoot.Awake runs | Unity lifecycle + `CombatBatchedRenderSystem.OnCreate` |
| `CombatRenderBatchId` shared component partitions entity chunks; value is `renderId` (faction overhaul removed the `(faction<<16)\|renderId` encoding); the render id space is globally unique within the one CombatRoot | `CombatRenderComponents.cs:34–39`, `ProjectileSpawnApplySystem.cs:447` |
| `batchBoundsHalfExtent` is effectively infinite (100 000); not a load-bearing culling invariant — treat as cosmetic | Confirmed in conversation |
| There is one unified `CombatRoot`; `Awake` is called once; teardown is called once. No concurrent registration or faction-filtered teardown. | Faction overhaul (task 006) |

## Mechanisms Reused vs. Introduced

**Reused**
- `CombatRenderResourceRegistry` managed singleton — already exists on an entity created in `CombatBatchedRenderSystem.OnCreate`; new methods added to the existing class.
- `BatchedSpriteRenderer.BuildResources` — static utility; called by registry instead of CombatRoot.
- `Entries[renderId]` key — the faction overhaul already changed the registry key to plain `renderId`; we keep that encoding.

**Introduced**
- `CombatRenderResourceRegistry.Register(sprite, visualScale, rotDeg, material, meshName, layer)` — unified mint + build + publish. No faction parameter (batch id = renderId, globally unique within the one root).
- `CombatRenderResourceRegistry.GetProjectileRenderComponent(renderId, projectileId)` — replaces `CombatRoot.ProjectileRenderComponentForRenderId`.
- `CombatRenderResourceRegistry.GetAoeRenderComponent(renderId, geometry)` — replaces `CombatRoot.AoeRenderComponentForRenderId`.
- `CombatRenderResourceRegistry.Unregister()` — removes + destroys all entries; replaces `CombatRoot.DestroyRenderResources` + the manual `Entries.Remove` loop. No faction parameter needed (one root owns all entries).
- `CombatRoot.RenderRegistry` property — exposes the cached registry reference so the compiler can call it directly.

**Justification**: no parallel path is introduced. The registry was already the authoritative ECS-side store; this change makes it own the lifecycle and logic that were previously duplicated in CombatRoot.

## Minimal/Additive vs. Refactor Comparison

**Additive**
- Resulting data flow: CombatRoot keeps `renderResourcesById` + counter; compiler continues calling through CombatRoot methods.
- New concepts: none — existing indirection preserved.
- Copies/translations: CombatRoot still holds a mirror of registry data to answer `GetRenderComponent` calls.
- Long-term cost: two owners of render resource data; CombatRoot teardown must keep manually unregistering + destroying; compiler cannot reach ECS directly.

**Refactor (chosen)**
- Resulting data flow: registry owns counter + resources dict + GPU lifecycle; compiler calls registry directly via `combatRoot.RenderRegistry`; CombatRoot holds only typeId→renderId maps (needed for the `Spawn(Request)` path).
- Existing concepts removed: `CombatRoot.renderResourcesById`, `nextRenderId`, `RegisterRenderResource`, `ProjectileRenderComponentForRenderId`, `AoeRenderComponentForRenderId`, `DestroyRenderResources`, `BatchIdFor`.
- Copies removed: no more parallel data dict in CombatRoot.
- Long-term benefit: single source of truth for render resources; CombatRoot's render role is pure config push; registry is self-contained for GPU lifetime management.

**Decision: refactor.** Two owners of the same resource data with no concrete compatibility reason to keep both.

## Design Validation

| Invariant | Validation |
|---|---|
| GPU objects built on main thread | `Registry.Register` is called from MonoBehaviour `Awake` and from compiler code in `PlayerSkillDriver` — both main-thread contexts. No burst/job involvement. ✓ |
| Destroy called at teardown | `Registry.Unregister()` removes all entries and calls `Destroy()` on each `CombatSpriteRenderResources`; called from `CombatRoot.OnDestroy`. ✓ |
| Registry stable during presentation | `Unregister` is called from `OnDestroy`, which fires outside the ECS system update loop. ✓ |
| Register before spawn | `Awake` calls `BuildProjectileRenderResources` (calls `registry.Register`); compiler registers before building spawn templates; both happen before any `Spawn(Request)` call. ✓ |
| Batch id = renderId | Faction overhaul already changed apply systems to write `cmd.RenderTypeId` directly into `CombatRenderBatchId.Value`; registry key is `renderId`. No `BatchIdFor` helper needed. ✓ |
| ECS singleton exists at Awake | Registry created in `CombatBatchedRenderSystem.OnCreate`; ECS world initializes before scene Awake. ✓ |

## Notes

The faction overhaul (task 006) reduced CombatRoot to a single instance and changed the render batch id encoding to plain `renderId`. Both of those changes remove earlier complexity from this plan:
- No `BatchIdFor(faction, renderId)` helper needed — the key is just `renderId`.
- `Unregister` needs no faction parameter — one root, one teardown, clear everything.
- No concurrent-Awake concern — one Awake.
- `Register` has no `faction` parameter — batch id is faction-free.

## Task List

| # | File(s) | Description |
|---|---|---|
| [001](001-extend-registry.md) | `CombatRenderComponents.cs` | Add counter, resources dict, `Register`, `GetProjectileRenderComponent`, `GetAoeRenderComponent`, `Unregister`, `BatchIdFor` to `CombatRenderResourceRegistry` |
| [002](002-update-combat-root.md) | `CombatRoot.cs` | Remove render ownership; expose `RenderRegistry`; update Awake setup + teardown + command builders to use registry |
| [003](003-update-compiler.md) | `PlayerSkillDriver.cs` | Compiler calls `registry.Register(...)` directly; removes `combatRoot.RegisterRenderResource`, `combatRoot.ProjectileRenderId`, `combatRoot.AoeRenderId` |

**Landing order**: 001 → 002 → 003. Steps 002 and 003 both depend on 001.
