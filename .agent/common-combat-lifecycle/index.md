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
