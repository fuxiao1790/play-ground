# Catalyst skill

## Summary

New ECS domain: a persistent, friendly, non-damaging body owned by a caster.
Friendly projectiles and AOEs treat it as something they hit — pierce is spent,
contact/tick gating applies, no `CombatHitEvent` is produced — and the hit fires
the catalyst's own compiled skill. Bodies share an authored duration; every cast
refreshes the whole group and adds one body up to a max count.

Scope of this plan: one motion pattern (`OrbitOwner`), all three collision
shapes, root-cast only. Movement pattern is authored per skill and the enum is
built to grow (placed wall, follow, static) without touching the archetype.

Performance target: 200 live catalysts simulate without a frame-rate cliff;
production content is ~10. High cast cooldowns are assumed, so catalyst spawn
churn is negligible — the load-bearing paths are projectile/AOE overlap tests
against catalysts and the spawn burst their triggers produce.

## Architecture decisions

1. **Own archetype, own domain** (`Assets/Scripts/System/Catalysts/`), not a
   projectile variant. A catalyst sharing `ProjectileTag` would need
   `WithNone` exclusions in movement, tracking, both collision systems, both
   spawn-apply reuse queries, and pool cleanup, and a projectile spawn could
   claim a disabled catalyst slot. It carries no `CombatHitPayload`, so damage,
   crit, and stack accrual are structurally impossible rather than
   conditionally skipped.
2. **Group identity is `(Owner, TemplateKey)`.** `CatalystSpawnApplySystem` is a
   refresh-or-add system, not a plain apply: it sets every live member's
   `CombatLifetimeComponent.Remaining` to the authored duration, adds one body
   when `count < MaxCount`, and respaces phases. At max count nothing is added,
   so a cast at cap refreshes the group — including the oldest member — which is
   the decided overflow rule. Members order by ascending `CatalystId` (monotonic
   per cast), so "oldest" and phase index are the same deterministic ordering.
   `TemplateKey` is a content hash: an authored change produces a new key, so a
   re-cast after a stat change starts a new group and the old one lives out its
   duration. That is intended.
3. **Motion is authored per skill and stateless.**
   `CatalystMotionComponent { Pattern, Radius, AngularSpeed, Phase, LastOwnerPosition }`,
   with `CatalystMotionPattern { OrbitOwner = 0 }`. Position is
   `owner + radius * (cos, sin)(Phase + ElapsedTime * AngularSpeed)` — computed
   from absolute sim time, never accumulated, so pool reuse and a mid-life
   refresh cannot drift or desync a ring. Velocity is written as the orbit
   tangent, which feeds the existing `AlignToVelocity` render bit for free
   facing. `CatalystAnchorSystem` switches on `Pattern`; an unknown pattern is
   rejected at compile time in managed code, never branched on in the job.
4. **Owner loss does not kill the body.** The anchor system caches
   `LastOwnerPosition` and keeps orbiting it when the owner proxy is gone, so
   catalysts expire on their own lifetime. Authored `despawnOnOwnerLoss`
   (default `false`) kills immediately instead.
5. **Detection lives inside the existing collision systems.** Pierce counters
   and the contact gate buffer are projectile-owned state, and the decision is
   that both apply to catalyst overlaps, so an outside observer job cannot
   implement this. One shared `CatalystHitEmission` helper is called from
   `ProjectileDiscreteCollisionSystem`, `ProjectileContinuousCollisionSystem`,
   and `AoeCollisionCore.RunCollision` (serving both AOE lanes), mirroring
   `ProjectileHitEmission` / `AoeCollisionCore`'s own structure.
6. **No hit event for catalysts.** The helper never writes `HitWriter`. It
   spends pierce, refreshes the projectile's contact gate under
   `ProjectileHitEmission.TargetKey(catalystEntity)`, and enqueues the
   catalyst's trigger spawn. Gate ids share a space with target proxies, which
   is safe because entity indices are distinct. No blocking: a projectile with
   pierce left continues through.
7. **No per-catalyst retrigger cooldown.** The projectile's own contact gate
   bounds retriggers to one per projectile per catalyst per
   `RepeatHitCooldownSeconds`; the lingering AOE's `AoeHitGateComponent` tick
   interval bounds the AOE lane. Adding a second cooldown would duplicate an
   existing bound.
