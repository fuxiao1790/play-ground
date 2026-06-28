# Plan: CombatRoot → Render Config Holder

## Goal

Make `CombatRoot` a **config holder** for rendering: it reads authored/serialized
config (sprites, scales, rotations, layer, AOE visuals) and pushes it to the ECS
render registry. It no longer *owns* render resources.

After this change `CombatRenderResourceRegistry` is the single owner of everything
about render resources — the render-id counter, the GPU resource store, GPU
lifetime, and render-component construction. `CombatRoot` keeps only:
- the authored config it reads from the scene,
- the per-domain `typeId → renderId` int maps the `Spawn(Request)` path needs,
- the render-layout constants (`ProjectileRenderZ`, `AoeRenderZ`, …) that several
  systems already reference as `CombatRoot.*` consts.

It also **sheds the `spawnVisuals` toggle** (002): whether an AOE renders is already
decided by whether its type's prefab carries a sprite (`AoeTypeRegistry.TryGetVisual`),
so the separate global bool was a redundant second source of truth. Removing it leaves
sprite presence as the sole gate.

## Design choice: read-back, not push-from-caller

`RegisterTemplate` / `RegisterConfig` / `RegisterType` are the existing
register-by-config entry points, shared by **three** caller groups:

| Caller | Calls |
|---|---|
| Compiler | `PlayerSkillDriver.cs:207-208` (`RegisterTemplate` + `ProjectileRenderId`), `:572-573` (`RegisterType` + `AoeRenderId`) |
| Mob | `MobProjectileAttack.cs:54,100` (`RegisterTemplate`), `MobRoot.cs:349` (`RegisterConfig`) |
| Tests | `BareMinimumPrototypePlayModeTests.cs:120,781`, `AoePlayModeTests.cs` (several `RegisterType`) |

These entry points stay the **single** place that translates authored config →
render-build arguments. Internally they now call `registry.Register(...)` instead
of the deleted local `RegisterRenderResource`, and they keep populating the local
`typeId → renderId` map. Every caller is **unchanged** — it still calls
register-by-config and reads the id back via `ProjectileRenderId` / `AoeRenderId`.

This is deliberately **not** the "compiler calls registry.Register directly" shape.
That alternative would (a) duplicate the config→args translation across the compiler
and the Awake path, (b) leak `CombatRoot.gameObject.layer` out as a property, and
(c) require making `RegisterTemplate` render-agnostic — which silently breaks the mob
and test paths that depend on it registering visuals. Read-back keeps one translation
path, one source of truth, and zero caller churn. CombatRoot-as-config-holder means
config flows *in* and is pushed to the registry from the object that holds it.

## Constraints & Invariants

| Constraint | Source |
|---|---|
| `CombatRenderResourceRegistry` is a managed `IComponentData` class (singleton); may hold managed objects | `CombatRenderComponents.cs:48-51` |
| `BatchedSpriteRenderer.BuildResources` allocates GPU objects (Mesh, Material); main thread only | Unity API rule |
| `Destroy()` must be called on GPU objects at teardown | `BatchedSpriteRenderer.cs` |
| `CombatBatchedRenderSystem.OnUpdate` iterates `registry.Entries`; registry must stay stable during presentation | `CombatBatchedRenderSystem.cs` |
| Register before the first spawn that uses the id; ordering: register → spawn event → apply | `combat-root-api.md §Ordering` |
| ECS world is initialized before scene `Awake`; registry singleton exists when `CombatRoot.Awake` runs | `CombatBatchedRenderSystem.OnCreate` |
| Batch id == `renderId`: apply systems write `cmd.RenderTypeId` straight into `CombatRenderBatchId.Value` | `ProjectileSpawnApplySystem.cs:447` |
| One unified `CombatRoot`; `Awake`/teardown each run once; no concurrent or faction-filtered registration | Faction overhaul (task 006) |
| `batchBoundsHalfExtent` is effectively infinite (100 000) and cosmetic | Confirmed in conversation |

## Mechanisms Reused vs. Introduced

