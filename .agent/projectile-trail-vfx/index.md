# Projectile Trail VFX — Implementation Plan

## Summary

Projectiles currently emit no VFX requests at all — `BasicAttackPrefab` (the
projectile authoring root) has no `VisualEffectAsset` slot, and `Docs/reference/simulation/projectile-system.md`
states outright: "Projectiles do not emit AOE VFX requests." This plan adds one
authored VFX slot, a **trail**, using the existing `LineSegment` data shape
(`Docs/contracts/vfx-requests.md`, `Docs/reference/simulation/vfx-system.md`).
Authoring mirrors the existing pattern used for AOE/Targeted VFX slots exactly:
a `VisualEffectAsset` + `VfxDataShape` selector pair, registered once through
`CombatVfxRoot.Register`, carried by id through the runtime definition and spawn
command, and consumed by an ECS system through `VfxEmit.EnqueueLineSegment`.

Emission point: `ProjectileMovementSystem`, which already integrates
`CombatKinematicsComponent.Position` once per frame for **both** the discrete
and continuous lanes (`Docs/reference/simulation/projectile-system.md` —
"Movement is shared data math"). Emission is **distance-gated, not
tick-gated**: the trail component tracks the world position of the last
emitted segment's end, and the job only enqueues a new `LineSegmentVfxEvent`
once the projectile has moved at least an authored `StepDistance` away from
that point, then advances the tracked point to the current position. Emitting
on every tick was rejected — at high frame rates or for slow projectiles it
floods the `LineSegment` queue with many near-zero-length segments per world
unit of travel, and segment density becomes a function of frame rate rather
than of the projectile's actual path, which is both wasteful (queue growth,
dispatch/upload cost) and visually unpredictable (segment spacing changes with
frame rate, not with authored intent). Distance-gating bounds emission to
"one segment per `StepDistance` of travel," independent of tick rate, the same
way Unity's built-in `TrailRenderer.minVertexDistance` bounds vertex emission
for MonoBehaviour-driven trails.

## Rationale For Major Architectural Decisions

- **Reuse `ProjectileMovementSystem`, not a new system.** It is the one place
  both lanes already share position integration. Adding a second system to walk
  the same entities a second time just to diff position would duplicate a query
  and a dependency chain that already exists here for no benefit.
- **One combined component, not a split ids/size pair.** AOE and Targeted VFX
  carry multiple effect slots (spawn/hit/expire/pulse/arming, or
  spawn/hit/expire/link/arming) so they justify a dedicated ids component plus a
  separate size component. Projectiles get exactly one optional effect, so
  `ProjectileTrailVfxComponent { int TrailId; float Width; float StepDistance;
  float2 LastEmitPosition; }` carries authored data (`TrailId`/`Width`/
  `StepDistance`) and the one piece of runtime emission state
  (`LastEmitPosition`) together, without inventing a second component that
  would only ever have one meaningful caller. This is also why the component
  cannot be purely a "resolved id" component like `AoeVfxIds`/`TargetedVfxIds`
  — unlike those, it carries mutable per-frame state, which is intrinsic to
  distance-gated emission (something has to remember where the last segment
  ended).
- **No new `CombatRoot`-side per-type VFX registry.** AOE/Targeted keep a
  per-type registry (`AoeTypeRegistry`/`TargetedTypeRegistry`) because
  `CombatRoot.Spawn` (the direct managed API, independent of skills) needs to
  look up their VFX ids by `TypeId` too (`VfxIdsFor` in `CombatRoot.cs:800`).
  Projectiles do not have this symmetry today: `CombatRoot.ProjectileCommandFor`
  already does not carry `SoundIds`/`SpawnSoundRadius` either — only the
  skill-driven path (`SkillIntervalTemplateBuilder.BuildProjectileTemplate`)
  populates those. Trail VFX follows the same existing asymmetry rather than
  inventing new machinery to close a gap that already exists for sound and is
  out of scope here.
