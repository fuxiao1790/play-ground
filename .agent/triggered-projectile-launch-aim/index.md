# Triggered Projectile Launch Aim

## Summary

Add authored, one-time nearest-hostile launch aim to trigger links whose effect is a
projectile. One target is selected per triggered spawn event/wave. Successful
acquisition rotates a full radial nova so deterministic shot index `0` points
directly at selected target and every other shot keeps equal full-circle spacing
relative to that aimed direction. Projectile velocity then stays fixed unless
projectile's existing discrete-only homing option is independently authored.

Feature applies before discrete/continuous command routing, so both projectile
lanes receive identical launch aim. Root/player casts never enable this policy;
they keep manual aim or existing player aim assist. No projectile archetype,
component set, pool, collision lane, or entity lifetime changes.

## Decisions

1. Author policy on `TriggerLink`, not `SkillDefinition`.
   Every trigger kind can target projectile skill. Same projectile asset can be
   manually cast, or reached by different trigger links with different launch-aim
   settings.
2. Use one shared enum, `ProjectileLaunchAimMode`, with `None = 0` and
   `NearestHostile = 1`, plus authored positive acquisition range. Zero enum value
   preserves current serialized assets.
3. Copy incoming trigger policy onto fresh compiled `RuntimeProjectileDefinition`,
   then existing command-shaped projectile template. Do not add trigger marker to
   spawn event and do not infer trigger origin from `DeterministicIdTickIndex`.
4. Acquire once per `ProjectileSpawnEvent`, before count expansion and before
   discrete/continuous split. Use acquired direction as angular origin of one
   full-circle radial nova: `direction(i) = Rotate(aimDirection, 360 * i / count)`.
5. Deterministic shot index `0` therefore points exactly at target; remaining shots
   occupy other nova slots. Successful acquisition intentionally replaces normal
   forward/side-spray/radial pattern calculation for whole wave. If no target is
   acquired, write existing authored trigger pattern unchanged.
6. Exclude `ContactGateSeedTargetId`. On-hit/stack-triggered waves must not select
   target they are temporarily forbidden to hit.
7. Refactor `TargetedAcquisition` into domain-neutral
   `CombatTargetAcquisition`; reuse same spatial-hash query for targeted chains and
   projectile launch aim. Do not duplicate nearest-hostile selection.
8. Launch aim and homing stay independent. Continuous projectiles may launch-aim
   but remain structurally unable to home. Discrete projectiles may use either or
   both.

## Constraints And Invariants

### Authoring and ownership

- Game logic owns trigger authoring and compilation; ECS consumes copied plain
  data. ECS cannot read live `TriggerLink`, `SkillDefinition`, GameObjects,
  Transforms, Colliders, or managed target companions.
  Sources: `Docs/reference/architecture/game-logic-simulation-boundary.md`,
  `Docs/layers/ecs-simulation.md`.
- Root casts use player-provided aim. Trigger launch aim must be explicit copied
  policy, disabled by default, and applied only to runtime projectile copies reached
  through incoming trigger edges.
  Sources: user decision; `Assets/Scripts/Skills/SkillSpawnTranslator.cs`,
  `Assets/Scripts/Skills/SkillSetCompiler.cs`.
- One authored trigger may target projectile, AOE, or targeted skill. Launch-aim
  fields are ignored unless compiled target is `RuntimeProjectileDefinition`.
  Sources: `Assets/Scripts/Skills/Trigger/TriggerLink.cs`,
  `Assets/Scripts/Skills/SkillSetCompiler.cs`.

### Spawn data flow

- Keep `event -> expansion -> command -> apply`. Expansion owns direction,
  count/spread/jitter, deterministic IDs, and lane routing. Apply only allocates or
  reuses entities.
  Sources: `Docs/flows/spawn-event-to-entity.md`,
  `Docs/contracts/spawn-events-and-commands.md`,
  `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs`.
- Spawn-template registry is immutable during simulation tick. Template policy is
  registered pre-tick and content-hashed with complete command-shaped template.
  Sources: `Docs/reference/simulation/spawn-template-registry.md`,
  `Assets/Scripts/System/Spawning/SpawnTemplateComponents.cs`,
  `Assets/Scripts/System/Core/CombatRoot.cs`.
