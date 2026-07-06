# Common Combat Lifecycle Refactor

## Summary

Replace the current scattered projectile/AOE enable-bit state with an ECS-owned
common combat lifecycle. Projectiles and AOEs share the same high-level phases:

| phase | `Active` | sprite gate | arming VFX | collision gate | timed spawn |
|---|---:|---:|---:|---:|---:|
| `Arming` | on | off | on | off | off |
| `Armed` | on | on | off | domain-derived | configured |
| `Dead` | off | off | off | off | off |

`Dead` is not a normal live phase. It is represented by disabled `Active`, so
the existing dead-slot reuse queries remain authoritative. External spawners and
`CombatRoot` provide lifecycle inputs only, such as arming seconds and duration.
They do not set phase, `Active`, sprite, collision, or timed-spawn state.

Large frame deltas must not lose armed time. A lifecycle tick computes
`CombatArmedDeltaComponent.Value`, the portion of the current frame spent armed.
Timed spawn, pulse VFX, and other armed interval systems consume that value
instead of raw `DeltaTime`, so a frame that crosses `Arming -> Armed` can still
emit same-frame armed ticks. Death is committed after armed systems have consumed
that frame's armed delta.

This plan supersedes the earlier AOE-only plans:

- `../aoe-spawn-state-unify/`
- `../aoe-activation-gates-reduce/`

The old #1 finding remains useful: spawn state must have one derivation point.
The old #2 decision changes: do not delete `CombatRenderActiveTag`; reframe it
as sprite visibility and derive it from lifecycle phase.

## Rationale

The current model makes state implicit in several enableable tags:

- `Active`
- `CombatRenderActiveTag`
- `ProjectileCollisionActiveTag`
- `AoeCollisionActiveTag`
- `TimedSpawnComponent`

Those tags are valid ECS gates, but they are not a lifecycle model by
themselves. Initial delay/arming needs an entity to be live and protected from
reuse, visually telegraphed by VFX, not sprite-rendered, not colliding, and not
spawning timed children. That requires one lifecycle source of truth and derived
gates.

The target shape keeps enableable components for chunk skipping and pooling, but
makes lifecycle phase and per-frame armed time common ECS data owned by apply and
lifecycle systems.

## Constraints & Invariants

- **External events are intent, not state.** Spawn events are slim links plus
  instance data, commands are allocation intent, and apply owns reuse/cold
  creation. State derivation therefore belongs in spawn apply, not in external
  spawners or `CombatRoot`. Source:
  [spawn-events-and-commands.md](../../Docs/contracts/spawn-events-and-commands.md).

- **Registry templates are immutable during the tick.** Any new command field
  must be plain unmanaged data and safe inside `NativeHashMap<Hash128, ...>`.
  Source:
  [spawn-template-registry.md](../../Docs/reference/simulation/spawn-template-registry.md).

- **Shared common components are not domain identity.** Common lifecycle data can
  live under `Common`, but systems that consume it must still require
  `ProjectileTag` or `AoeTag`. Source:
  [coding-standards.md](../../Docs/coding-standards.md).

- **Pooling uses disabled `Active`.** Spawn apply systems query
  `WithDisabled<Active>()` for dead slots, and pool cleanup counts enabled
  `Active`. `Arming` and `Armed` must both keep `Active` enabled. Sources:
  [project-ecs-implementation.md](../../Docs/reference/simulation/project-ecs-implementation.md),
  [ProjectileSpawnApplySystem.cs](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs),
  [AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs).

- **Enableable gates are the right hot-path mechanism.** State changes should
  avoid structural changes and keep chunk skipping for sprite/collision/timed
  work. Source:
  [ecs-notes.md](../../Docs/reference/simulation/ecs-notes.md).

- **Sprite render is not gameplay state.** Render state must not define domain or
  faction. Domain stays `ProjectileTag`/`AoeTag`; faction stays command/component
  data. Source:
  [render-batch-data.md](../../Docs/contracts/render-batch-data.md).

- **VFX is visual-only.** Arming VFX requests must not carry damage/status
  authority or call managed VFX objects from jobs. Source:
  [vfx-requests.md](../../Docs/contracts/vfx-requests.md).

- **Timed spawn already has catch-up caps.** The current `TimedSpawnSystem`
  bounds catch-up at `MaxTicksPerUpdate = 256`; the lifecycle refactor must
  preserve that guard while changing the delta source. Source:
  [TimedSpawnSystem.cs](../../Assets/Scripts/System/Common/TimedSpawnSystem.cs).

- **Current AOE spawn state is already partly documented but not enforced.**
  `AoeSpawnApplyUtility.SpawnStateFor` exists, yet the reuse/cold-create paths
  still write enable bits by hand. Source:
  [AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs).

- **Current death transitions are scattered.** Lifetime, projectile collision,
  and AOE collision each disable overlapping gates. The refactor must collapse
  despawn gate changes behind shared lifecycle/death helpers. Sources:
  [CombatLifetimeSystem.cs](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs),
  [ProjectileCollisionSystem.cs](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs),
  [AoeCollisionCore.cs](../../Assets/Scripts/System/Aoe/AoeCollisionCore.cs).

## Mechanisms Reused vs. Introduced

Reused:

- `Active` as live-pool-slot gate and disabled-slot reuse authority.
- Domain collision enableable tags:
  `ProjectileCollisionActiveTag` and `AoeCollisionActiveTag`.