- **Prefab-level hard validation (`BasicAttackPrefab.IsValidTemplate`) is left
  alone.** `TargetedPrefab.IsValidTemplate` hard-fails on a wrong link shape
  because every Targeted skill is expected to carry one. Every existing
  projectile prefab (`MagicBolt`, `MagicBolt2`, `PurpleBall`, `ArcaneMissile`,
  `ArcaneMissile2`, `FireArrow`, …) has no trail today and most will stay that
  way; a hard `Awake()` throw on a newly-added field would risk turning an
  optional cosmetic slot into a breaking authoring requirement across every
  existing projectile prefab. Shape/width mistakes are instead surfaced as
  loadout validator warnings, matching how the loadout validator (not the
  prefab) already flags Targeted link VFX problems as warnings
  (`SkillLoadoutValidator.cs:159-177`) — the shape enum initializer default
  (`= VfxDataShape.LineSegment`) still keeps a never-configured slot valid by
  construction.

## Constraints & Invariants

- **`LineSegment` VFX identity and dispatch are shape-generic, not
  emitter-specific.** `Docs/contracts/vfx-requests.md` — "`VfxId` identifies the
  registered VFX graph kind, not the AOE type and not the reason the event was
  emitted." A trail graph is registered and dispatched exactly like the
  Targeted link graph; nothing about dispatch changes.
- **Request buffers are transient upload payloads, not persistent per-particle
  storage.** `Docs/reference/simulation/vfx-system.md` "Shared Upload Buffer
  Semantics" — the trail graph must copy `StartPositions`/`EndPositions`/`Widths`
  into particle attributes in `Initialize Particles`; this is an authoring
  requirement on the `VisualEffectAsset` itself (Unity editor work, not code),
  and per [[editor-steps-are-user-steps]] is not something this plan hand-edits.
- **`VfxEmit.EnqueueLineSegment` already no-ops on id `0` or a non-`LineSegment`
  id** (`Assets/Scripts/System/Vfx/VfxEmit.cs:15`). A projectile with no
  authored trail effect costs one cheap integer check per frame and enqueues
  nothing — this is the same cost model AOE/Targeted already pay for their
  optional slots.
- **`CombatAoeVfxDispatchSingleton` is a fail-loud singleton**
  ([[fail-loud-singletons]]) — every existing VFX-producing system fetches it
  with `SystemAPI.GetSingletonRW`, never a `TryGet` guard. `ProjectileMovementSystem`
  must do the same; a missing singleton is a broken world and should throw, not
  silently skip trail emission.
- **`ProjectileMovementJob` runs `ScheduleParallel` across every active,
  non-arming projectile, both lanes, every simulation tick.** This is the
  hottest per-projectile job in the sim (`Docs/reference/simulation/projectile-system.md`
  performance notes call out "extreme attack scaling with many projectiles").
  Adding a required component parameter here is a job-query change, not just an
  extra field write — see the test-fixture invariant below. The distance gate
  itself must stay branch-cheap for every untrailed projectile: gate on
  `TrailId > 0` first, and compare squared distance
  (`math.lengthsq(position - LastEmitPosition)` against `StepDistance *
  StepDistance`) rather than calling `math.distance`/`sqrt`, since only the
  threshold comparison is needed, never the actual distance value.
- **IJobEntity components listed in `Execute` become required query terms.**
  Several PlayMode/EditMode tests hand-build projectile entities directly with
  `entityManager.CreateEntity(typeof(ProjectileTag), ...)` rather than going
  through `ProjectileDiscreteSpawnApplySystem`/`ProjectileContinuousSpawnApplySystem`
  (e.g. `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs:595-610`).
  Adding `in ProjectileTrailVfxComponent` to `ProjectileMovementJob.Execute`
  means any hand-built projectile entity missing that component is silently
  excluded from the query and will never have its position integrated again —
  not a compile error, a silent behavioral regression in those tests. This is
  the same shape of gap the project's own docs call out for hand-authored
  fixtures; it must be fixed by adding the component to those fixtures, not by
  weakening the production query.
- **Command flow: events are intent, commands are allocation intent**
  (`Docs/contracts/spawn-events-and-commands.md`). Trail id/width belong on
  `ProjectileSpawnCommand` (resolved per-entity data), not on `ProjectileSpawnEvent`
  (which only carries a template key + instance frame) — this matches how AOE's
  `VfxIds` and Targeted's `VfxIds`/`VfxSize` are carried today.

