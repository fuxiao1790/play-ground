---
name: vfx-faction-scope-removal
description: Plan to remove faction routing and CombatScope coupling from the VFX system
---

# VFX Faction + Scope Removal

## Summary

Remove two couplings from the VFX system so it behaves like sprite rendering —
visual-only, faction-agnostic, and not tied to the gameplay `CombatScope`
entity:

1. **Faction removal.** Drop `CombatFaction Faction` from `VfxPendingSpawn` and
   `VfxSpawnRequestElement`, drop the faction assignment at every VFX emitter,
   drop the `Faction == None` guard in the flush jobs, and collapse the
   faction-keyed `CombatVfxRoot` registry into a single static instance. The
   dispatch system stops splitting requests into player/mob lists.
2. **Scope removal.** Move the `DynamicBuffer<VfxSpawnRequestElement>` staging
   buffer off the shared `CombatScope` entity onto a dedicated VFX singleton
   entity owned by `CombatVfxDispatchSystem` (mirroring how
   `CombatBatchedRenderSystem` owns its `CombatRenderResourceRegistry`
   singleton). Producers resolve that entity via a `VfxSingleton` tag instead of
   `CombatScope`.

After the change the VFX path no longer reads faction and no longer touches
`CombatScope`; it is a self-contained presentation concern.

## Rationale

- The user's framing: "scope was used as faction, and VFX should not be
  concerned with faction — similar to sprite rendering." Sprite rendering
  ([CombatBatchedRenderSystem.cs](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs))
  iterates all renderable entities without faction split and owns a single
  managed resource registry resolved through a dedicated singleton entity. VFX
  should match that shape.