**Reused**
- `CombatRenderResourceRegistry.Entries` (`Dictionary<int, CombatRenderResourceEntry>`)
  — already the authoritative ECS-side store; `Entries[renderId].Resources` already
  holds the `CombatSpriteRenderResources`. This stays the **single** resource store.
- `BatchedSpriteRenderer.BuildResources` — called by the registry now instead of CombatRoot.
- `CombatRoot.ProjectileRenderZ` / `AoeRenderZ` / `…Slots` / `…Step` consts — referenced
  by the registry's component builders and by external systems; left in CombatRoot.

**Introduced (all on the registry)**
- render-id counter (`_nextRenderId`) — moved off CombatRoot; the registry owns the id space.
- `Register(sprite, visualScale, rotDeg, material, meshName, layer) → int` — mint + build +
  publish into `Entries`. No `_resourcesById` mirror; `Entries` is the only store.
- `GetProjectileRenderComponent(renderId, projectileId)` / `GetAoeRenderComponent(renderId, geometry)`
  — read from `Entries[renderId].Resources`; replace the CombatRoot builders.
- `Unregister()` — destroy every `Entries[].Resources` and clear `Entries`. One root, one teardown.
- public mesh-name consts `ProjectileMeshName` / `AoeMeshName` — moved off CombatRoot so the
  config-holder can pass them when calling `Register`.

**Not introduced:** no second resource dictionary, no `BatchIdFor`, no `SetRenderId`
setters, no `RenderRegistry` accessor, no faction parameters.

## Refactor vs. Additive Comparison

**Additive (rejected):** keep `CombatRoot.renderResourcesById` + counter as a mirror of
`registry.Entries`; CombatRoot keeps building render components from its own copy. →
two owners of the same GPU resource data, teardown must hand-unregister, and the registry
can't manage its own GPU lifetime. This is the duplication we are removing.

**Refactor (chosen):** registry owns counter + store (`Entries`) + GPU lifecycle +
component construction. CombatRoot reads config and pushes via `Register`, holds only
`int → int` maps. → single source of truth; CombatRoot's render role is pure config push;
no mirrored dictionary; zero caller changes.

**Earlier-draft trap, now avoided:** an interim draft added a *new* `_resourcesById`
dictionary to the registry. That would have removed CombatRoot's duplicate store only to
recreate it inside the registry (two dicts keyed by `renderId`, both owning the resources,
kept in sync). The rewrite uses `Entries` as the sole store.

## Design Validation

| Invariant | Validation |
|---|---|
| GPU objects built on main thread | `Register` is called only from `CombatRoot` `Awake`/registration entry points (MonoBehaviour main thread). ✓ |
| Single resource store | Only `Entries`; `_resourcesById` eliminated. ✓ |
| Destroy at teardown | `Unregister()` destroys each `Entries[].Resources`; called from `CombatRoot.OnDestroy`. ✓ |
| Registry stable during presentation | `Unregister` runs in `OnDestroy`, outside the system update loop. ✓ |
| Register before spawn | Awake registration + compiler/mob registration all run before any `Spawn(Request)`. ✓ |
| Batch id == renderId | Apply systems already write `cmd.RenderTypeId`; registry key is `renderId`. ✓ |
| Callers unchanged | `RegisterTemplate`/`RegisterConfig`/`RegisterType` keep their signatures and the read-back contract; grep of all call sites confirms no other entry points. ✓ |

## Task List

| # | File(s) | Description |
|---|---|---|
| [001](001-extend-registry.md) | `CombatRenderComponents.cs` | Add counter, mesh-name consts, `Register`, `GetProjectileRenderComponent`, `GetAoeRenderComponent`, `Unregister` to the registry — using `Entries` as the only store |
| [002](002-update-combat-root.md) | `CombatRoot.cs` | Delete render-resource ownership; route registration entry points + command builders + teardown through the registry; keep config + `int→int` maps + render-Z consts |

**Landing order:** 001 → 002. 002 depends on 001.

**No task for callers.** `PlayerSkillDriver`, `MobProjectileAttack`, `MobRoot`, and the
PlayMode tests are intentionally untouched — preserving them is the point of the read-back
design. 002's acceptance criteria include verifying they still compile and that mob/test
visuals still register.
