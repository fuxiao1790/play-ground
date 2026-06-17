Condensed Design Points — ECS Rewrite
1. Goals & Scope
Primary goal: architecture clarity. Old systems built with incomplete knowledge; hard to extend, debug, reason about.
Secondary: performance, authoring, determinism — valid but not driving.
Out of rewrite scope: movement, lifetime math, collision math, rendering/VFX, authoring definitions.
In scope: event/data flow, spawn/despawn/structural changes, hit/damage application.
2. Core Rules
Snapshot at spawn. All data needed by runtime systems is fat-copied at spawn time. No runtime lookups into GameObjects, managed callbacks, or centralized definition storage. In-flight entities are self-contained.

Component presence = behavior presence. No fat snapshot + flag-branching. If an entity can do timed spawn, it has a TimedProjectileSpawn component. Systems query only entities with the relevant component.

Next-tick spawn. Entities spawned during tick N do not participate in simulation until tick N+1. Enforced by phase order alone — no SpawnedThisTick marker.

No managed access in simulation. Burst/job systems use only unmanaged ECS data + Entity references. Managed companion references live only on proxy entities and are read only by the damage dispatch bridge.

Spawn is a next-tick, phase-local, unordered operation. Spawn queues are unordered transport. Correctness comes from command data, not queue position.

3. Active / Slot Reuse

Active : IComponentData, IEnableableComponent
Generic runtime occupancy flag for all reusable entities (projectile, AoE, future types).
Entity type = marker/data components. Slot availability = Active enabled state.
Lifetime system: when lifetime <= 0 → SetComponentEnabled<Active>(entity, false).
Reuse query: WithDisabled<Active>() + required archetype components.
No external free-list. No pooling registry. Query is the source of truth.
Proxy target entities: delete immediately when no longer simulating (not disable). They are not pooled.
4. Lifetime Unification
Merge ProjectileLifetimeSystem + AoeLifetimeSystem into one generic system.
Logic: iterate all entities with Lifetime + Active, tick time, disable Active when <= 0.
5. Spawn Pipeline
5a. Event vs Command distinction
Type	Meaning
SpawnEvent	Gameplay intent. May describe volley / burst / fan / ring / scatter. One event = 0..N entities.
SpawnCommand	Allocation intent. One command = exactly one concrete entity. No volley semantics.
5b. Typed event streams
Streams are typed by what is spawned, not why or who spawned it. Multiple producers may write to the same stream.

ProjectileSpawnEvent, ProjectileSpawnCommand
AoeSpawnEvent, AoeSpawnCommand
5c. Pipeline stages (per spawned entity type)

Producer Systems (parallel)
  → NativeQueue<SpawnEvent>.ParallelWriter

Event Finalize
  → NativeArray<SpawnEvent>

SpawnExpansionSystem
  → resolves all per-entity math: count, spread, jitter, direction, position, rotation
  → routes to exact-shape command queue
  → NativeQueue<SpawnCommand>.ParallelWriter (per shape)

Command Finalize
  → NativeArray<SpawnCommand> (per shape)

SpawnApplySystem (per shape)
  → query disabled Active entities with matching component set
  → overwrite components, enable Active
  → ECB.CreateEntity + AddComponent per remaining commands (overflow)
5d. Exact-shape command queues
Start with exact component-set queues. Each unique projectile shape gets its own command queue and apply path. Generalize only when duplication becomes painful.


BasicProjectileCommandQueue       → BasicProjectileSpawnApplySystem
ImpactAoeProjectileCommandQueue   → ImpactAoeProjectileSpawnApplySystem
TimedChildProjectileCommandQueue  → TimedProjectileSpawnApplySystem
TimedImpactProjectileCommandQueue → TimedImpactProjectileSpawnApplySystem
...
Rule: command queue == required component set == dead-slot query shape == overflow creation shape.

5e. Expansion owns all spawn math
Expansion resolves count, spread, jitter, final direction, final position, final rotation, any randomized variation. Spawn apply does not understand volleys, fans, or rings.

5f. Recursive spawn depth
Enforced by type shape, not runtime depth counter. Payloads are fixed-depth nested structs. Each spawn system strips the top layer when emitting the child event. No unbounded child list or self-reference — deeper recursion impossible by construction.


Before:  Entity → SpawnCondition → Entity → SpawnCondition → Entity
After 1: Entity → SpawnCondition → Entity
After 2: Entity
5g. Spawn condition components
Separate by both trigger type and output type. No generic SpawnKind enum.


