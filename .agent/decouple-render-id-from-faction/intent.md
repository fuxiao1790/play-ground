# Plan: Decouple Render Resources from Faction

## Motivation

Render entities carry two shared components, `CombatRenderFaction` and
`CombatRenderTypeId` ([CombatRenderComponents.cs:34,42](../../Assets/Scripts/System/Common/CombatRenderComponents.cs#L34-L47)).
Faction is in the render/batch key only because **`TypeId` is unique per root, not
globally**: each `CombatRoot` has its own `nextTypeId` restarting at 1
([CombatRoot.cs:65](../../Assets/Scripts/System/Common/CombatRoot.cs#L65)) and its
own resource dictionaries ([CombatRoot.cs:54,59](../../Assets/Scripts/System/Common/CombatRoot.cs#L54-L59)),
so player `typeId 1` and mob `typeId 1` are different visuals. The renderer also
pulls **layer and bounds from the root** ([CombatBatchedRenderSystem.cs:66-69](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L66-L69)).

Goal: faction should be a **collision / damage** concern only (it already lives on
`AoeIdentityComponent` / `ProjectileIdentityComponent`). Rendering should key on a
self-contained visual identity, and `CombatRenderFaction` should be deleted from
render entities.

## Approach

Make the render identity globally unique and self-describing, collapsing the two
shared components into one.

1. **Single global render id.** Replace `(CombatRenderFaction, CombatRenderTypeId)`
   with one shared `CombatRenderBatchId { int Value }`. Use the namespacing scheme
   `batchId = ((int)faction << 16) | typeId` — deterministic, no stateful allocator,
   stable across root teardown/rebuild.
2. **ECS-owned resource registry.** Create a managed `IComponentData` class
   `CombatRenderResourceRegistry` holding a
   `Dictionary<int, CombatRenderResourceEntry>` where each entry carries
   `{ CombatSpriteRenderResources Resources, int Layer, float BoundsHalfExtent }`.
   Store it on a singleton entity. `CombatRoot` adds/updates entries on registration
   and removes them on teardown. The render system reads the singleton — no
   dependency on `CombatRoot` or any MonoBehaviour at runtime.
   `Mesh`, `Material`, and `MaterialPropertyBlock` are managed objects and stay on
   the heap regardless; ECS ownership just ties their lifetime to the world and
   eliminates static state.
3. **Render system simplifies.** `CombatBatchedRenderSystem` drops the per-faction
   pass and the faction filter ([:66-88](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L66-L88));
   it iterates the registry entries on the singleton and filters the projectile/AOE
   query by `CombatRenderBatchId`, submitting with that entry's resources/layer/bounds.
4. **Spawn stamps the id.** `CombatRoot` already knows faction + typeId at spawn,
   so it resolves `batchId` where the render component is built
   ([AoeRenderComponentFor](../../Assets/Scripts/System/Common/CombatRoot.cs#L474-L486)
   and the projectile equivalent).
5. **Delete `CombatRenderFaction`.** Faction remains only on the identity
   components used by collision / damage.

## Touched files

- `CombatRenderComponents.cs` — remove `CombatRenderFaction`, replace
  `CombatRenderTypeId` with `CombatRenderBatchId`; add managed `CombatRenderResourceRegistry`
  (`IComponentData` class) and `CombatRenderResourceEntry` record.
- `CombatRoot.cs` — compute `batchId` via `(faction << 16) | typeId`; populate/remove
  entries on the singleton registry entity; stamp `batchId` at spawn.
- `CombatBatchedRenderSystem.cs` — drop `TryGetByFaction` and per-faction pass; read
  singleton registry; one shared filter per entry.
- `AoeSpawnApplySystem.cs` / `ProjectileSpawnApplySystem.cs` — archetype loses
  `CombatRenderFaction`; dead-slot reuse query keys on `CombatRenderBatchId` only;
  cold-create does one `AddSharedComponent` instead of two.
- `ProjectileSpawnPipelineTests.cs` / `BareMinimumPrototypePlayModeTests.cs` — update
  to construct `CombatRenderBatchId` instead of the two old components.

## Caveats (read before assuming this is the spawn-cost fix)

- **Does not merge player/mob pools or reduce batch count.** A player AOE and a mob
  AOE of "the same type" are genuinely different visuals → different batches and
  different reuse pools regardless. This collapses *two shared components into one*,
  not chunk cardinality. Wins: cleaner render loop, one fewer structural dimension
  on the archetype, faction off the render path.
- **Does not, by itself, fix the ~32 ms AOE spawn cost.** Same-key reuse should
  already work; this refactor doesn't change why reuse currently returns ~0. The
  reuse failure must be measured/fixed separately (see the spawn-reuse
  investigation; this is a *simplification* track, not that fix).
- **Id stability.** The namespacing scheme (`faction << 16 | typeId`) is
  deterministic — player and mob roots can register/teardown independently without
  coordinating a shared counter. Registry entries are removed on teardown and
  re-added on re-register with the same batchId.

## Relationship to other tasks

- Sibling cleanup to [aoe-gate-lingering-only](../aoe-gate-lingering-only/index.md).
- Both are downstream of resolving the spawn-reuse failure (the real cost). Do the
  reuse investigation first; these are hygiene/decoupling, not the perf fix.
- **Unblocks [render-rework](../render-rework/intent.md).** That task collapses
  `CombatRenderPrepareSystem` + `CombatBatchedRenderSystem` into a single system
  with per-batchId Burst gather jobs and a managed submission-only pass. It depends
  on `CombatRenderBatchId` and the singleton `CombatRenderResourceRegistry` that this
  task produces. Land this task first.

## Acceptance

- `CombatRenderFaction` no longer exists; render entities carry a single
  `CombatRenderBatchId`.
- Faction is referenced only by collision / damage / identity code paths.
- Player and mob visuals, layers, and bounds render identically to before.
- Spawn reuse still works (cold-create not regressed); reuse query keys on the
  single render id.
- `CombatBatchedRenderSystem` holds no reference to `CombatRoot` or any
  MonoBehaviour; it reads only the singleton registry entity.
