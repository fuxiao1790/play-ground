# Rendering Rework — Dependency Context

Map of how the rendering systems depend on ECS components, on simulation
systems, and on the render components themselves. Reference for the rework.

## Two axes of dependency

Rendering couples to ECS in two ways:

1. **Component data** it reads/writes.
2. **System ordering** across the two groups (Simulation → Presentation).

## Render components

All in [CombatRenderComponents.cs](../../Assets/Scripts/System/Common/CombatRenderComponents.cs).

| Component | Kind | Role |
|---|---|---|
| `CombatRenderComponent` | `IComponentData` | Static per-entity visual params — `VisualScale`, rotation sin/cos, `RenderZ`, `IsRenderable`, `AlignToVelocity`. Set once at spawn. |
| `CombatRenderElement` | `IComponentData` | Computed `objectToWorld` `Matrix4x4`. Rewritten every frame by prepare. |
| `CombatRenderActiveTag` | enableable tag | Gate: is entity rendered this frame. Toggled in lockstep with `Active`. |
| `CombatRenderBatchId` | `IComponentData` | Per-entity render-resource id copied from spawn `RenderTypeId`; used only to select the GPU resource batch at submit. |
| `CombatRenderResourceRegistry` | managed singleton | Holds GPU mesh/material/props keyed by render id. |
| `CombatRenderMatrixUtility` | static | Builds matrix from kinematics + render params. |

## Data flow (who writes, who reads)

```
CombatRoot (main thread)
  └─ CombatRenderResourceRegistry.Register()  → mint renderId, build GPU mesh/mat
  └─ GetProjectile/AoeRenderComponent()        → bakes CombatRenderComponent into spawn template
        │
        ▼  (SIMULATION group)
Projectile/AoeSpawnApplySystem
  • create archetype w/ render comps
  • SetComponent CombatRenderComponent  (from template)
  • SetComponent CombatRenderBatchId = cmd.RenderTypeId
  • reset CombatRenderElement
  • enable CombatRenderActiveTag
        │
CombatLifetimeSystem / ProjectileCollisionSystem / Impact+LingeringAoeCollisionSystem
  • on despawn: disable Active + CombatRenderActiveTag together
        │
        ▼  (PRESENTATION group)
CombatRenderPrepareSystem  [OrderFirst]
  • reads  CombatKinematicsComponent + CombatRenderComponent
  • writes CombatRenderElement   (= the matrix)   ← ONLY sim-coupling read
        │
CombatBatchedRenderSystem
  • CompleteDependency()  (waits on prepare job + all sim jobs)
  • clear/reuse per-batch matrix buffers
  • scatter active ProjectileTag/AoeTag chunks by CombatRenderBatchId
  • Graphics.RenderMeshInstanced for each non-empty registered batch
```

## Key dependency facts

1. **The only read from ECS-simulation data into rendering is
   `CombatKinematicsComponent`.**
   [CombatRenderPrepareSystem](../../Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs) reads
   `Position` + `Velocity`. Everything else render needs (`CombatRenderComponent`)
   is render-owned and frozen at spawn. That is the whole coupling surface
   between sim and render.

2. **Render never touches gameplay/faction/damage components.** Domain split
   comes from `ProjectileTag`/`AoeTag` (queries in
   [CombatBatchedRenderSystem](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs)); batch
   grouping comes from per-entity `CombatRenderBatchId` (= `RenderTypeId`). Matches the
   restriction in [render-batch-data.md](../../Docs/contracts/render-batch-data.md).

3. **Render lifecycle rides on the domain, it does not own it.**
   `CombatRenderActiveTag` is always flipped alongside `Active` by simulation
   systems ([CombatLifetimeSystem](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs),
   collision systems). Render has no despawn logic of its own.

4. **System ordering is the cross-group contract:**
   - **Simulation group** writes kinematics + toggles the tags.
   - **Presentation group**: `CombatRenderPrepareSystem` (OrderFirst) →
     `CombatApplyBridge` (`UpdateBefore` render) → `CombatBatchedRenderSystem` →
     `CombatStatsGatherSystem` (`UpdateAfter` render).
   - Prepare job is scheduled parallel off `Dependency`; submit calls
     `CompleteDependency()` to sync before reading elements on the main thread.
     So prepare→submit is a job-dependency handoff, not an explicit `UpdateBefore`.

## Producers / writers (by system)

- **CombatRoot** — builds registry, `Register()` mints render ids + GPU
  resources; `GetProjectile/AoeRenderComponent()` bakes the
  `CombatRenderComponent` snapshot into spawn commands/templates.
- **ProjectileSpawnApplySystem / AoeSpawnApplySystem** (Simulation) — at spawn:
  build archetype, set `CombatRenderComponent`, set `CombatRenderBatchId`,
  reset `CombatRenderElement`, enable `CombatRenderActiveTag`.
- **CombatRenderPrepareSystem** (Presentation, OrderFirst) — writes
  `CombatRenderElement` from `CombatKinematicsComponent` + `CombatRenderComponent`.
- **CombatLifetimeSystem, ProjectileCollisionSystem, Impact/LingeringAoeCollisionSystem**
  (Simulation) — disable `CombatRenderActiveTag` with `Active` on expiry/despawn.

## Consumers / readers

- **CombatBatchedRenderSystem** (Presentation) — reads `CombatRenderElement`
  and `CombatRenderBatchId` (gated by `CombatRenderActiveTag`, split by
  `ProjectileTag`/`AoeTag`), scatters matrices into registered batch buffers,
  looks up GPU resources in the registry, and issues `Graphics.RenderMeshInstanced`.

## Rework-relevant state

Current on-disk code has the batch-id/spawn-pool coupling removed:
`CombatRenderBatchId` is plain component data, spawn reuse overwrites it, and
`CombatBatchedRenderSystem` scatters prepared matrices into reused per-batch
buffers by reading that int.

- **Prepare/submit split remains** — `CombatRenderPrepareSystem` runs
  `PresentationSystemGroup` OrderFirst; submit is consume-only and waits with
  `CompleteDependency()` before main-thread scatter.
- **Deferred follow-ups** — parallel count/prefix-sum/scatter, dropping
  `CombatRenderElement`, and removing degenerate projectile counting-sort.
- **Two `CombatApplyBridge` classes still exist** —
  [CombatApplyFinalizeSingleSystem.cs:382](../../Assets/Scripts/System/Common/CombatApplyFinalizeSingleSystem.cs#L382)
  and [CombatApplyFinalizeSystem.cs:415](../../Assets/Scripts/System/Common/CombatApplyFinalizeSystem.cs#L415),
  both `UpdateBefore(CombatBatchedRenderSystem)`. Confirm which is live. This
  duplication is a plausible suspect for the spawn/render breakage above.