TimedProjectileSpawn    → emits ProjectileSpawnEvent
TimedAoeSpawn           → emits AoeSpawnEvent
ImpactProjectileSpawn   → emits ProjectileSpawnEvent on collision
ImpactAoeSpawn          → emits AoeSpawnEvent on collision
5h. AoE follows identical pipeline
AoE will gain scatter/timed-child behavior. Keep the same event → expansion → exact-shape command → apply structure.

5i. Overflow creation
ECB.CreateEntity + AddComponent per required component. Not prototype entities. Structural change is acceptable on the overflow path only.

6. Native Container Rules
Container	Writer	Reader
NativeQueue<T>	creates / ParallelWriter	converts to array, then clears
NativeStream	creates	disposes after read
Writers may assert queue is empty before scheduling. No container crosses a phase boundary partially consumed.

7. Collision & Consequence Pipeline
ContactGateSystem — standalone system, maintains gate state only: tick cooldowns, expire old entries, clear invalid state.

CollisionSystem — detects overlap, checks contact gate inline, emits final typed consequence events. May disable source Active immediately if projectile consumed by hit.


ProjectileCollisionSystem
  → DamageReplayEvent       (if ContactDamage component present)
  → AoeSpawnEvent           (if ImpactAoeSpawn component present)
  → ProjectileSpawnEvent    (if ImpactProjectileSpawn component present)
Collision may NOT: apply damage, spawn entities, call managed callbacks, mutate GameObjects.

AoE collision: use existing logic, also emit final consequence events.

8. Damage / Target Bridge
Damage replay: atomic. Every qualified hit = one DamageReplayEvent. No per-target-per-tick condensation yet.


DamageReplayEvent {
    Entity TargetProxy;
    DamageSnapshot Damage;
    float2 HitPosition;
    float2 HitDirection;
}
Storage: NativeQueue<DamageReplayEvent> (parallel writes during simulation) → NativeArray → dispatch.

Target proxy entity: ECS proxy per GameObject target. Unmanaged ECS identity for simulation. Carries managed companion reference (restricted access — see below).

Proxy lifecycle: GameObject owns it. Creates on enable, pushes position/state each Update(), deletes immediately when no longer simulating (not disabled/pooled). Death animation continues on GO after proxy deletion.

Managed companion access: Only DamageDispatchBridge may read the managed companion reference. No simulation, collision, spawn, or tracking system may access it.

9. Frame / Phase Order

MonoBehaviour Update()
  - GOs push target position/collision state into ECS
  - GOs that stop simulating delete their proxy entity

ECS Simulation
  2.1  Lifetime / Active update
  2.2  Timed spawn event production
  2.3  Projectile tracking / movement
  2.4  Projectile collision (+ contact gate inline)
  2.5  AoE collision (+ contact gate inline)
  2.6  Contact gate state maintenance
  2.7  Spawn event finalization
  2.8  Spawn expansion: events → commands
  2.9  Spawn apply: commands → reuse / ECB overflow
  2.10 Damage replay export (NativeArray ready)

Damage Dispatch  (main thread, still within Update frame)
  - DamageDispatchBridge reads array → managed companion → GameObject callback

MonoBehaviour LateUpdate()
  - Death animation transitions
  - Scene cleanup / GO destruction
  - No unresolved ECS events remain
Strict phase ordering is architectural, not incidental scheduling.

10. Delta Time
Variable deltaTime. No fixed simulation tick.

Timed spawn: catch up fully. If deltaTime spans multiple intervals, emit all due spawn events. Burst cost accepted. Spawned entities still don't simulate until next tick.

11. What Stays
ProjectileMovementSystem
ProjectileTrackingSystem
ProjectileCollisionSystem (minor refinement to consequence output)
AoeCollisionSystem (same)
ProjectileContactGateSystem / AoeContactGateSystem (minor role clarification)
Rendering / VFX pipeline
Authoring / ScriptableObject definitions
Target bridge structure (CombatTargetRegistry, CombatTargetSync, ICombatTarget) — refine boundary, don't rebuild
12. What Gets Rewritten
ProjectileSpawnSystem, AoeSpawnSystem, ProjectileChildSpawnSystem, ProjectileMultiExpandSystem → replaced by typed event → expansion → exact-shape apply pipeline
ProjectileSpawnCommand → split into ProjectileSpawnEvent / ProjectileSpawnCommand with clear boundary
CombatSpawnConvertJob, CombatHitElement, CombatHitDispatchSystem, CombatHitFlushJob → replaced by typed consequence streams + DamageDispatchBridge
ProjectileLifetimeSystem + AoeLifetimeSystem → unified generic lifetime system