## Mechanisms Reused Vs. Introduced

Reused:

- `VfxDataShape.LineSegment` + `VfxDataShapeTable` (no new shape).
- `CombatVfxRoot.Register(VisualEffectAsset, VfxDataShape)` (no new registration API).
- `VfxEmit.EnqueueLineSegment` (no new emit helper).
- `CombatAoeVfxDispatchSingleton.PendingLineSegmentSpawns` / `ProducerHandle`
  combine pattern, copied verbatim from `TargetedResolveSystem.OnUpdate`.
- `ProjectileSpawnApplyUtility.WriteCommon` shared write path for both lanes'
  apply systems (no new per-lane duplication).
- The authored `VisualEffectAsset` + `VfxDataShape` selector pair pattern from
  `TargetedPrefab`/`BasicAoePrefab`/`LingeringAoePrefab` (no new authoring
  shape).
- The loadout-validator warning pattern for a misconfigured VFX slot, copied
  from `ValidateTargetedDefinition`'s link-effect checks.

Introduced (justified because nothing existing covers them):

- `ProjectileTrailVfxComponent` (`ProjectileEcsComponents.cs`) — projectiles
  have no existing per-instance VFX-id component at all, and no existing
  component tracks "distance since last visual event" for any emitter.
- `BasicAttackPrefab.TrailEffect` / `TrailEffectShape` / `TrailWidth` /
  `TrailStepDistance` — projectiles have no existing VFX authoring slot at all.
- `RuntimeProjectileDefinition.TrailVfxId` / `TrailWidth` / `TrailStepDistance`,
  and `ProjectileSpawnCommand.TrailVfxId` / `TrailWidth` / `TrailStepDistance` —
  the resolved-id/authored-value carrier, matching `AoeSpawnCommand.VfxIds` /
  `TargetedSpawnCommand.VfxIds`.
- `SkillDriver.RegisterProjectileVfx` — the one missing piece of the
  `RegisterAoeVfx`/`RegisterTargetedVfx` family.
- `SkillValidationWarningCode.ProjectileVisualWarning`.

## Design Validation Against Each Invariant

- Shape-generic dispatch: trail graph is just another `LineSegment` registrant;
  `CombatVfxRoot`/`CombatAoeVfxDispatcher` code is untouched.
- Transient upload buffers: no change to upload/particle-attribute contract;
  the new emitter produces the same `LineSegmentVfxEvent` shape Targeted
  already produces.
- No-op on id 0: authoring the slot is opt-in per prefab; unauthored projectiles
  pay one branch per frame, no queue growth.
- Fail-loud singleton: `ProjectileMovementSystem` fetches
  `CombatAoeVfxDispatchSingleton` with `GetSingletonRW`, matching every other
  VFX producer; no guard added.
- Hot job: only one new required component parameter and one cheap
  `TrailId > 0` branch plus a squared-distance compare added to the existing
  per-projectile job body; no new job, no new query pass, no new managed
  allocation, no `sqrt` call.
- Required-component query gap: explicitly called out as its own subtask
  (005) with the concrete file list, so it is fixed rather than discovered
  later as a silent test regression.
- Command flow: trail id/width added to `ProjectileSpawnCommand` only, never to
  `ProjectileSpawnEvent`, matching the event/command split doc.

## Minimal/Additive Vs. Refactor Comparison

**Minimal/additive approach (chosen):**

- Resulting data flow: `BasicAttackPrefab` slot → `vfxRoot.Register` in
  `SkillDriver` → `RuntimeProjectileDefinition.TrailVfxId/Width` →
  `ProjectileSpawnCommand.TrailVfxId/Width` → `ProjectileTrailVfxComponent` on
  the entity → read once per frame in `ProjectileMovementJob` → `VfxEmit.EnqueueLineSegment`.
  This is exactly the AOE/Targeted VFX data flow, one link added for
  projectiles.
- New concepts/types introduced: one component
  (`ProjectileTrailVfxComponent`), no new enum values beyond one validator
  warning code, no new system.