8. **Broadphase extends the existing system rather than adding a parallel
   one.** `TargetSpatialHashSystem` gains a catalyst gather, a
   projectile-cell-size map, an AOE-cell-size map, and `MaxCatalystRadius`, all
   in `TargetSpatialHashSingleton` behind a `CatalystCount` gate. A second hash
   system would duplicate the `BuildHandle`/`ConsumerHandle` protocol, which is
   the subtlest contract in the file and the easiest to get wrong. Catalysts are
   **not** added to the target proxy arrays: that would force exclusion checks
   into tracking, targeted acquisition, resource regen, and companion replay,
   where a missed check is a silent gameplay bug.
9. **Snapshot is one packed entry array, not parallel streams.** At ≤200 entries
   a single `CatalystSnapshotEntry` (entity, position, shape, faction, ids,
   `OnHitSpawnRef`) is one stream and one cache line per candidate; the target
   lane's parallel arrays exist for thousands of proxies and different consumers
   reading different subsets. Cell range for the catalyst probe is computed
   separately, expanded by `MaxCatalystRadius` only, so a large catalyst never
   widens the unit scan.
10. **Catalyst is a root-cast kind only.** `IntervalChildKind.Catalyst = 4` for
    the registry and the cast gate, but `SpawnTemplateValidation.EnsureValidChildKind`
    keeps rejecting it, so no interval or on-hit link can ever materialize a
    catalyst as a child. Trigger aim is authored:
    `CatalystTriggerAim { OutwardFromOwner = 0, OrbitTangent = 1, IncomingProjectile = 2 }`,
    default `OutwardFromOwner`.

## Constraints and invariants

