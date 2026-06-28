---
name: faction-overhaul-plan
description: Plan to make faction a plain component field on one unified CombatRoot/world, with collision + target acquisition skipping same-faction targets and spawn reuse merging across factions
---

# Faction Overhaul — Implementation Plan

## High-Level Summary

Collapse the two per-faction `CombatRoot` instances (player root + mob root) into
**one faction-agnostic `CombatRoot`** sharing the **one already-shared ECS world and
scope**. Faction stops being a property of a root and a spatial-hash key; it becomes
purely a **field on the entity** (already present on `ProjectileIdentityComponent`,
`AoeIdentityComponent`, `TargetFaction`) that callers supply per spawn and that
collision/target-acquisition read to **ignore same-faction targets**.

Five concrete shifts:

1. **`TargetFaction` flips meaning** — from "the firing faction allowed to hit me"
   to "my own allegiance." A target registers once with its own faction.
2. **Collision + target acquisition use a unified spatial hash** (cell-only key) and
   skip a candidate when `candidate.Faction == self.Faction`. (Faction-granular keys
   are a deferred optimization, per request.)
3. **Render batch id drops its faction namespacing** (`(faction<<16)|renderId` →
   `renderId`), which both removes the managed faction coupling and **lets spawn reuse
   merge player/mob dead slots of the same render id**.
4. **Spawn reuse reads faction per command** (from the expansion-stamped
   `cmd.Faction`) instead of per reuse-bucket, so a mixed-faction bucket is correct.
5. **One `CombatRoot`**: no `faction` field, no `ByFaction[]` static; the spawn API
   takes a `CombatFaction` argument; managed callers supply their own faction.

The ECS world, scope, ref-counted ownership, spawn-template registry, expansion,
apply structure, hit aggregation, status, and render systems are **reused unchanged
in shape**; only the faction-as-key usages are removed.

## Rationale for Major Decisions