- Copies/translations added: none beyond the ones AOE/Targeted already pay
  (definition → command → component, each already a copy step in this
  codebase's spawn pipeline).
- Long-term cost: one more field to keep in sync across
  `RuntimeProjectileDefinition`/`ProjectileSpawnCommand`/`ProjectileTrailVfxComponent`,
  identical in shape to the AOE/Targeted VFX id fields already maintained this
  way. No parallel data path is created — there is exactly one way a projectile
  gets a trail id.

**Refactor approach (rejected):**

- A "collapse projectile registration into a shared per-type registry" refactor
  (giving projectiles an `AoeTypeRegistry`-style store in `CombatRoot`, unifying
  sound/VFX/render lookup by `TypeId` for both the skill path and the direct
  `CombatRoot.Spawn` path) would remove the existing Sound/VFX asymmetry
  between the two projectile spawn paths.
- Resulting data flow: single `ProjectileTypeRegistry` keyed by `TypeId`,
  consulted by both `SkillIntervalTemplateBuilder.BuildProjectileTemplate` and
  `CombatRoot.ProjectileCommandFor`.
- Existing concepts/types changed: `CombatRoot`'s ad hoc
  `templateTypeIds`/`projectileRenderIdByType` dictionaries, plus a new
  `SetProjectileVfxIds`/`SetProjectileSoundIds` surface.
- Copies/translations removed: the current duplication where
  `CombatRoot.ProjectileCommandFor` silently omits sound/VFX that the
  skill-driven path fills in.
- Long-term benefit: closes a real, pre-existing gap (direct-API projectiles
  get no sound or VFX), and would match the AOE/Targeted shape exactly.
- Decision: **ask user, deferred out of this plan.** The direct
  `CombatRoot.Spawn` asymmetry is a pre-existing gap unrelated to "projectiles
  emit trail VFX authored like other VFX events" — the request in scope is
  authoring parity for skill-driven projectiles. Closing the direct-API gap for
  sound *and* VFX together is a larger, separable refactor and is called out
  under Open Questions rather than folded silently into this change.

## Default Decision Rule Applied

Trail id/width has exactly one data path end-to-end
(prefab → registration → runtime definition → command → component → emit).
No second representation of "this projectile's trail VFX" is created anywhere,
so there is no divergent-source-of-truth risk to resolve.

## Task List

1. [001-authoring-projectile-trail-slot.md](001-authoring-projectile-trail-slot.md) —
   `BasicAttackPrefab` trail slot + `SkillLoadoutValidator` warnings.
2. [002-runtime-and-registration-wiring.md](002-runtime-and-registration-wiring.md) —
   `RuntimeProjectileDefinition`, `SkillDriver.RegisterProjectileVfx`,
   `ProjectileSpawnCommand`, `SkillIntervalTemplateBuilder.BuildProjectileTemplate`.
3. [003-ecs-trail-component-and-archetypes.md](003-ecs-trail-component-and-archetypes.md) —
   `ProjectileTrailVfxComponent`, both projectile archetypes, `WriteCommon`.
4. [004-movement-system-emission.md](004-movement-system-emission.md) —
   `ProjectileMovementSystem` emits one `LineSegmentVfxEvent` per `StepDistance`
   of travel (distance-gated, not per frame).
5. [005-test-fixture-archetype-updates.md](005-test-fixture-archetype-updates.md) —
   patch hand-built projectile test archetypes so they keep matching
   `ProjectileMovementJob`'s query.
6. [006-docs-updates.md](006-docs-updates.md) — correct the docs that currently
   assert projectiles never emit VFX/LineSegment requests.

Dependency order: 001 → 002 → 003 → 004 in sequence (each layer's output feeds
the next); 005 depends on 004 (the query change is what makes 005 necessary);
006 can be written any time after 004 is settled, since it documents the final
shape.

## Open Questions

- Should the direct `CombatRoot.Spawn` API path (non-skill projectiles) ever
  get trail VFX (and, separately, sound)? Out of scope here; flagged above as a
  candidate follow-up refactor, not part of this change.
- Authoring the actual `VisualEffectAsset` trail graph asset(s) and assigning
  them on specific projectile prefabs in the editor is user/editor work per
  [[editor-steps-are-user-steps]], not part of this code plan.