| Invariant | Source | Design check |
|---|---|---|
| Spawn flow is `event -> expansion -> command -> apply`; apply reuses disabled slots before cold create. | [skill-ecs-simulation.md](../../Docs/reference/simulation/skill-ecs-simulation.md), [ProjectileDiscreteSpawnApplySystem.cs](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteSpawnApplySystem.cs) | Catalyst keeps event -> apply with disabled-slot reuse. No expansion stage: one cast materializes at most one body, so there is nothing to fan out. |
| ECS consumes copied snapshots and ids only; no asset, Transform, or managed companion reads in jobs. | [ecs-simulation.md](../../Docs/layers/ecs-simulation.md), [adr-002](../../Docs/decisions/adr-002-plain-data-snapshot-boundary.md) | Catalyst components carry resolved floats, ids, and `Hash128` keys. Owner is an `Entity` and is read through `ComponentLookup<TargetPosition>`. |
| Target hash: consumers combine `BuildHandle`, then publish into `ConsumerHandle`; the build system completes both before clearing. | [TargetSpatialHashSystem.cs:90](../../Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs#L90) | Catalyst lane lives in the same singleton under the same two handles. No new protocol. |
| Same-faction targets are skipped by projectile and AOE collision. | [ProjectileDiscreteCollisionSystem.cs:214](../../Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs#L214), [AoeCollisionCore.cs:90](../../Assets/Scripts/System/Aoes/AoeCollisionCore.cs#L90) | Catalyst rule is the inverse and lives in its own scan: overlap only when `entry.Faction == source.Faction`. Enemy fire ignores catalysts entirely. |
| A hit event means damage/stack application plus `TargetCompanion` presentation replay. | [CombatApplyFinalizeSingleSystem.cs:210](../../Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs#L210), [CombatApplyBridge.cs](../../Assets/Scripts/System/Presentation/CombatApplyBridge.cs) | Catalyst overlaps emit no `CombatHitEvent`, so they never reach finalize or the bridge. Catalyst has no `Health` and no `TargetStackEntry`. |
| Template keys are content-hashed, owner- and instance-counted; a despawn must release every key the entity carries. | [SpawnTemplateComponents.cs:89](../../Assets/Scripts/System/Spawning/SpawnTemplateComponents.cs#L89) | `SpawnTemplateRefEmit.Acquire/ReleaseCatalyst` walks the one key a catalyst carries (its `OnHitSpawnRef`), called from apply and from every death path. |
| Structural changes are main-thread and cause sync points; hot state changes use enableable components. | [ecs-notes.md](../../Docs/reference/simulation/ecs-notes.md) | Catalyst reuses disable-in-place pooling (`Active`, `CombatCollisionActiveTag`, `ArmingTag`) and gets its own `CombatPoolCleanupSystem` query, like targeted. |
| Root casts spend mana in `ExternalSpawnGateSystem` and refund the slot on rejection; internal children never spend. | [ExternalSpawnGateSystem.cs:126](../../Assets/Scripts/System/Spawning/ExternalSpawnGateSystem.cs#L126) | Catalyst casts, including refresh casts, go through the gate and pay. Catalyst triggers are internal spawns and pay nothing. |
| Cast rate/cooldown is `SkillSlotState`, owned in game logic, never ECS. | [skill-gameplay-system.md](../../Docs/reference/game-logic/skill-gameplay-system.md) | Duration, max count, and refresh live in ECS; the high cooldown is `1 / rate` in the slot state, unchanged. |
| Render is domain-agnostic: `CombatKinematicsComponent + CombatRenderComponent + CombatRenderAuthoring + Active`. | [CombatRenderPrepareSystem.cs:38](../../Assets/Scripts/System/Rendering/CombatRenderPrepareSystem.cs#L38) | Catalyst archetype includes those four and needs no render system work. |
| Combat entities must not access managed objects from jobs; singletons the sim needs are read directly and throw when absent. | [ecs-notes.md](../../Docs/reference/simulation/ecs-notes.md), existing collision systems | Catalyst snapshot and template map are read with `GetSingleton`, no `TryGet` skip paths. |

## Mechanisms reused versus introduced

Reused: the spawn event/command/apply pipeline, content-hashed template
registry with ref counting, disable-in-place pooling and `CombatPoolCleanupSystem`,
`CombatLifetimeComponent` + `CombatLifetimeSystem`, `ArmingTag` windup,
domain-agnostic render prep and batched render, `CombatShapeType` +
`CombatCollisionMath` + `CombatTargetShapeUtility` collider baking (all three
shapes, free), `CombatSpatialHash` cell math, the target hash's handle protocol,
`OnHitSpawnRef` + the `OnHitTrigger` compile path for "fires its own skill",
`ProjectileHitEmission` pierce/gate helpers, `ExternalSpawnGateSystem` mana
gate, and `SkillDefinitionTags.Duration` so `IncreasedDurationSupport` scales
catalyst duration with no new support.

Introduced: one archetype and domain; one motion pattern enum plus anchor
system (nothing in the codebase anchors an entity to an owner today); one
broadphase lane inside the existing hash system; one hit-emission helper whose
outcome is spawn-only. No new trigger link, no new stat, no new fold, no second
broadphase system, no second template registry pattern.

## Additive versus refactor comparison

| | Minimal/additive | Chosen |
|---|---|---|
| Resulting flow | New `CatalystSpatialHashSystem` beside the target one; catalyst hits detected by a standalone observer job that re-derives overlap. | Catalyst lane inside the existing broadphase; overlap resolved in the collision systems that already own pierce and gating. |
| Concepts/types | Second broadphase owner, second build/consumer handle pair, second overlap loop, plus a parallel notion of "hit" that cannot spend pierce. | One broadphase owner; one overlap loop per existing collision lane; "hit" keeps one meaning and one pierce/gate owner. |
| Copies/translations | Catalyst positions copied into a second snapshot with its own lifetime rules; pierce/gate state would need mirroring or a second event lane. | One snapshot, one array; trigger spawns stamped straight into the four existing spawn queues the jobs already hold. |
| Long-term cost/benefit | Two broadphase systems drift; "hit but no damage" becomes ambiguous; pierce interaction impossible without a third path. | Adding the wall pattern later touches one enum, one system switch, and the snapshot build — no new path. |

Decision: **refactor/extend** the broadphase and the collision loops. The
additive option creates a second data path for the same domain concept
(broadphase candidates) and a second, weaker meaning of "hit", which the
default decision rule rejects. The one genuinely new concept — an
owner-anchored, non-damaging body — gets a new type because nothing existing
represents it.

## Design validation and gates

- **Pierce ordering.** A projectile can overlap units and catalysts in the same
  frame. Discrete: the catalyst scan runs after the unit scan, so units
  resolve first and the ordering is fixed and documented, not incidental.
  Continuous: catalysts join the existing `FixedList512Bytes<HitCandidate>`
  time-of-impact list and resolve strictly nearest-first with units. Encode
  catalyst entries as `~index` in the existing `int TargetIndex` field so
  `HitCandidate` stays 8 bytes and candidate capacity does not shrink.
- **Spawn burst.** Worst case is one dense volley crossing the whole ring:
  `projectiles x catalysts` trigger spawns in one frame, each a normal spawn
  event. The contact gate stops the same projectile retriggering, but the first
  crossing is unbounded by design. Benchmark this case (task 008) before
  deciding whether a per-frame cap is needed; do not pre-add one.
- **AOE cap interaction.** `AoeCollisionCore` stops at
  `CollisionConstants.MaxAoeTargetsPerTick = 32`. Catalyst scanning must use
  its own budget so a crowded unit pass cannot starve catalyst triggers, and so
  catalysts cannot consume the unit budget. Keep the two counters separate.
- **Time source.** The stateless orbit angle requires the anchor system, the
  collision systems, and any test harness to agree on `SystemAPI.Time.ElapsedTime`.
  Verify a catalyst's snapshot position and its rendered position come from the
  same update's value (anchor runs in `SimulationSystemGroup` before the hash
  build; render prep re-derives nothing).
- **Ordering.** `CatalystAnchorSystem` must run before `TargetSpatialHashSystem`
  (positions must be final before the lane is built) and therefore before every
  collision system. `CatalystSpawnApplySystem` runs after
  `ExternalSpawnGateSystem` and before the anchor system, so a body spawned this
  frame is placed before it can be hit.
- **Pool reuse.** A reused slot must reset `Phase`, `LastOwnerPosition`,
  lifetime, arming, and its `OnHitSpawnRef`, and must acquire its template key
  exactly once. Mirror the reset discipline in the projectile apply systems.
- **Fail-loud.** Missing catalyst template map or snapshot singleton throws;
  no `TryGetSingleton` skip. An unhandled `CatalystMotionPattern` throws at
  registration in `CombatRoot`, not in a job.

## Tasks

1. [001-catalyst-spawn-kind-and-registry.md](001-catalyst-spawn-kind-and-registry.md) — `IntervalChildKind.Catalyst`, scope buffer/map/counts, `CombatRoot` register + spawn API, cast-gate branch.
2. [002-catalyst-archetype-and-apply.md](002-catalyst-archetype-and-apply.md) — components, archetype, refresh-or-add apply system, lifetime/pool/render wiring.
3. [003-catalyst-anchor-motion.md](003-catalyst-anchor-motion.md) — motion pattern enum, orbit anchor system, owner-loss policy.
4. [004-catalyst-broadphase-lane.md](004-catalyst-broadphase-lane.md) — catalyst gather, two cell maps, `MaxCatalystRadius`, handle protocol.
5. [005-projectile-catalyst-hits.md](005-projectile-catalyst-hits.md) — `CatalystHitEmission`, discrete + continuous wiring, pierce/gate/no-hit-event.
6. [006-aoe-catalyst-hits.md](006-aoe-catalyst-hits.md) — `AoeCollisionCore` catalyst pass with its own budget, both AOE lanes.
7. [007-skill-layer-catalyst-skill.md](007-skill-layer-catalyst-skill.md) — `CatalystSkill`, definition, runtime definition, tags, compiler, translator, driver registration, validator.
8. [008-tests-benchmark-docs.md](008-tests-benchmark-docs.md) — named EditMode/PlayMode tests, 200-catalyst benchmark, doc updates.

Editor work (prefab, ScriptableObjects, loadout wiring, benchmark scene
placement) is listed as user steps inside tasks 007 and 008. No agent hand-edits
Unity YAML.

## Open questions and dependencies

- **`OnExpireTrigger` is dead code** ([OnExpireTrigger.cs](../../Assets/Scripts/Skills/Trigger/OnExpireTrigger.cs)):
  `SourceSkillTags = None` and zero compiler references. It is the natural home
  for "ring pops when duration ends". Out of scope here — delete it or plan it
  separately, but do not leave it half-wired.
- **Trigger aim default** is `OutwardFromOwner`; `IncomingProjectile` needs the
  incoming direction threaded into the helper, which task 005 does anyway.
  Confirm the shipped default when the first catalyst asset is authored.
- **Wall/placed pattern** is deliberately excluded. It changes nothing in this
  plan's structure: a new `CatalystMotionPattern` value, a branch in the anchor
  system, and a placement position on the spawn command.
- Task 008's benchmark decides whether the trigger-spawn burst needs a cap.
  Nothing else in the plan depends on that answer.