- `ProjectileSpawnEvent` remains slim. Do not add duplicate launch-aim policy to
  every event; expansion already resolves template before volley construction.
  Sources: `Docs/contracts/spawn-events-and-commands.md`,
  `Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs`.

### Projectile lane and lifecycle

- Discrete/continuous membership remains authored and immutable per archetype.
  Lane split occurs only after expansion. Continuous archetype has no tracking
  component; discrete archetype retains existing tracking component.
  Sources: `Docs/reference/simulation/projectile-system.md`,
  `Assets/Scripts/System/Projectiles/ProjectileDiscreteSpawnApplySystem.cs`,
  `Assets/Scripts/System/Projectiles/ProjectileContinuousSpawnApplySystem.cs`.
- No new entity component, structural change, archetype, or pool. Launch policy
  exists only through authoring/runtime/template expansion and does not survive as
  entity state.
  Sources: `Docs/reference/simulation/ecs-notes.md`, user decision.
- Spawned entities join movement/collision next simulation update. Launch aim only
  initializes velocity; it adds no per-frame steering or target ownership.
  Sources: `Docs/flows/spawn-event-to-entity.md`,
  `Docs/reference/simulation/projectile-system.md`.

### Target acquisition and concurrency

- Reuse `TargetSpatialHashSingleton` snapshot and occupied-cell map. Selection must
  filter same faction, honor authored radius against target collision shape,
  deduplicate multi-cell targets, choose deterministic nearest candidate, and
  support excluded target key.
  Sources: `Assets/Scripts/System/Targeted/TargetedAcquisition.cs`,
  `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs`.
- Jobs reading persistent hash containers must depend on `BuildHandle` and publish
  their read handle into `ConsumerHandle`; next hash rebuild waits for consumers.
  Sources: `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs`,
  `Assets/Scripts/System/Targeted/TargetedResolveSystem.cs`.
- Missing hash singleton, non-positive range, `CombatFaction.None`, no hostile in
  range, or target coincident with spawn position must safely retain existing
  trigger direction/pattern. No zero-vector normalization.
  Sources: existing defensive spawn/tracking behavior and stripped-world test
  fixtures.
- High-count path remains Burst-compatible and allocation-free. Query once per
  event/wave, never once per projectile command, and bound cell traversal by
  authored range.
  Sources: `Docs/reference/simulation/ecs-notes.md`, project performance target in
  `Docs/project-overview.md`.

### Tests

- Agent must not execute Unity tests. User runs named EditMode/PlayMode tests and
  exports XML under `Logs/`; result claims require XML review.
  Source: `Docs/testing.md`, `Docs/project-overview.md`.

## Mechanisms Reused Vs Introduced

### Reused

- `TriggerLink` as authored edge owner.
- Fresh per-edge runtime compilation in `SkillSetCompiler`.
- `RuntimeProjectileDefinition -> ProjectileSpawnCommand` snapshot path.
- Command-shaped template registration and `SpawnTemplateHash`.
- `ProjectileSpawnExpansionSystem` as sole owner of wave direction and pattern.
- `TargetSpatialHashSingleton.AoeOccupiedCells` plus existing nearest-hostile
  collision-shape query.
- Existing faction and contact-gate target keys.
- Existing discrete/continuous command lists and pools.

### Introduced

- `ProjectileLaunchAimMode` enum: one shared representation of disabled versus
  nearest-hostile launch policy.
- Trigger-authored launch-aim mode and range, copied into runtime projectile and
  projectile template.
- Generic name/location `CombatTargetAcquisition` for already-existing reusable
  acquisition implementation.

No new system, event lane, entity component, target cache, or targeting algorithm.

## Design Validation