- `TimedSpawnComponent` as interval child-spawn config/gate.
- Existing spawn event -> expansion -> command -> apply pipeline.
- Existing VFX request queue and trigger model.
- Existing bounded interval catch-up loops.

Introduced:

- `CombatLifecyclePhase` common enum: `Arming`, `Armed`.
- `CombatLifecycleComponent` common data: phase, arming remaining, pending death
  flag.
- `CombatArmedDeltaComponent` common data: armed elapsed time this frame.
- A lifecycle spawn-state helper that derives phase, active, sprite, collision,
  timed-spawn, and initial armed delta from command inputs.
- A lifecycle tick system that transitions `Arming -> Armed`, computes armed
  delta, and marks pending death without immediately disabling `Active`.
- A death commit helper/system that disables `Active` and derived gates after
  armed systems have consumed the current frame.
- A sprite render gate name/comment (`CombatSpriteRenderActiveTag`) replacing the
  broader `CombatRenderActiveTag` meaning.
- Arming VFX trigger/request path, visual-only.

## Design Validation

- **Spawner encapsulation:** External code passes only arming/duration inputs.
  Apply systems derive phase and gates. Holds.
- **Pool safety:** `Active` remains enabled for `Arming` and `Armed`; disabled
  only at death commit. Holds.
- **Sprite/VFX split:** Sprite gate is enabled only while armed. Arming VFX is
  separate visual-only request data. Holds.
- **Large delta behavior:** lifecycle tick computes `armedDelta` from leftover
  frame time after arming. Interval systems consume `armedDelta`, so ticks are
  not lost. Holds.
- **Chunk skip/perf:** sprite, collision, and timed-spawn remain enableable
  gates. Lifecycle data is plain component data on reusable archetypes. Holds.
- **Domain boundaries:** common lifecycle systems use domain-tagged jobs or
  domain-specific adapters; no common-component-only gameplay queries. Holds.
- **Template safety:** command/request additions are primitive fields, not
  managed references. Holds.
- **Impact AOE caution:** impact AOE is one-shot collision-owned death today.
  The plan keeps collision-owned deactivation, while lifecycle/death helpers
  provide the same derived-gate path. Armed duration support must be tested so
  one-shot impact AOEs are not killed before their collision pass.

## Minimal/Additive vs. Refactor Comparison

Minimal/additive approach:

- resulting data flow: add `InitialDelaySeconds` to AOE spawn and manually delay
  only `AoeCollisionActiveTag`.
- new concepts/types introduced: AOE-only windup/delay component.
- copies/translations added: AOE delay state separate from projectile lifetime,
  render state, timed-spawn state, and collision state.
- long-term cost: projectiles need a second future implementation; sprite/VFX
  split remains implicit; large-delta catch-up remains inconsistent.

Refactor approach:

- resulting data flow: command carries lifecycle inputs; apply derives common
  lifecycle state; lifecycle tick derives per-frame armed delta and gate
  transitions; death commit disables reusable slots.
- existing concepts/types changed or removed: `CombatRenderActiveTag` becomes
  sprite-specific; scattered spawn/death gate writes move behind lifecycle
  helpers.
- copies/translations removed or avoided: no AOE-only state path, no external
  state booleans, no duplicate render-vs-active representation.
- long-term benefit: projectile and AOE arming share one model; initial delay is
  data on top of existing lifecycle.

Decision: choose refactor.

Reason: arming/armed/dead is a shared combat-entity lifecycle concept currently
represented by scattered gate writes. Adding an AOE-only delay would create a
parallel state system and preserve the bug class.

## Default Decision Rule

If two representations or data paths describe the same domain concept, refactor
toward one source of truth unless there is a concrete compatibility or migration
reason not to. Here, lifecycle phase is the source of truth; enableable gates are
derived execution filters.

## Task List

- [001](001-render-gate-becomes-sprite-gate.md) - Rename/reframe render gate as
  sprite visibility, no behavior change.
- [002](002-common-lifecycle-components.md) - Add common lifecycle/armed-delta
  data to projectile and AOE archetypes.
- [003](003-spawn-apply-derives-lifecycle-state.md) - Make projectile and AOE
  spawn apply derive all lifecycle gates from command inputs.
- [004](004-lifecycle-tick-and-death-commit.md) - Replace immediate lifetime
  death with lifecycle tick, armed-delta computation, and death commit.
- [005](005-armed-delta-consumers.md) - Convert timed spawn and AOE pulse VFX to
  use armed delta and bounded catch-up.
- [006](006-arming-vfx-path.md) - Add arming VFX trigger path separate from
  sprite render.
- [007](007-regression-tests.md) - Add behavior and large-delta regression tests.
- [008](008-docs-and-cleanup.md) - Update docs/comments and remove stale AOE-only
  plan assumptions.

## Open Questions / Considerations

- **Impact AOE one-shot timing:** impact AOEs currently have no
  `CombatLifetimeComponent` and die after collision. If every impact AOE must
  also have lifetime ticking, implementation must prove that lifecycle expiry
  cannot kill it before its first armed collision pass. The likely rule is:
  collision-owned death remains authoritative for one-shot impact AOEs, while
  lifecycle owns arming, sprite, and pending death.
- **Arming VFX duration:** existing `VfxPendingSpawn` has no duration field.
  Either the arming VFX asset owns duration by trigger/type, or `VfxPendingSpawn`
  gains a visual-only duration field. This is visual-only and does not affect
  gameplay authority.
- **Naming migration:** `CombatRenderActiveTag` can be renamed in one mechanical
  commit or temporarily aliased. Prefer direct rename to avoid two render gates.