- Faction routing in VFX is already effectively dead: `MobProjectileAttack` is
  constructed by `MobRoot` with no `vfxRoot`
  ([MobRoot.cs:495-506](../../Assets/Scripts/Mob/MobRoot.cs#L495-L506)), so it
  never registers VFX and never calls `BindFaction`. `TryGetByFaction(Mob, …)`
  always returns false today; all live VFX already runs through the single
  player-bound root. Removing the split deletes an unused code path rather than
  changing behavior in the common case.
- `CombatScope` is a gameplay-spawn concept (projectile/AOE spawn events +
  template registries). The VFX staging buffer rode along on it for no reason
  other than convenience. Decoupling removes a cross-concern dependency and lets
  VFX own its own lifecycle.

## Constraints & invariants the change must respect

- **VFX is visual-only; dropping requests is allowed.** Capping/dropping after a
  budget never affects gameplay authority.
  Source: [vfx-system.md](../../Docs/reference/simulation/vfx-system.md) "Performance Notes",
  [vfx-requests.md](../../Docs/contracts/vfx-requests.md) "Guarantees".
- **Simulation jobs must not call managed VFX objects.** Producers only write
  plain-data requests into native queues/streams; managed dispatch happens on
  the main thread in `PresentationSystemGroup`.
  Source: [vfx-requests.md](../../Docs/contracts/vfx-requests.md) "Restrictions",
  [vfx-system.md](../../Docs/reference/simulation/vfx-system.md) "Non-goals".
- **Flush ordering: producers (SimulationSystemGroup) → flush job → buffer →
  dispatch (PresentationSystemGroup) drains and clears.** The buffer is written
  by Burst flush jobs and by `AoeSpawnExpansionSystem` (main thread), and drained
  + cleared once per frame by `CombatVfxDispatchSystem`.
  Source: [vfx-dispatch.md](../../Docs/flows/vfx-dispatch.md) "Sequence".
- **Singleton resolution must exist before producers run.** Producers resolve
  the VFX entity during `SimulationSystemGroup` update. Whatever creates that
  entity must run in `OnCreate` (all systems' `OnCreate` complete before any
  `OnUpdate`). The render registry uses exactly this guarantee
  ([CombatBatchedRenderSystem.cs:28-31](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L28-L31)).
- **Faction stays on gameplay events.** `identity.Faction`, `AoeSpawnEvent.Faction`,
  `ProjectileSpawnEvent.Faction`, `CombatHitEvent`, and the gameplay
  `identity.Faction == None` early-outs in collision jobs are gameplay authority
  and must NOT be touched. Only the faction field on the two *VFX request* types
  is removed.
- **Single `CombatRoot` — typeIds are globally unique (governing invariant).**
  There must be exactly one `CombatRoot`; player and mob register their templates
  on it, so `(typeId, trigger)` keys never collide across factions. This is a
  project decision (user directive) and the target of the
  [faction-overhaul](../../../memory/project_faction_overhaul.md) consolidation.
  The VFX change is designed against this invariant: with one root there is no
  cross-faction VFX collision to route around, which is precisely why a single
  faction-agnostic dispatcher is correct. Today two tagged roots still exist
  (`PlayerProjectileRoot`, `MobProjectileRoot`); see ordering below.
- **No native leaks.** The staging buffer is a `DynamicBuffer` owned by the
  `EntityManager`; moving it to another entity keeps ECS ownership and needs no
  manual disposal. No new `NativeContainer` is introduced.

## Mechanisms reused vs introduced

- **Reused — presentation-owned singleton entity.** `CombatBatchedRenderSystem`
  already creates a dedicated singleton entity in `OnCreate` to hold its managed
  presentation resource and resolves it without faction routing. The VFX
  dispatch system adopts the same pattern for the staging-buffer entity. No new
  architectural concept.
- **Reused — `GetSingletonEntity<Tag>()` producer resolution.** Producers
  currently resolve the buffer host with `SystemAPI.GetSingletonEntity<CombatScope>()`.
  They switch to `SystemAPI.GetSingletonEntity<VfxSingleton>()` — identical call
  shape, different tag.
- **Reused — `CombatVfxDispatcher` / `DrainAndDispatch` / `StageSpawn`.**
  Unchanged. `DrainAndDispatch(NativeArray<VfxSpawnRequestElement>)` keeps its
  signature; it is now fed the whole buffer instead of a per-faction slice.
- **Introduced — `VfxSingleton : IComponentData` tag.** One zero-size marker
  component, the singleton key for the VFX staging entity. Justified below: it
  replaces `CombatScope` as the resolution key in the exact same call pattern
  and matches the render-registry precedent (a dedicated marker, not a piggyback
  on an unrelated gameplay component).

## Design validation

- *Visual-only / drop-allowed*: unchanged — caps and the `StageSpawn`
  `TryGetValue` drop for unregistered `(typeId, trigger)` pairs still hold.
- *No managed calls from jobs*: unchanged — flush jobs still only append
  plain-data elements; dispatch still happens on the main thread.
- *Flush ordering*: unchanged — same producer→flush→buffer→dispatch sequence,
  same single drain+clear in presentation. The buffer simply lives on a
  different entity.
- *Singleton exists before producers*: satisfied — `CombatVfxDispatchSystem.OnCreate`
  creates the `VfxSingleton` entity, and `OnCreate` precedes all `OnUpdate`s in
  the default world (same guarantee the render registry relies on). Tests that
  exclude the dispatch system create the entity in setup, exactly as they create
  the scope+buffer today.
- *Faction stays on gameplay events*: satisfied — only the VFX request writes
  drop `Faction`; gameplay event writes (AOE/projectile spawn events, hit events,
  `identity.Faction` guards) are untouched.

## Minimal/additive vs. refactor comparison

- **Minimal/additive approach** — keep the faction field and the per-faction
  `ByFaction[]` registry, keep the VFX buffer on `CombatScope`, just stop binding
  the mob faction.
  - resulting data flow: unchanged dual-path (faction split in dispatch,
    scope-coupled buffer).
  - new concepts/types introduced: none.
  - copies/translations added: none removed either — keeps the per-frame
    player/mob temp `NativeList` split copy in dispatch.
  - long-term cost: keeps a dead faction dimension and a cross-concern coupling
    to `CombatScope`; contradicts the "VFX like sprite rendering" target; the
    faction field stays a trap for future emitters to set.
- **Refactor approach** — remove the faction field, collapse to one root, move
  the buffer to a dedicated VFX singleton entity.
  - resulting data flow: single non-faction path; buffer owned by VFX; dispatch
    drains the buffer directly with no per-faction copy.
  - existing concepts/types changed or removed: `Faction` field removed from two
    request types; `ByFaction[]` / `BindFaction` / `TryGetByFaction` removed;
    `AddBuffer<VfxSpawnRequestElement>` removed from `CombatScopeOwner`.
  - copies/translations removed: the per-frame player/mob `NativeList` split in
    `CombatVfxDispatchSystem` (two `Allocator.Temp` lists + element copy per
    frame) is removed — the whole buffer is dispatched in place.
  - long-term benefit: VFX is a self-contained presentation concern with one
    source of truth; aligns with the planned single-`CombatRoot` unification
    ([project_faction_overhaul](../../../memory/project_faction_overhaul.md)).
- **Decision:** choose refactor.
  - reason: the additive option leaves a dead concept (faction) and a
    cross-concern coupling (scope) in place, directly contradicting the stated
    goal. The refactor removes a data path and a per-frame copy with no new
    runtime concept beyond a one-field marker tag.

## Default decision rule applied

Faction in VFX and faction in gameplay describe the same domain concept but VFX
does not need it; rather than maintain a second (unused) faction dimension, we
collapse VFX to a single faction-agnostic path — one source of truth, with
gameplay keeping faction where it has authority.

## Risks / open considerations

- **TypeId uniqueness depends on the single-`CombatRoot` invariant.** Under one
  root (the governing invariant above) `(typeId, trigger)` keys are globally
  unique, so a faction-agnostic dispatcher has no collision to route around —
  this is the correct end state and carries no risk. The only exposure is
  *ordering*: if this VFX change lands while two roots still exist, a mob request
  could share a key with a *registered* player typeId. Even then mobs register no
  VFX, so mob requests are dropped by `StageSpawn`'s `TryGetValue`; the only
  visible effect would be a mob hit rendering a player effect on a key clash —
  visual-only, no gameplay impact. Mitigation: land this with or after the
  single-`CombatRoot` consolidation; the transient window is benign regardless.
- **Factionless deactivation VFX.** A projectile with `identity.Faction == None`
  is force-deactivated in collision and emits a trigger-2 request; today the
  flush guard drops it, after removal it is staged (and dropped only if its
  typeId is unregistered). Practically unreachable — spawned projectiles always
  carry a real faction — and visual-only. Negligible.
- **Single `CombatVfxRoot` in scene.** The collapse to `static Instance` assumes
  one root. True in practice (only the player driver wires one; mobs pass null).
  If a scene ever has two, last-`Awake` wins; acceptable for a visual singleton
  and consistent with the render registry being a singleton.

## Task list

1. [001-strip-faction-from-vfx-requests.md](001-strip-faction-from-vfx-requests.md)
   — remove `Faction` from the two VFX request types, the 7 emitter writes, and
   the flush-job faction guard.
2. [002-collapse-vfx-root-and-dispatch.md](002-collapse-vfx-root-and-dispatch.md)
   — single static `CombatVfxRoot.Instance`; dispatch drains the whole buffer;
   drop `BindFaction` calls.
3. [003-move-vfx-buffer-off-scope.md](003-move-vfx-buffer-off-scope.md)
   — add `VfxSingleton` tag, create the VFX entity in `CombatVfxDispatchSystem.OnCreate`,
   remove the scope buffer, repoint all producers.
4. [004-update-tests.md](004-update-tests.md)
   — create the VFX singleton entity in the 4 play-mode test setups.
5. [005-update-docs.md](005-update-docs.md)
   — update the VFX reference, contract, flow, and layer docs.

## Dependencies / ordering

- 001 and 002 together form one compile unit for "faction removal" (the field
  removal forces both the emitter and the dispatch edits).
- 003 layers on top (buffer ownership) and can compile only after producers are
  repointed in the same change.
- 004 must land with 001-003 (the build won't pass otherwise).
- 005 is documentation; land alongside or immediately after.
- 002 and 003 both edit `CombatVfxDispatchSystem` in non-overlapping regions
  (002: the `OnUpdate` drain; 003: the `OnCreate` entity source + query).
- Preferably land this with or after the single-`CombatRoot` consolidation
  ([faction-overhaul](../../../memory/project_faction_overhaul.md)) so typeIds are
  globally unique. Not a hard blocker — the pre-consolidation window is benign
  (see Risks) — but it is the clean ordering.