| Constraint | Validation |
|---|---|
| Trigger-only behavior | Compiler stamps policy only after compiling incoming trigger target. Root runtime projectile retains enum zero. |
| Manual/player aim preserved | Root template policy remains `None`; event `AimDirection` from player path remains authoritative. |
| Both projectile lanes | Acquisition happens before `ContinuousCollision` routes command to discrete/continuous list. |
| Continuous does not become homing | Only initial `BaseDirection` changes; continuous archetype still lacks `ProjectileTrackingComponent`. |
| Projectile identity preserved | Existing projectile apply systems and archetypes unchanged. |
| One target lookup per wave | Acquisition runs once after template stamp and before count loop. |
| Existing patterns preserved as fallback | Disabled or failed acquisition enters current forward/side-spray/radial switch unchanged. |
| Aim-oriented nova | Successful acquisition uses acquired direction as radial slot `0`; slots `1..count-1` use equal `360/count` offsets. |
| Exactly one direct slot | Only radial slot `0` is assigned zero angular offset from target direction; wave count and IDs remain unchanged. |
| On-hit self-target avoided | Acquisition passes event contact-gate key as excluded target. |
| Registry correctness | Policy fields remain in normalized template and therefore participate in existing whole-struct hash. |
| Thread safety | Expansion depends on hash build and registers scheduled read handle as consumer. |
| Performance | Existing occupied-cell broad phase, bounded radius, fixed-list dedupe, Burst job, one query/event. |

## Minimal/Additive Vs Refactor Comparison

### Minimal/additive approach

- Resulting data flow: trigger fields -> runtime projectile -> projectile template
  -> expansion-local target lookup -> existing command lists.
- New concepts/types: launch-aim enum and two fields at existing snapshot stages.
- Copies/translations added: mode/range copied at existing compile and template
  boundaries; no new intermediate payload.
- Structural warning: directly calling `TargetedAcquisition` from projectile code
  would make projectile simulation depend on a helper named/owned by targeted
  domain. Reimplementing query would create second nearest-hostile algorithm.
- Long-term cost: misleading ownership or duplicated selection semantics.

### Refactor approach

- Resulting data flow: same single spawn path, with existing acquisition helper
  moved to common combat-target ownership and consumed by targeted plus projectile
  systems.
- Existing concepts/types changed or removed: rename/move `TargetedAcquisition`
  to `CombatTargetAcquisition`; update existing callers/tests.
- Copies/translations removed or avoided: avoids second target snapshot wrapper,
  second spatial query, and synchronization contract.
- Long-term benefit: one source of truth for nearest-hostile-by-shape selection and
  excluded-target handling.

### Decision

Choose small refactor plus additive policy fields. Refactor shared acquisition
ownership; extend existing snapshot and expansion path. This produces one target
selection implementation and one spawn data path without coupling projectile
entities to targeted archetype/runtime state.

Default decision rule applied: authored, runtime, and template fields are necessary
boundary snapshots, not competing authorities. Authored `TriggerLink` is source of
truth; later copies are immutable runtime data. Duplicate target query is rejected.

## Tasks

1. [001-refactor-combat-target-acquisition.md](001-refactor-combat-target-acquisition.md)
2. [002-author-trigger-launch-aim.md](002-author-trigger-launch-aim.md)
3. [003-compile-and-template-launch-aim.md](003-compile-and-template-launch-aim.md)
4. [004-expand-aimed-projectile-waves.md](004-expand-aimed-projectile-waves.md)
5. [005-launch-aim-tests.md](005-launch-aim-tests.md)
6. [006-update-launch-aim-docs.md](006-update-launch-aim-docs.md)
7. [007-aim-oriented-nova.md](007-aim-oriented-nova.md)
8. [008-aim-oriented-nova-tests.md](008-aim-oriented-nova-tests.md)
9. [009-update-aim-oriented-nova-docs.md](009-update-aim-oriented-nova-docs.md)

## Dependencies And Considerations

- `001` and `002` independent.
- `003` depends on `002`.
- `004` depends on `001` and `003`.
- `005` depends on `001`-`004`.
- `006` follows final behavior from `001`-`005`.
- `001`-`006` were implemented against superseded shot-`0` replacement behavior.
- `007`-`009` are required follow-up tasks for revised aim-oriented nova behavior.
- `007` depends on existing implementation from `001`-`006`; `008` depends on
  `007`; `009` depends on `007` and `008` behavior being stable.
- No `info.md` exists for this task; this plan records exploration findings.
- Balance values remain authored per trigger. Existing assets default to `None`;
  implementation must not silently enable launch aim or choose global range.
- Authored range should be clamped non-negative at compile. `NearestHostile` with
  range `<= 0` behaves as no acquisition and uses existing fallback.

## Open Questions

None. Chosen behavior is complete: trigger-authored mode/range, one nearest hostile
lookup per wave, acquired direction as full radial-nova origin, exactly one directly
aimed slot, both projectile lanes, no homing coupling.
