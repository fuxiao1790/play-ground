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
| `CombatRenderBatchId` shared component partitions entity chunks; value is `(faction << 16) | renderId`; the render id space must remain unique per faction | `CombatRenderComponents.cs:35–39` + apply systems |
| `batchBoundsHalfExtent` is effectively infinite (100 000); not a load-bearing culling invariant — treat as cosmetic | Confirmed in conversation |
| `CombatRoot` MonoBehaviour lifecycle: `Awake` registers, `OnDestroy` teardown; registration order between Player and Mob roots is indeterminate | Unity lifecycle |

## Mechanisms Reused vs. Introduced

**Reused**
- `CombatRenderResourceRegistry` managed singleton — already exists on an entity created in `CombatBatchedRenderSystem.OnCreate`; new methods added to the existing class.
- `BatchIdFor = (faction << 16) | renderId` — encoding unchanged; helper moves from `CombatRoot` to the registry.
- `BatchedSpriteRenderer.BuildResources` — static utility; called by registry instead of CombatRoot.

**Introduced**
- `CombatRenderResourceRegistry.Register(sprite, faction, layer, meshName)` — unified mint + build + publish.
- `CombatRenderResourceRegistry.GetProjectileRenderComponent(renderId, projectileId)` — replaces `CombatRoot.ProjectileRenderComponentForRenderId`.
- `CombatRenderResourceRegistry.GetAoeRenderComponent(renderId, geometry)` — replaces `CombatRoot.AoeRenderComponentForRenderId`.
- `CombatRenderResourceRegistry.Unregister(faction)` — removes + destroys all entries for a faction on teardown; replaces `CombatRoot.DestroyRenderResources` + the manual entry removal loop.
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
| Destroy called at teardown | `Registry.Unregister(faction)` removes entries and calls `Destroy()` on each `CombatSpriteRenderResources`; called from `CombatRoot.OnDestroy`. ✓ |
| Registry stable during presentation | `Unregister` is called from `OnDestroy`, which fires outside the ECS system update loop. ✓ |
| Register before spawn | `Awake` calls `BuildProjectileRenderResources` (calls `registry.Register`); compiler registers before building spawn templates; both happen before any `Spawn(Request)` call. ✓ |
| Batch id encoding unchanged | `BatchIdFor` same formula, moved to registry as a static helper. Apply systems unchanged. ✓ |
| Faction teardown removes only its own entries | `Unregister(faction)` iterates `renderResourcesById` filtering by faction; CombatRoot passes its own `faction` value. ✓ |
| ECS singleton exists at Awake | Registry created in `CombatBatchedRenderSystem.OnCreate`; ECS world initializes before scene Awake. ✓ |

## Open Question

One load-bearing invariant not confirmed in code: when two `CombatRoot` instances (Player + Mob) both call `registry.Register(...)` concurrently in `Awake`, is that safe?

**Answer**: Unity MonoBehaviour `Awake` runs on the main thread sequentially (not in parallel), so two roots cannot interleave. Safe.

## Task List

| # | File(s) | Description |
|---|---|---|
| [001](001-extend-registry.md) | `CombatRenderComponents.cs` | Add counter, resources dict, `Register`, `GetProjectileRenderComponent`, `GetAoeRenderComponent`, `Unregister`, `BatchIdFor` to `CombatRenderResourceRegistry` |
| [002](002-update-combat-root.md) | `CombatRoot.cs` | Remove render ownership; expose `RenderRegistry`; update Awake setup + teardown + command builders to use registry |
| [003](003-update-compiler.md) | `PlayerSkillDriver.cs` | Compiler calls `registry.Register(...)` directly; removes `combatRoot.RegisterRenderResource`, `combatRoot.ProjectileRenderId`, `combatRoot.AoeRenderId` |

**Landing order**: 001 → 002 → 003. Steps 002 and 003 both depend on 001.