- **One root, faction per spawn (not per root).** The request is explicit: "1 world
  and 1 root for both player and mobs … faction should simply be a field in a
  component." The world and scope are *already* shared and ref-counted
  ([CombatEcsComponents.cs:11-159](../../Assets/Scripts/System/Common/CombatEcsComponents.cs#L11-L159)),
  so the only thing the two roots still carried separately was a `faction` field and
  a target registry. Removing the field and threading faction through the spawn call
  is the minimal change that yields a single root.
- **`TargetFaction` = own faction.** Today a mob proxy is stamped `Player` because it
  was registered through the *player* root (firing-faction model,
  [CombatRoot.cs:784](../../Assets/Scripts/System/Common/CombatRoot.cs#L784)). With one
  registry serving both sides this is ambiguous. Making it the target's own allegiance
  gives a single source of truth and matches "ignore targets with the same faction."
- **Unified hash + per-candidate skip (defer faction-granular keys).** The request
  says the spatial hash "can be more granular but focus on structural changes … this
  optimization can be done later." The structural baseline is therefore a cell-only
  hash with a faction inequality test in the narrow phase; re-adding faction (or
  faction×region) to the key is a later perf pass.
- **Drop faction from the render batch id to unlock cross-faction reuse.** Reuse pools
  are partitioned by the `CombatRenderBatchId` shared component
  ([ProjectileSpawnApplySystem.cs:206](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L206)).
  With one root the render-id space is already globally unique (one `nextRenderId`
  counter), so faction in the key is redundant; removing it satisfies "spawn reuse
  should be able to reuse any entity with the same archetype regardless of faction"
  for entities that share a visual (the only case where reuse is physically possible,
  since differing visuals live in different chunks).

## Constraints & Invariants The Change Must Respect

| # | Invariant | Source |
|---|---|---|
| I1 | `CombatFaction.None=0` is the sentinel for unbound/never-spawned; collision/dispatch **deactivate** entities whose faction is `None`. Spawned entities must always get a real faction. | [CombatScope.cs:17-22](../../Assets/Scripts/System/Common/CombatScope.cs#L17-L22), [ProjectileCollisionSystem.cs:185](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs#L185), [AoeCollisionCore.cs:94](../../Assets/Scripts/System/Aoe/AoeCollisionCore.cs#L94) |
| I2 | Per-instance faction is stamped onto the command from the event at expansion (single-threaded `Stamp`); this is the only authoritative per-entity faction at apply time. | [ProjectileSpawnExpansionSystem.cs:220-232](../../Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs#L220-L232) |
| I3 | The spawn-template registry is faction-agnostic (templates normalized to `None`) and is read-only/immutable during the tick. | [CombatRoot.cs:554-591](../../Assets/Scripts/System/Common/CombatRoot.cs#L554-L591), [spawn-events-and-commands.md](../../Docs/contracts/spawn-events-and-commands.md) |
| I4 | `CombatRenderBatchId` is an `ISharedComponentData` that partitions chunks; `SetSharedComponentFilter` is the reuse-pool and render-submit filter. Changing the key changes chunk grouping. | [CombatRenderComponents.cs:34-39](../../Assets/Scripts/System/Common/CombatRenderComponents.cs#L34-L39), [CombatBatchedRenderSystem.cs:60-75](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L60-L75) |
| I5 | The render system reads only the ECS `CombatRenderResourceRegistry` singleton (no `CombatRoot` ref at runtime). The registry key must equal the batch id. | [CombatBatchedRenderSystem.cs:49-75](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L49-L75) |
| I6 | World + scope are ref-counted; first binder creates, last releases. One root means count goes 0→1→0 — still valid. | [CombatEcsComponents.cs:11-159](../../Assets/Scripts/System/Common/CombatEcsComponents.cs#L11-L159) |
| I7 | Structural ops (AddSharedComponent / create / destroy) run main-thread via ECB; archetype shape is stable post-creation. | [ecs-notes.md](../../Docs/reference/simulation/ecs-notes.md), apply systems |
| I8 | Collision/tracking read target component arrays read-only after `CompleteDependencyBeforeRO<TargetFaction>()`; `TargetFaction` is already queried by every collision/tracking system. | [ProjectileCollisionSystem.cs:55-60](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs#L55-L60), [LingeringAoeCollisionSystem.cs:55-60](../../Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs#L55-L60), [ProjectileTrackingSystem.cs:34-39](../../Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs#L34-L39) |
| I9 | `TargetFaction` is set once at proxy creation and lives on the proxy archetype. | [CombatTargetProxy.cs:63-85](../../Assets/Scripts/System/Common/CombatTargetProxy.cs#L63-L85) |
| I10 | Performance is a primary constraint; combat must scale to many projectiles/AOEs. | [project-overview.md:16-21](../../Docs/project-overview.md#L16-L21) |

## Mechanisms Reused vs. Introduced

**Reused (conform, do not duplicate):**
- Faction-as-field already exists on `ProjectileIdentityComponent`,
  `AoeIdentityComponent`, `TargetFaction`, `ProjectileSpawnEvent/Command`,
  `AoeSpawnEvent/Command`, `StackEffectSnapshot`. We keep these and only change *how*
  faction is read (per-entity field, not a hash key).
- The `CombatRenderBatchId` + `CombatRenderResourceRegistry` decouple (already landed,
  see [decouple-render-id-from-faction](../decouple-render-id-from-faction/index.md))
  is reused; we only drop the faction bits from the id.
- Expansion's per-command faction stamp (I2) is reused as the reuse-time faction
  source — no new plumbing.
- Ref-counted world/scope ownership is reused as-is; one owner instead of two.

**Introduced (minimal):**
- `ICombatTarget.CombatFaction { get; }` (default `None`) so a target reports its own
  allegiance to the registry. Replaces the registry-wide `faction` field — not a
  second representation.
- A `CombatFaction faction` parameter on the public spawn API (`Spawn(...)`,
  `SpawnRegisteredProjectile/Aoe`). Replaces the per-root `faction` field — not a
  parallel path.

No new component types, no new data path, no adapter/shim, no parallel old/new system.

## Additive vs. Refactor Comparison

**Additive** (keep two roots, translate): keeps `faction` on each root, keeps
firing-faction `TargetFaction`, keeps faction-keyed hashes, and bolts on cross-faction
reuse via a second key — two roots that must stay in sync, two registries, and a
`TargetFaction` whose meaning depends on which root registered it. Long-term cost:
duplicate ownership of "who can hit whom," unclear source of truth, no single-root.

**Refactor (chosen):** one root; faction is a per-entity field supplied at spawn;
`TargetFaction` = own allegiance; collision derives "can hit" from faction inequality;
one render-id space; reuse merges across faction. Removes the two-root duplication, the
firing-faction ambiguity, and the faction key indirection. Data flow becomes: caller
→ `Spawn(request, faction)` → event.Faction → command.Faction (stamped) →
identity.Faction → collision skip vs. `TargetFaction`.

**Decision: refactor.** Two representations of "faction"/"who can hit whom" describe
one domain concept; the request mandates a single root and faction-as-field. There is
no migration/compat reason to keep the additive form.

**Default decision rule applied:** `TargetFaction` had two meanings (firing-faction vs.
own-faction); we converge on one source of truth — every combat entity's faction is its
own allegiance, and targeting is `self.Faction != other.Faction`.

**Governing principle (per directive):** do not default to additive abstraction-preserving
changes. Each decision below compares an additive option against a data-path-collapsing
option and prefers fewer runtime data paths, fewer copies, clearer ownership, and better
cache/job behavior — even when that means editing existing types. No new *type* is
introduced anywhere in this plan: `ICombatTarget.CombatFaction` is a member on an existing
interface (it deletes the registry-wide `faction` field and the firing-faction stamp), and
the spawn `CombatFaction` argument deletes the per-root `faction` field. Both *remove* a
representation rather than hide one.

### Friendly-fire filter: collapse two gates into one field
Static reading shows target mask is **not** enforced at ECS collision today:
`ProjectileSpawnCommand`/`AoeSpawnCommand` carry no mask (no `Mask` token in
[SpawnTemplateComponents.cs](../../Assets/Scripts/System/Common/SpawnTemplateComponents.cs)),
the command builders drop `request.TargetMask`
([CombatRoot.cs:424-509](../../Assets/Scripts/System/Common/CombatRoot.cs#L424-L509)), and
neither collision job reads `TargetCollisionShape.Mask`. The only live "who can be hit"
gate is the per-root `CanTarget` (targetLayers/targetTag) at proxy creation
([CombatRoot.cs:197-210](../../Assets/Scripts/System/Common/CombatRoot.cs#L197-L210),
[CombatTargetRegistry.cs:62-81](../../Assets/Scripts/System/Common/CombatTargetRegistry.cs#L62-L81)):
two roots, each with a layer mask that only admits the *other* side's proxies. The
faction-keyed hash was a second, redundant gate on top.
**Decision:** collapse both gates (per-root `CanTarget` layer/tag + faction-keyed hash)
into the single per-entity faction-inequality test. The `CanTarget`/targetLayers/targetTag
targeting role is removed (not kept "inert"); see open questions for the now-unenforced
request `TargetMask` and its tests.

### Spatial hash: additive (faction-keyed) vs. collapse (unified key)
- **Faction-keyed, own-faction semantics (deferred optimization):** keep `CellKey(faction,
  cell)`, register each target under its own faction, and have a shot query only the
  buckets for factions `!= self`. No perf regression; but the cell key keeps a faction
  dimension and every reader must enumerate the faction set — more coupling, not a collapse.
- **Unified cell-only key + per-candidate faction skip (chosen):** one hash, one key
  dimension, readers need no faction set; a shot scans a cell's targets and skips
  same-faction. Fewer data paths and clearer ownership.
- **Cache/job analysis:** the regression of the unified key is bounded by the count of
  *same-faction* targets in a cell. The dominant workload is the player's projectiles
  ("extreme attack scaling," [project-overview.md:16-21](../../Docs/project-overview.md#L16-L21)),
  whose same-faction set is just the player (≈+1 candidate) — negligible. The only adverse
  case is *mob* projectiles inside a mob crowd, the minority path.
- **Decision:** unified key now (collapse + clarity, negligible cost on the hot path);
  faction- (or faction×region-) granular keys remain the named follow-up the request
  deferred, to recover locality for the mob-vs-crowd case.

## Design Validation Against Invariants

- **I1 (None sentinel):** Managed callers always pass a real faction
  (`Player`/`Mob`); the spawn API will not default to `None`. Targets default to
  `None` only for unconfigured test doubles (treated as hittable-by-all, harmless).
  Collision still deactivates `None`-faction entities. ✔
- **I2 (per-command faction):** Reuse/cold paths switch from a per-bucket `Faction`
  to `cmd.Faction`/`cfg.Faction`, which is exactly the expansion-stamped value. A
  bucket may now mix factions; each element carries its own. ✔
- **I3 (registry faction-agnostic):** Templates already normalize faction to `None`;
  we additionally normalize `StackEffect.Faction` to `None` so dedupe is faction-free.
  Apply re-stamps the real faction. ✔
- **I4/I5 (batch id = chunk + registry key):** `batchId = renderId` is unique per
  visual within the single root's render space; registry publishes under `renderId`;
  apply filters by `renderId`. Chunk count drops (player+mob of the same visual merge)
  — intended. ✔
- **I6 (ref-count):** One acquire/one release; `ownerCount` 0→1→0. ✔
- **I7 (structural/ECB):** Cold-create still adds `CombatRenderBatchId` once at
  create; no archetype churn. ✔
- **I8/I9 (target reads):** Collision/tracking already complete `TargetFaction` RO and
  query it; we pass the existing array into the job and read the per-candidate value.
  `TargetFaction` is still written once at create. ✔
- **I10 (perf):** Unified hash adds same-faction iterations (e.g., a mob projectile
  scanning mobs before reaching the player). Accepted per the explicit deferral of the
  granularity optimization; re-adding a faction/region key is a follow-up. ✔

## Task List

| # | Task | Files | Depends on |
|---|---|---|---|
| [001](001-target-faction-own-allegiance.md) | `TargetFaction` = target's own faction | `ICombatTarget.cs`, `CombatTargetRegistry.cs`, `CombatTargetProxy.cs`, `PlayerRoot.cs`, `MobRoot.cs` | — |
| [002](002-projectile-collision-faction-skip.md) | Projectile collision: unified hash + same-faction skip | `ProjectileCollisionSystem.cs` | 001 |
| [003](003-aoe-collision-faction-skip.md) | AOE collision: unified hash + same-faction skip | `AoeCollisionCore.cs`, `LingeringAoeCollisionSystem.cs`, `ImpactAoeCollisionSystem.cs` | 001 |
| [004](004-projectile-tracking-faction-skip.md) | Projectile target acquisition: unified hash + same-faction skip | `ProjectileTrackingSystem.cs` | 001 |
| [005](005-faction-free-render-batch-and-reuse.md) | Faction-free render batch id + cross-faction reuse | `CombatRoot.cs` (render registry), `ProjectileSpawnApplySystem.cs`, `AoeSpawnApplySystem.cs` | — |
| [006](006-combatroot-faction-per-spawn.md) | One `CombatRoot`: faction-per-spawn API, drop faction field/static/tag-defaults | `CombatRoot.cs` | 005 |
| [007](007-managed-wiring-single-root.md) | Wire scene/managed callers to one root + own faction | `GameRoot.cs`, `MobRoot.cs`, `MobSpawnerRoot.cs`, `PlayerSkillDriver.cs`, `SkillSpawnTranslator.cs`, `MobProjectileAttack.cs`, scene | 006 |
| [008](008-tests-and-docs.md) | Flip test target factions + single-root setup; update docs | `Assets/Tests/PlayMode/*`, `Docs/*` | 001–007 |

**Landing order:** 001 → (002, 003, 004 in any order) → 005 → 006 → 007 → 008.
001 is required before the collision/tracking skips are meaningful. 005 + 006 should be
verified together (intermediate after 005 alone still compiles because the apply key is
changed within 005). 007 needs the 006 spawn API. 008 last.

## Open Questions / Dependencies / Considerations

- **Scene migration (manual).** `GameRoot`, `MobSpawnerRoot`, etc. serialize two
  `CombatRoot` refs and the scene has two `CombatRoot` GameObjects
  ([GameRoot.cs:13-14](../../Assets/Scripts/Game/GameRoot.cs#L13-L14),
  [BenchmarkLarge.unity]). After 006/007 these collapse to one ref / one GameObject.
  This requires a Unity scene edit that cannot be done purely in code; 007 flags it.
- **VFX roots stay faction-keyed and out of scope.** `CombatVfxRoot` is a separate
  structure ([CombatVfxDispatchSystem.cs](../../Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs))
  that routes by `VfxSpawnRequestElement.Faction` (still correctly flowing from
  `identity.Faction`). The only code touch is replacing `combatRoot.Faction` with the
  literal `CombatFaction.Player` in `PlayerSkillDriver`'s `vfxRoot.BindFaction(...)`.
- **`StackEffectSnapshot.Faction`** is built from `root.Faction` in
  `SkillIntervalTemplateBuilder` ([PlayerSkillDriver.cs:786,813](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L786))
  but is **overwritten at apply** by the spawning entity's faction
  ([ProjectileSpawnApplySystem.cs:278-285](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L278-L285)),
  so it is set to `None` in the template (07) — no behavior change.
- **`CombatRoot.CanTarget`/targetLayers/targetTag** are the *removed* friendly-fire gate
  (collapsed into faction); 006 deletes their targeting role rather than leaving them
  inert.
- **Open question — request `TargetMask`:** static reading says it is already not
  enforced at collision (no mask on commands/entities; collision never reads
  `TargetCollisionShape.Mask`), yet `ProjectileTargetMaskFiltersHits`
  ([BareMinimumPrototypePlayModeTests.cs:174](../../Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs#L174))
  and the AoE mask tests assert filtering. **Resolve in 008 by running those tests first:**
  if they were exercising the now-removed `CanTarget` gate, convert them to faction-skip
  tests and delete the dead `TargetMask` from `ProjectileSpawnRequest`/`AoeConfig`
  (data-path collapse). If mask is wanted as an orthogonal sub-target filter, that is a
  *separate* feature from faction and must be restored explicitly at collision — not left
  as silent dead state. Do not keep `TargetMask` inert.
- **Deferred:** faction- (or faction×region-) granular spatial hash keys for collision
  and acquisition, to recover the locality the unified hash gives up (I10).
