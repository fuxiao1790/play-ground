# Skill ECS Simulation

All docs in `Docs/` are design references. Check code before locking in an
implementation.

This document owns ECS-facing runtime behavior for compiled skills. For player
authoring, supports, loadouts, stat semantics, and cooldowns, see
[Skill Gameplay System](../game-logic/skill-gameplay-system.md).

## Scope

ECS receives copied runtime snapshots and turns them into scalable combat
state. It owns spawn events, commands, reusable entities, movement, target
queries, hit results, internal follow-up spawns, lifetime, and render/VFX
request data.

| ECS simulation owns | ECS simulation must not own |
|---|---|
| Event -> command -> apply, pools, active state, and runtime components | ScriptableObject/prefab authoring, supports, loadouts, or cooldowns |
| Projectile/AOE/targeted behavior, target queries, collision, health/status | Player input, UI state, or `SkillDriver` session state |
| Internal trigger expansion and copied child templates | Managed target callbacks or live authoring references |

Layer rules: [ECS Simulation](../../layers/ecs-simulation.md),
[Game Logic](../../layers/game-logic.md), and
[Game Logic And Simulation Boundary](../architecture/game-logic-simulation-boundary.md).

## Boundary Input

Game logic compiles a `RuntimeSkillDefinition`; `SkillSpawnTranslator` and
`CombatRoot` submit a plain spawn request/event. No ECS system reads a skill
asset, support, prefab, Transform, GameObject, Physics2D object, or managed
target companion.

The copied snapshot carries resolved behavior, damage/crit/status inputs,
faction/source data, visual type ids, sound/VFX ids, and child-template keys.
It must be complete for entity lifetime. See:

- [Skill Runtime Snapshots](../../contracts/skill-runtime-snapshots.md)
- [Spawn Requests](../../contracts/spawn-requests.md)
- [Spawn Events And Commands](../../contracts/spawn-events-and-commands.md)
- [Combat Root API](../../contracts/combat-root-api.md)

`CombatRoot` is managed combat bridge, not gameplay or ECS simulation owner. It
validates registration and appends initial event. ECS only consumes copied data
later.

## Root Cast Gate

External root-cast event carries caster proxy, resolved mana cost, and cast
token. `ExternalSpawnGateSystem` owns live ECS mana deduction and either emits
normal internal spawn event or rejection. Rejection reaches gameplay bridge so
`SkillDriver` can refund matching cooldown.

Interval, impact, on-hit, and stack-triggered child effects are internal ECS
events. They never spend player mana again.

## Skill Domains

| Compiled skill | ECS domain behavior | Detailed doc |
|---|---|---|
| Projectile | Discrete or continuous lane, move, optional tracking, collision, pierce, impact/timed child events, lifetime, render preparation. | [Projectile System](./projectile-system.md) |
| Pulse/lingering AOE | Event expansion, active pulse/tick collision, child events, lifetime, VFX, reuse. | [AOE System](./aoe-system.md) |
| Targeted chain | Target-proxy walk, delay/falloff, link VFX, walk-end expiry, reuse. | [Targeted System](./targeted-system.md) |
| Stacking detonation | Target-bucketed stack/state processing, threshold detonation event, status/result aggregation. | [Mob Combat State ECS](./mob-combat-state-ecs.md) |

Targeted chains query target proxies through spatial hash. They do not use a
gameplay collider or Physics2D. Tracking, projectile, and AOE target reads use
same proxy boundary; see [Target Proxy](../../contracts/target-proxy.md).

## Spawn And Child Templates

All materialization follows `event -> expansion -> command -> apply`.
Expansion handles authored count, echo, spread, scatter, and nested follow-up
payloads. Apply reuses disabled slots before cold-create overflow. A newly
applied combat entity starts resolving next simulation update.

Energy-driven child behavior is registered as content-hashed copied template.
Per-source ECS state holds template key, energy configuration, and accumulated
energy. On activation or pool reuse, state deterministically rolls from
`-InitialEnergyPercent` to `+InitialEnergyPercent` of its energy threshold.
The roll uses materialized spawner's own `SourceId`, not template jitter data,
so every spawning entity has separate offset. Negative accumulated energy stays
negative, delaying first child; future gain uses `EnergyPerSecond`.
`TimedSpawnSystem` stamps per-instance fields and emits normal child event when
threshold is met. Template ownership/counting prevents in-flight or pooled
entity data from disappearing after gameplay recompile.

Full shape, hashing, lifetime, and cleanup rules:
[Spawn Template Registry](./spawn-template-registry.md).

## Registration Boundary

Before root cast, managed bridge registers visuals, collision presets, audio,
VFX, and child templates. Compiled snapshot stores stable returned ids/keys.
Registration may deduplicate equivalent content. ECS consumes ids/keys only;
it never resolves Unity assets itself.

`SkillDriver` acquires complete replacement key set before releasing previous
compile set. Registry retains entries until neither owner nor live/pooled
instances reference them. See [Combat Root API](../../contracts/combat-root-api.md)
and [Spawn Template Registry](./spawn-template-registry.md).

## ECS Invariants

- Internal follow-ups remain ECS events; never route through managed callbacks.
- Domain systems require `ProjectileTag`, `AoeTag`, or `TargetedTag`; `Active`
  and `CombatScope` are not domain markers.
- Hot despawn disables reusable state; pooling and trimming stay bounded.
- Collision emits plain combat/spawn/VFX consequences. Managed application is
  separate presentation bridge work.
- Keep sound, VFX, spawn, and combat-result lanes typed and separate.
- ECS jobs do not access managed `TargetCompanion`.

## Frame Position

Skill events enter after managed root-cast submission. ECS expands events,
applies/reuses entities, then later updates run movement, collision, status,
render preparation, and presentation-ready outputs according to frame order.
See [Runtime Frame](../../flows/runtime-frame.md),
[Spawn Event To Entity](../../flows/spawn-event-to-entity.md), and
[Simulation ECS Overview](./index.md).

## Related Docs

- [Skill Gameplay System](../game-logic/skill-gameplay-system.md)
- [Projectile System](./projectile-system.md)
- [AOE System](./aoe-system.md)
- [Targeted System](./targeted-system.md)
- [Project ECS Implementation](./project-ecs-implementation.md)
- [ECS Notes](./ecs-notes.md)
