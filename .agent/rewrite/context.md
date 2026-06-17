Working assumptions so far:

* The doc is for a **near-full rewrite of your current Unity ECS/DOTS systems**.
* The goal is not just code cleanup, but a clearer architecture for high projectile counts, rendering, collision, hit/damage application, child spawning, and VFX integration.
* I should avoid asking a long questionnaire upfront.
* After each answer, I’ll update assumptions and either ask the next highest-value question or start drafting the doc.
* Once extra questions become low-value, I’ll produce the best document using the assumptions we have.

First high-impact question:

What is the **primary goal** of the rewrite?

Choose the closest option:

A. **Performance ceiling** — support more projectiles / hits / VFX at stable frame times.
B. **Architecture clarity** — make systems easier to reason about, extend, and debug.
C. **Authoring workflow** — make prefab/VFX/projectile definitions easier to create and wire up.
D. **Deterministic gameplay behavior** — fixed tick behavior, consistent hit/damage semantics, predictable ordering.
E. **All of the above**, but rank them in priority order.

architecture clarity mainly but all points are valid to some extent. old systems were built with incomplete knowledge resulting in janky implementation that is hard to update/debug. 

Working assumptions updated:

* **Primary goal:** architecture clarity.
* Performance, authoring, deterministic behavior, and debugging still matter, but they are secondary constraints.
* The rewrite should replace “janky accumulated implementation” with a design that is easier to extend, inspect, profile, and reason about.
* The doc should probably explain **why the old design became hard to work with**, then define a cleaner system/data/event pipeline.

Next high-impact question:

What is the intended **scope of the rewrite**?

A. **Projectile ECS only** — movement, lifetime, collision, child spawning, rendering data.
B. **Projectile + hit/damage pipeline** — includes collision events, damage condensation/application, target health.
C. **Projectile + rendering + VFX** — includes instanced sprites and GPU/VFX Graph dispatch.
D. **Full combat runtime slice** — projectile simulation, collision, damage, target state, rendering, VFX, authoring, debugging/profiling tools.
E. Something else; describe the boundary.

movement lifetime collision are all working fine and architecturally sound. since they are very simple / math heavy. these are systems where the complexity isn't coming from utilizing unity dot. 

Working assumptions updated:

* **Not in main rewrite scope:** movement, lifetime, and core collision math. These are already simple, math-heavy, and architecturally acceptable.
* **Rewrite focus is likely around the systems where DOTS constraints create complexity**, not pure simulation math.
* The problematic areas are probably things like event flow, structural changes, buffers, render collection/submission, child spawning, hit/damage application, ECS ↔ scene tree bridge, VFX dispatch, and authoring/debugging.

Next high-impact question:

Which area is the **main source of architectural pain right now**?

A. **Event/data flow** — too many buffers, streams, copies, unclear ownership/order.
B. **Spawn/despawn/structural changes** — ECB playback, prefab spawning, child projectile requests.
C. **Hit/damage application** — many hit events, target aggregation, ECS vs scene nodes, per-tick semantics.
D. **Rendering/VFX dispatch** — collecting renderables, grouping by type, VFX Graph buffers/events.
E. **Authoring/data definitions** — prefab metadata, ScriptableObjects, projectile type definitions, editor workflow.
F. **Debuggability/profiling** — hard to inspect what happened in a tick or why performance spikes.

A, B, C mainly. very hard to work with.
D, E can also largely stay as it is. 
F can some times be a problem but not the biggest issue.

Working assumptions updated:

* **Main rewrite scope:**
  A. Event/data flow
  B. Spawn/despawn/structural changes
  C. Hit/damage application
* **Mostly out of scope:** movement, lifetime, collision math, rendering/VFX, authoring definitions.
* **Secondary concern:** debugging/profiling, but mostly as a byproduct of cleaner architecture.
* The doc should focus on untangling ownership, event lifecycle, command buffers, buffer usage, and the “what happens during one tick” pipeline.

Next high-impact question:

What is the preferred **top-level architectural shape** for the rewrite?

A. **Strict staged pipeline**
Each tick has explicit phases: collect events → reduce/aggregate → apply structural changes → apply damage → clear buffers. Easy to debug, slightly more rigid.

B. **Owner-local buffers with final joins**
Each subsystem/entity/scope owns its own buffers as much as possible. Jobs write locally, then a small number of join systems merge/apply results. Fewer shared writes, but more buffer plumbing.

C. **Central combat event bus**
All combat systems write to centralized streams/buffers. Later systems consume from a known place. Easier to trace globally, but can become a bottleneck or dumping ground.

D. **Hybrid: staged pipeline + local buffers**
Use explicit tick phases, but keep writes local until a well-defined join/apply phase. This is probably the cleanest fit for your constraints unless you prefer otherwise.

this is what i have gathered from reviewing current code base and roughly what i want to future implementation to look like

the design of the ecs relys on snapshotting everything at spawn time. /snapshotting.md
snapshotting everything does mean recursive spawn, and recursive spawn is limited to 2. 
for example, projectile -> time based child spawn -> projectile -> impact spawn -> aoe
the first projectile carries a time based child spawn component 
the component contains the projectile that will be spawned and a impact aoe component 

life time system is split into 2. 1 for projectile and 1 for aoe. they should combine into 1 since the logic should simply be looping over all entities that have lifetime component, tick time, if time <= 0 then set active = false using IEnableableComponent 

a set of systems should take care of time based spawns if an entitiy has the component for it.
each spawn system should simply loop though entities with the specific kind of TimedSpawnComponent, and produce spawn event.
since everything must be snapshotted at spawn time the component should contain all data fields required produce spawn event that contains raw data instead of referencing some kind of centralized storage.

projectile collision system can remain as it is. for the most part.
aoe collision system same as above
collision systems are expected to produce spawn events (aoe explosion on projectile hit, the projectile carries an aoe spawn component)

projectile contact gate system should also stay as it is.

projectile tracking should also stay as it is.

spawn system is intended to reuse dead slots to reduce the # of entities going through ecb and memory management.
it should read events produced from collision systems use entity query to query for archetypes with dead slots, spawn workers to loop through them and reuse dead slots.
the native container used by producers and consumers should be contention free since we guarantee writers are done before readers start.
slot distribution doesn't have to be perfect, a few remainder is nothing compared to having to full scan something just to have perfect distribution.

still considering how damage application should be done. will provide more detail.

Working assumptions updated:

* The rewrite should be based on **snapshot-at-spawn** as a core rule.
* Spawned entities should not reference centralized projectile/AoE definition storage during runtime simulation.
* Recursive spawn is allowed but intentionally bounded, currently assumed to be **max depth 2**.
* Movement, projectile collision, AoE collision, contact gate, and tracking mostly stay.
* Lifetime should become a single generic system over `Lifetime + Active/Enableable` instead of separate projectile/AoE lifetime systems.
* Timed spawn systems should be narrow and component-specific:
  `TimedSpawnComponent -> SpawnEvent`.
* Collision systems may also produce spawn events, such as projectile impact spawning AoE.
* Spawn events should contain **raw snapshotted spawn data**, not handles into centralized definitions.
* Spawn application should prioritize **dead slot reuse** using `IEnableableComponent`, only falling back to ECB/entity creation for overflow.
* Producer/consumer containers can be designed as phase-local and contention-free because readers start only after writers complete.
* Perfect dead-slot distribution is not required; avoiding expensive full scans is more important.

A clearer architectural direction is emerging:

```text
Simulation systems produce intent/events.
Spawn systems consume spawn events and materialize entities.
Entity lifetime/death disables entities instead of destroying them.
Structural changes are treated as exceptional fallback, not the normal path.
```

The main thing still missing is the **shape of the spawn event/data model**, because that determines whether this design stays clean or turns into another blob of special cases.

Next high-impact question:

How do you want spawn events represented?

A. **One generic `SpawnEvent` with a `SpawnKind` enum/union-like payload**
Example: one stream/list contains projectile, AoE, maybe future spawn kinds. Consumer switches on kind. Simple pipeline, but payload can become fat.

B. **Separate event streams per spawn type**
Example: `ProjectileSpawnEvent`, `AoeSpawnEvent`, etc. Cleaner data layout and fewer switches, but more systems/containers.

C. **Separate event streams per producer category**
Example: timed projectile spawn events, impact AoE spawn events, death spawn events. Good traceability, but can fragment the pipeline.

D. **Hybrid**
Use separate event structs by spawned entity type, but allow multiple producers to write into the same destination event collection. For example, timed spawn and collision both produce `ProjectileSpawnEvent` or `AoeSpawnEvent`.

My current assumption would be **D**: event type should describe what is being spawned, not why it was spawned.

B, separate. Spawning different entity is where the logic truly diverge, don't need to force them to be together, at some point there will be an if statement somewhere alone that path that checks what is being spawned, it's cleaner to do it through component and system and i think it is more idiomatic to ecs.

Working assumptions updated:

* Spawn events should be **separate by spawned entity type**, not generic.
* The design should avoid a “fat spawn event + switch on spawn kind” model.
* Divergence in spawn logic is real, so it should be represented explicitly through **separate components, event buffers, and systems**.
* Event type should probably correspond to the entity/archetype being materialized:

  * `ProjectileSpawnEvent`
  * `AoeSpawnEvent`
  * potentially future `PickupSpawnEvent`, `BeamSpawnEvent`, etc.
* Multiple producers may still write the same event type, but the **consumer/apply system is specific to the spawned entity type**.
* This fits ECS better because different spawned entities usually need different component sets, reuse queries, initialization logic, and fallback ECB paths.

Current architecture direction:

```text
TimedProjectileSpawnSystem ─┐
ProjectileImpactSpawnSystem ├─> ProjectileSpawnEvents ─> ProjectileSpawnApplySystem
OtherProjectileProducer ────┘

ProjectileCollisionSystem ──┐
TimedAoeSpawnSystem ────────┤─> AoeSpawnEvents ────────> AoeSpawnApplySystem
OtherAoeProducer ───────────┘
```

This keeps the pipeline staged without pretending all spawn operations are the same.

Next high-impact question:

Where should spawn events live during a tick?

A. **Singleton dynamic buffers**
Example: one singleton entity has `DynamicBuffer<ProjectileSpawnEvent>` and another has `DynamicBuffer<AoeSpawnEvent>`. Easy to inspect and consume, but parallel writing needs care.

B. **Native containers owned by systems**
Example: `NativeList<T>`, `NativeStream`, or per-worker containers allocated/cleared by the spawn pipeline. Faster and more explicit for jobs, but less visible in Entity Debugger.

C. **Producer-local buffers, then join**
Each producer/system writes locally, then a join system merges into type-specific spawn queues. More plumbing, but strongest ownership boundaries.

D. **Hybrid**
Use native containers for high-volume internal events, then optionally expose debug summaries or sampled events through ECS buffers/tools.

spawn system should directly reuse dead slots and create ecb for remainder. it's pointless to pass it to other systems. 
also, need to differentiate between projectile spawn command and projectile spawn event. 
projectile spawn event can contain a volley of projectile but projectile spawn command can contain only 1 projectile. the projectile system need to expand the volley definition into a concrete list of projectile before it can be spawned.

Working assumptions updated:

* **Spawn events should not be passed through extra join/apply systems unnecessarily.**
* The type-specific spawn system should be the direct owner of:

  * reading spawn input,
  * expanding high-level spawn events,
  * reusing dead slots,
  * issuing ECB fallback for overflow.
* Need a clear distinction between:

```text
ProjectileSpawnEvent
= high-level gameplay intent
= may describe a volley / pattern / multiple projectiles

ProjectileSpawnCommand
= concrete materialization request
= exactly one projectile entity worth of data
```

This is an important naming boundary. The architecture should probably treat **events as semantic/gameplay-level** and **commands as entity-allocation-level**.

Suggested wording for the doc later:

```text
Spawn events are not the final unit of allocation. A spawn event may represent a gameplay-level action such as a volley, burst, fan, ring, impact explosion, or timed child release. The spawn system expands these events into concrete spawn commands. A spawn command represents exactly one entity to initialize.

This keeps gameplay expression compact while keeping entity materialization simple and uniform.
```

Updated pipeline shape:

```text
Producer Systems
  └─ produce type-specific spawn events
       e.g. ProjectileSpawnEvent, AoeSpawnEvent

ProjectileSpawnSystem
  ├─ read ProjectileSpawnEvents
  ├─ expand volley/pattern events into ProjectileSpawnCommands
  ├─ find disabled projectile slots
  ├─ initialize reused slots directly
  └─ create ECB entities only for remaining commands

AoeSpawnSystem
  ├─ read AoeSpawnEvents
  ├─ expand if needed
  ├─ reuse disabled AoE slots
  └─ ECB fallback for overflow
```

This also means the “spawn system” is not just an apply system. It is a **type-specific spawn pipeline** with three responsibilities:

1. **Expand** gameplay-level events into concrete commands.
2. **Reuse** inactive entities where possible.
3. **Fallback-create** entities only when reuse capacity is insufficient.

Next high-impact question:

For projectile volleys/patterns, where should the expansion logic live?

A. **Inside `ProjectileSpawnSystem` only**
All projectile events go through one expansion path. Best for centralizing projectile spawn rules.

B. **Separate `ProjectileVolleyExpansionSystem` before spawn allocation**
Produces `ProjectileSpawnCommand`s, then `ProjectileSpawnSystem` only allocates/reuses. Cleaner separation, but adds another phase/container.

C. **Inside producer systems**
Timed spawn/collision/etc. directly produce concrete `ProjectileSpawnCommand`s. Simplest spawn system, but duplicates volley expansion logic across producers.

My current assumption is **A** unless expansion becomes large enough to justify splitting it later.

B, for clarity, there needs to be some kind of separation of concern. 

Working assumptions updated:

* Projectile spawning has at least two explicit phases:

```text
ProjectileSpawnEvent
  = gameplay-level spawn request
  = may represent a volley, ring, burst, fan, child pattern, etc.

ProjectileSpawnCommand
  = allocation-level spawn request
  = exactly one concrete projectile
```

* The pipeline should separate concerns:

```text
Producer Systems
  └─ write ProjectileSpawnEvent / AoeSpawnEvent

ProjectileSpawnExpansionSystem
  └─ expands ProjectileSpawnEvent into ProjectileSpawnCommand

ProjectileSpawnApplySystem
  ├─ reuses disabled projectile slots
  └─ uses ECB only for overflow

AoeSpawnExpansionSystem, if needed
  └─ expands AoeSpawnEvent into AoeSpawnCommand

AoeSpawnApplySystem
  ├─ reuses disabled AoE slots
  └─ uses ECB only for overflow
```

* This is slightly more plumbing, but better for clarity:

  * producers do not care about allocation;
  * expansion does not care about entity reuse;
  * apply systems do not care about volley semantics;
  * each phase has a clear input and output.

Doc principle:

```text
A system should not both interpret gameplay intent and solve entity allocation unless those responsibilities are inseparable.
```

Next high-impact question:

How should reusable/dead slots be tracked?

A. **Query disabled entities when spawning**
Spawn apply systems query entities with disabled `Active`/enableable state and reuse whatever they find. Simple and robust, but may scan more than needed.

B. **Maintain a free-list per spawned entity type**
When lifetime/collision disables an entity, it registers itself into a projectile/AoE free-list. Spawn apply systems pop from the free-list. Fast allocation path, but more state to keep correct.

C. **Hybrid opportunistic query**
Spawn apply systems query chunks likely to contain dead slots and reuse them until commands are satisfied, without trying to build a perfect free-list. Overflow goes to ECB. This matches what you described so far.

D. **Preallocated pool with fixed capacity**
Projectile/AoE entities are allocated upfront. Runtime spawning only enables/reinitializes existing entities. Very predictable, but less flexible.

WithDisabled<T>() matches chunks that must have T, but only matches entities where T is disabled.

Good correction. Working assumptions updated:

* Dead-slot reuse does **not** require a separate free-list by default.
* Spawn apply systems can directly query reusable entities using disabled enableable components.
* `WithDisabled<T>()` gives a clean reuse query shape:

```csharp
// Conceptually:
Query entities that have T,
but where T is currently disabled.
```

So the preferred reuse model becomes:

```text
ProjectileSpawnApplySystem
  ├─ query disabled reusable projectile entities
  ├─ initialize as many as needed
  ├─ enable their active/runtime component
  └─ ECB-create only remaining commands
```

This makes the earlier “query disabled entities” option stronger than I described. It is not a blind full active-entity scan if the query is shaped correctly.

Updated doc principle:

```text
Reusable entities are not tracked by an external free-list unless profiling proves it necessary. 
The source of truth for slot availability is the enabled state of a runtime component.
```

Next high-impact question:

Which component should be used as the reusable slot state?

A. **Generic `Active : IEnableableComponent`**
Used by projectiles, AoEs, maybe future runtime entities. Reuse queries combine `WithDisabled<Active>()` with entity-type components.

B. **Type-specific enableable components**
Examples: `ProjectileActive`, `AoeActive`. Clearer queries and harder to misuse, but more duplicated concepts.

C. **Use the main data component itself as enableable**
Examples: `Projectile : IComponentData, IEnableableComponent`, `Aoe : IComponentData, IEnableableComponent`. Compact, but mixes “data exists” with “entity is alive/active.”

D. **Use Unity’s built-in disabled entity state**
Probably not preferred here because reusable entities should still be visible to pool/reuse systems.

A Generic Active

Working assumptions updated:

* Reusable slot state is a generic:

```csharp
public struct Active : IComponentData, IEnableableComponent {}
```

* Entity type is still determined by normal marker/data components:

```text
Projectile slot = has Projectile components + Active disabled
AoE slot        = has AoE components + Active disabled
```

* Lifetime/death does not destroy entities. It disables `Active`.
* Spawn apply systems look for disabled `Active` entities with the required archetype/type components.
* No free-list unless profiling later proves the query-based reuse path is insufficient.

Current reuse shape:

```text
LifetimeSystem
  └─ when lifetime <= 0:
       SetComponentEnabled<Active>(entity, false)

ProjectileSpawnApplySystem
  └─ query:
       Projectile components
       Active disabled
     then:
       overwrite snapshot data
       reset lifetime/contact/tracking/etc.
       SetComponentEnabled<Active>(entity, true)
```

Important doc principle:

```text
Active is the runtime occupancy flag for reusable entities. 
The presence of components defines what kind of slot the entity is.
The enabled state defines whether the slot is currently alive.
```

Next high-impact question:

How should spawn events and spawn commands be stored between phases?

A. **Singleton dynamic buffers per type**
Example: `ProjectileSpawnEventBuffer`, `ProjectileSpawnCommandBuffer`. Very ECS-visible and inspectable, but parallel writes need careful handling.

B. **System-owned native containers per type**
Example: `NativeList<ProjectileSpawnEvent>`, `NativeList<ProjectileSpawnCommand>`, cleared every tick. Clear phase ownership, less ECS-visible.

C. **NativeStream / per-worker native containers**
Good for high-volume parallel producers. Reader phase runs only after writers finish. More boilerplate, but avoids write contention.

D. **Hybrid**
Use native containers for runtime performance, and optionally expose debug summaries/samples to ECS buffers or logs.

A singleton
since reuse is per entity archetype, singleton fits very nicely. can easily create the unique entity with the component needed.

Working assumptions updated:

* Spawn events and spawn commands should live in **singleton dynamic buffers**.
* There should be separate singleton queue entities per spawn entity type / phase, for example:

```csharp
public struct ProjectileSpawnEventQueueTag : IComponentData {}
public struct ProjectileSpawnCommandQueueTag : IComponentData {}

public struct ProjectileSpawnEvent : IBufferElementData
{
    // High-level gameplay intent.
    // May describe volley / burst / pattern.
}

public struct ProjectileSpawnCommand : IBufferElementData
{
    // One concrete projectile.
}
```

* This fits the design because each spawn apply system has a clear singleton input:

```text
ProjectileSpawnEventQueue
  -> ProjectileSpawnExpansionSystem
  -> ProjectileSpawnCommandQueue
  -> ProjectileSpawnApplySystem
```

* Singleton queues are also easy to inspect, clear, and reason about.
* Reuse remains archetype-based:

```text
ProjectileSpawnApplySystem
  reads ProjectileSpawnCommandQueue
  queries disabled Active projectile slots
  reinitializes matching slots
  ECB-creates only overflow
```

Important doc principle:

```text
Spawn queues are explicit ECS state. They are not hidden inside private system-owned containers unless profiling later proves that dynamic buffers are the bottleneck.
```

One architectural concern this creates: **who is allowed to write to the singleton buffers, and how?** Dynamic buffers are clean, but parallel append from multiple producer jobs needs a deliberate rule.

Next high-impact question:

How should producers write spawn events into singleton buffers?

A. **Only single-thread systems write spawn events**
Simplest and most inspectable. Fine if spawn event volume is low.

B. **Parallel producers write through ECB `AppendToBuffer`**
More scalable, still ECS-native, but event ordering is less direct and playback becomes a defined phase.

C. **Parallel producers write to temporary native containers, then one merge system appends to singleton buffers**
Keeps singleton buffers as the official queue, avoids buffer write contention, but adds a merge phase.

D. **Mixed rule**
Low-volume producers write directly; high-volume producers use native temp containers then merge.

parallel

Working assumptions updated:

* Spawn producers may run in parallel.
* Singleton dynamic buffers remain the **official ECS-visible queues**, but producers should not all directly mutate the same buffer unsafely.
* The design needs an explicit parallel-write rule for event production.

Important distinction:

```text
Official queue:
  Singleton DynamicBuffer<ProjectileSpawnEvent>

Parallel write mechanism:
  Needs a safe append path before the singleton buffer is finalized for the next phase.
```

So the pipeline becomes more like:

```text
Parallel Producer Jobs
  └─ produce spawn events safely

Spawn Event Queue Finalization
  └─ singleton ProjectileSpawnEvent buffer is ready

Spawn Expansion System
  └─ ProjectileSpawnEvent -> ProjectileSpawnCommand

Spawn Apply System
  └─ reuse disabled slots, ECB-create remainder
```

Next high-impact question:

For parallel producers writing to singleton spawn queues, which rule do you prefer?

A. **ECB append-to-buffer**
Parallel jobs use `EntityCommandBuffer.ParallelWriter.AppendToBuffer(singleton, event)`. Playback fills the singleton buffer before expansion. ECS-native and simple conceptually, but adds ECB playback as a required phase.

B. **Temporary native parallel containers, then merge into singleton buffer**
Parallel jobs write to `NativeStream`, per-worker `NativeList`, or similar. A finalize system copies/merges into the singleton dynamic buffer. More explicit and likely easier to control ordering/capacity, but more plumbing.

C. **Direct singleton buffer only from single-thread finalizers**
Parallel producers write local/native output, but only one finalizer touches ECS buffers. This is similar to B, but with a stricter rule: no producer job writes ECS buffers directly.

A but post expansion, Commands cannot be converted to entities with components without touching expansion logic which belongs to another system. 
Ruse also cannot reuse dead slots unless Spawn Events have been expanded into spawn command.

Working assumptions updated:

* Parallel producers may use **ECB `AppendToBuffer`** to write spawn **events** into singleton dynamic buffers.
* This applies to **pre-expansion event production**, not entity materialization.
* Entity materialization must happen **after expansion**, because reuse/allocation needs concrete one-entity commands.
* Spawn apply systems should never need to understand volley/ring/burst logic.
* Expansion logic belongs only in expansion systems.

Corrected pipeline:

```text
Parallel Producer Systems
  └─ ECB.AppendToBuffer(ProjectileSpawnEventQueue, ProjectileSpawnEvent)
  └─ ECB.AppendToBuffer(AoeSpawnEventQueue, AoeSpawnEvent)

ECB Playback / Event Queue Finalization
  └─ singleton event buffers now contain this tick's spawn events

ProjectileSpawnExpansionSystem
  └─ ProjectileSpawnEvent -> ProjectileSpawnCommand[]
  └─ writes ProjectileSpawnCommandQueue

AoeSpawnExpansionSystem
  └─ AoeSpawnEvent -> AoeSpawnCommand[]
  └─ writes AoeSpawnCommandQueue

ProjectileSpawnApplySystem
  ├─ reads ProjectileSpawnCommandQueue
  ├─ queries disabled Active projectile slots
  ├─ initializes reused entities
  └─ ECB-creates overflow entities

AoeSpawnApplySystem
  ├─ reads AoeSpawnCommandQueue
  ├─ queries disabled Active AoE slots
  ├─ initializes reused entities
  └─ ECB-creates overflow entities
```

Important doc rule:

```text
Spawn events are allowed to be abstract. Spawn commands are not.

Only spawn commands may reach the reuse/allocation phase, because reuse needs to know exactly which entity archetype and component values are required.
```

Another important rule:

```text
Spawn apply systems must not expand gameplay patterns.

If a spawn apply system needs to know whether an event is a fan, ring, volley, timed child, or impact spawn, the phase boundary has failed.
```

So the `ECB.AppendToBuffer` choice is specifically for **parallel event production into singleton queues**. It should not collapse expansion and allocation back into one system.

Next high-impact question:

Should `ProjectileSpawnExpansionSystem` write commands **directly to the singleton command buffer**, or should expansion also use ECB append?

A. **Direct write to command buffer**
Expansion runs after producer ECB playback, owns the command buffer, clears it, expands events, appends commands. Cleanest if expansion is single-threaded or internally controlled.

B. **ECB append to command buffer**
Useful if expansion itself is parallel. Adds another playback/finalization phase before apply.

C. **Temporary native command container, then copy into singleton command buffer**
Best if expansion is parallel and high-volume, while keeping the ECS command buffer as the official inspectable queue.

expand into another container, expand system should only expand, spawn system should only spawn. 

Working assumptions updated:

* Expansion output should go into a **separate command container/queue**.
* `ProjectileSpawnExpansionSystem` should only do:

```text
ProjectileSpawnEvent -> ProjectileSpawnCommand
```

* `ProjectileSpawnApplySystem` should only do:

```text
ProjectileSpawnCommand -> reused entity or new ECB entity
```

* No system should both interpret volley/pattern logic and allocate entities.
* This means each spawn type has a clean three-stage shape:

```text
Event Production
  produces abstract spawn events

Expansion
  expands abstract events into concrete commands

Spawn Application
  materializes concrete commands into entities
```

The current architecture rule is now:

```text
Events are gameplay intent.
Commands are allocation intent.
Spawn systems consume commands only.
```

For singleton buffers, this likely means:

```text
ProjectileSpawnEventQueue singleton
ProjectileSpawnCommandQueue singleton
AoeSpawnEventQueue singleton
AoeSpawnCommandQueue singleton
```

Even if the expansion system writes directly to the command buffer, conceptually it is writing to a different phase-owned container, not spawning.

Next high-impact question:

Should command queues be **ECS dynamic buffers** as well, or can they be native containers?

A. **Dynamic buffers for both events and commands**
Most inspectable. The whole pipeline is visible in ECS. Slightly more ceremony and possible buffer capacity concerns.

B. **Dynamic buffers for events, native containers for commands**
Events stay ECS-visible; commands are a high-volume internal allocation list. Cleaner performance path, but commands are less visible in Entity Debugger.

C. **Native containers for expansion output, then optional debug copy/sample**
Treat commands as implementation detail. Add debug tools only when needed.

native container used by producers and consumers should be contention free since we guarantee writers are done before readers start.
use something like NativeQueue.ParalleWriter for multi threaded writes and then convert to NativeArray for multi threaded reads. We can guarantee reads won't overlap with indexes, and Writer to the native queue is guaranteed to finish writing before readers start.

Working assumptions updated:

* Hot-path event/command queues should be **native containers**, not necessarily ECS dynamic buffers.
* Producers may write in parallel using something like:

```csharp
NativeQueue<T>.ParallelWriter
```

* Readers should not consume the queue directly while writers are active.
* The intended phase boundary is:

```text
Parallel write phase
  -> complete producer jobs
  -> freeze / convert queue to NativeArray
  -> parallel read phase by non-overlapping index ranges
```

* This makes the container effectively contention-free from the consumer side:

```text
NativeQueue<T>         // append-oriented producer container
NativeArray<T>         // indexed, partitionable consumer input
```

* The design should not require perfect ordering unless explicitly needed. A parallel queue should be treated as **unordered** unless the system adds ordering metadata.
* Singleton entities may still be useful as **ownership/phase markers**, but the actual high-volume data path should probably be system-owned native containers.

Updated pipeline shape:

```text
Projectile spawn producers
  -> NativeQueue<ProjectileSpawnEvent>.ParallelWriter

Producer dependency completes

ProjectileSpawnEventFinalizeSystem
  -> converts queue to NativeArray<ProjectileSpawnEvent>

ProjectileSpawnExpansionSystem
  -> reads event array in parallel
  -> writes NativeQueue<ProjectileSpawnCommand>.ParallelWriter

Expansion dependency completes

ProjectileSpawnCommandFinalizeSystem
  -> converts command queue to NativeArray<ProjectileSpawnCommand>

ProjectileSpawnApplySystem
  -> reads command array by index ranges
  -> reuses disabled Active projectile slots
  -> ECB-creates overflow
```

Doc rule:

```text
Queues are for parallel append.
Arrays are for parallel consume.
No system reads from a queue that is still being written.
No system mutates command data after it has been finalized into an array.
```

One important correction to the earlier singleton assumption:

```text
Singleton dynamic buffers are good for visibility, but not ideal as the primary high-volume queue if many jobs write in parallel.
For the rewrite, singleton entities can define ownership and lifecycle, while native containers carry the hot-path data.
```

Next high-impact question:

How should these native queues/arrays be **owned and reset**?

A. **System-owned persistent containers**
Each pipeline system owns its `NativeQueue` / `NativeArray` fields, clears/disposes them in `OnCreate`/`OnDestroy`. Simple and explicit, but ownership is hidden inside systems.

B. **A dedicated spawn pipeline state object/system**
One system owns all spawn queues/arrays for a spawn type, and producer/expansion/apply systems access them through a known pipeline API or singleton-like owner. Clear central ownership, more plumbing.

C. **Singleton ECS entities only define queue ownership; native containers remain system-owned internally**
The singleton exists for discoverability/debugging/configuration, but native containers are not stored in ECS components. Good compromise.

My current assumption is **C**: singleton entities for ECS-visible ownership/config/debug, native containers for the actual high-volume transport.

for native queue, reader always call clear.
for native stream where clear is not an option, reader always dispose and write always create.
writer can use some defensive checking, ensure queue is empty before scheduling workers to write into it or check if the stream is already disposed or not before recreating.

Working assumptions updated:

* For `NativeQueue<T>`:

  * writer phase assumes the queue is empty before scheduling writes;
  * producer jobs use `ParallelWriter`;
  * reader/finalizer consumes or converts the queue;
  * **reader is responsible for `Clear()`** after consumption.

* For `NativeStream`:

  * no long-lived clear/reuse assumption;
  * **writer creates** the stream for the write phase;
  * **reader disposes** the stream after consumption;
  * writer can defensively check that the previous stream has already been disposed before recreating.

* Defensive checks are acceptable in the owner system:

  * queue must be empty before scheduling producers;
  * stream must not already be allocated/live before creating;
  * reader must clear/dispose after consuming.

This gives a good ownership rule:

```text
The writer owns creation/readiness before the write phase.
The reader owns cleanup after the read phase.
No container crosses a phase boundary in a partially consumed state.
```

For the doc, I would phrase the native container rule like this:

```text
Native containers are phase-local transport, not long-term state.

For reusable containers such as NativeQueue, the reader clears the container after it has fully consumed or finalized the data. Writers may assert that the queue is empty before scheduling parallel writes.

For non-clearable containers such as NativeStream, the writer creates the stream at the start of the write phase, and the reader disposes it after the read phase. This prevents unclear ownership and avoids carrying partially consumed streams across ticks.
```

Next high-impact question:

Do spawn commands need **stable ordering**?

A. **No stable ordering required**
Parallel queue order is acceptable. Spawn visual/gameplay order may vary as long as the final set of spawned entities is correct.

B. **Stable order only within one event**
A volley/ring/fan should expand deterministically internally, but different events may interleave arbitrarily.

C. **Stable order across all events in a tick**
Every spawn event/command needs an order key, likely `Tick + ProducerOrder + LocalIndex`, and commands may need sorting before spawn apply.

D. **Stable order only for debugging**
Runtime does not require it, but debug builds may attach order/source metadata.

no stable ordering. 

Working assumptions updated:

* **No stable ordering requirement** for spawn events or spawn commands.
* Parallel queue ordering is acceptable.
* Spawn apply does not need sorting.
* If order/source metadata exists, it is for debugging only, not correctness.

Doc rule:

```text
Spawn queues are unordered transport.

Systems must not rely on the order in which events or commands are produced, expanded, reused, or ECB-created. Correctness must come from the command data itself, not from queue order.
```

Current spawn pipeline assumptions:

```text
Parallel producers
  -> unordered NativeQueue<SpawnEvent>.ParallelWriter

Finalize
  -> NativeArray<SpawnEvent>

Expansion systems
  -> unordered NativeQueue<SpawnCommand>.ParallelWriter

Finalize
  -> NativeArray<SpawnCommand>

Spawn apply systems
  -> read non-overlapping command index ranges
  -> reuse disabled Active slots
  -> ECB-create overflow
```

Next high-impact question:

How should **snapshotted recursive spawn data** be represented inside components/events?

A. **Fixed-depth nested structs**
Example: projectile snapshot contains optional timed child spawn snapshot, which contains optional impact AoE snapshot. Simple and Burst-friendly, but can get bulky.

B. **Separate optional components per behavior**
Example: spawned projectile entity gets `TimedProjectileSpawn`, `ImpactAoeSpawn`, etc. Each component contains the full raw snapshot it needs. Cleaner ECS queries, but duplicated data across components.

C. **BlobAssetReference for immutable authoring data + copied runtime overrides**
Less duplication, but violates the “snapshot everything” rule unless the blob is treated as immutable definition-only data.

D. **Hybrid: snapshot runtime-affecting data, reference immutable static shape data**
Example: damage/lifetime/position/timing are copied, but pattern geometry or visual metadata may reference immutable blobs.

My current assumption is **B**, with fixed-depth raw snapshot structs inside the optional behavior components where needed.

fixed depth nested structs. Below is the snap shotting design decision doc, type names might not be the same after the rewrite the but idea stays.

Decision
- Use Option A: fat fire-time snapshot payloads. All data required later to emit cross-domain spawn commands is copied into plain-data payload fields on the spawn command and carried through projectile/AOE entities into the collision-time hit streams. No live managed object lookups (no `GameObject`, `Transform`, `Collider2D`, or managed attack callbacks) are invoked during ECS collision. 

Why this choice
- Lifetime safety: in-flight projectiles must not depend on the continued existence or unmutated state of authored attack objects.
- Determinism: collision systems operate using plain, immutable data; no pointer-chasing or version semantics to reason about in jobs.
- Performance: collision and spawn-path code stays allocation-free and cache-friendly; the only step that still crosses to managed code is small (damage replay) and runs off the hot path.
- Implementation complexity: easiest to reason about and test; matches existing direct-damage snapshotting behavior.

Working assumptions updated:

* The rewrite should preserve the existing **snapshotting decision**.
* Runtime ECS systems should use **fat fire-time snapshot payloads**.
* Any data needed later for timed spawn, impact spawn, AoE spawn, or damage replay must be copied into plain unmanaged data before entering the hot ECS path.
* No ECS collision/spawn-path system should call back into:

  * `GameObject`
  * `Transform`
  * `Collider2D`
  * managed attack objects
  * managed callbacks
  * centralized mutable authoring/runtime definition storage
* In-flight projectiles/AoEs must remain valid even if authored objects are destroyed or mutated later.
* The tradeoff is accepted: payloads may be larger, but lifetime safety, determinism, Burst/job compatibility, and debuggability are more valuable.
* Recursive spawn data is represented through **fixed-depth nested plain structs**, with recursion intentionally capped.

Doc principle:

```text
Snapshot data is the runtime contract.

Once a projectile or AoE is spawned, ECS systems must not ask authoring objects what the projectile means. The entity already carries the data needed to finish its lifetime, spawn children, emit impact effects, and produce damage replay data.
```

The next missing architectural detail is how “optional behavior” is represented when using fat fixed-depth snapshots.

Question:

How should optional spawn behaviors be encoded on entities?

A. **Separate behavior components with fat snapshot payloads**
Example: entity may have `TimedProjectileSpawn`, `ImpactAoeSpawn`, `ContactDamage`, etc. Each component contains fixed-depth raw data. Systems query only entities that actually have that behavior.

B. **One large projectile snapshot component with flags**
Example: `ProjectileRuntimeSnapshot` contains `HasTimedSpawn`, `TimedSpawnPayload`, `HasImpactAoe`, `ImpactAoePayload`, etc. Fewer components/archetypes, but systems must branch over flags.

C. **Hybrid**
Use separate components for high-level behavior presence, but each component contains fixed-depth nested structs. For example, `TimedProjectileSpawn` exists only when needed, and its payload may contain a full child projectile snapshot plus child impact AoE snapshot.

My current assumption is **C**, because it keeps ECS queries clean while preserving the snapshotting rule.

A component

Working assumptions updated:

* Optional behavior should be represented by **separate ECS components**, not flags inside one giant projectile component.
* Each behavior component owns the full snapshot payload needed by that behavior.

Example shape:

```csharp
public struct TimedProjectileSpawn : IComponentData
{
    public float Interval;
    public float TimeUntilNextSpawn;
    public int RemainingCount;

    // Full fixed-depth snapshot needed to produce ProjectileSpawnEvent later.
    public ProjectileSpawnSnapshot ChildProjectile;
}

public struct ImpactAoeSpawn : IComponentData
{
    // Full fixed-depth snapshot needed to produce AoeSpawnEvent on collision.
    public AoeSpawnSnapshot Aoe;
}

public struct ContactDamage : IComponentData
{
    // Plain copied damage data needed for hit/damage replay.
    public DamageSnapshot Damage;
}
```

Doc rule:

```text
Behavior presence is represented by component presence.

Systems should not scan a giant projectile snapshot and branch on many flags. 
If an entity can perform timed spawn, it has a timed spawn component. 
If it can spawn AoE on impact, it has an impact AoE spawn component.
```

This fits the rewrite goal well because it makes system ownership obvious:

```text
TimedProjectileSpawnSystem
  queries TimedProjectileSpawn + Active

ImpactAoeSpawnSystem / collision phase
  reads ImpactAoeSpawn only when collision occurs

ContactDamageSystem
  reads ContactDamage only for entities that can emit damage
```

Next high-impact question:

What is the intended **damage target model** after the rewrite?

A. **Targets become ECS entities with `Health` components**
Damage application stays mostly inside DOTS.

B. **Targets remain scene/GameObject nodes**
ECS produces damage replay events, then a managed bridge applies damage off the hot collision path.

C. **Hybrid**
Simple combat state like health is in ECS, but scene nodes still receive replay events for animation, UI, death behavior, etc.

D. **Undecided**
The rewrite doc should describe damage as a separate unresolved design area with constraints and candidate approaches.

B for now, C later. animations in unity is still mainly designed to work with game objects. 

Working assumptions updated:

* **Damage targets remain GameObject/scene nodes for now.**
* ECS should produce plain-data damage replay events, then a managed bridge applies them outside the hot ECS collision path.
* Longer-term direction may be **hybrid**:

  * ECS owns simple combat state like health;
  * GameObjects still handle animation, visual feedback, death animation, UI, and authored behavior.
* The rewrite should not force target animation/behavior fully into ECS yet.
* Damage application is therefore a boundary system:

```text id="dji5oi"
ECS collision / hit detection
  -> plain unmanaged damage replay data
  -> managed bridge
  -> GameObject target / animation / behavior
```

Doc rule:

```text id="nkdo88"
The ECS combat pipeline may detect and describe damage, but for now it does not directly own the full target lifecycle. Scene targets remain the authority for animation-driven behavior.
```

Next high-impact question:

What should the **damage replay semantics** be?

A. **Atomic hit replay**
Every hit is preserved as a separate damage replay event. Best for “on hit” effects, lifesteal, proc logic, and accurate hit count, but can be high-volume.

B. **Condensed per target per tick**
Many hits against the same target are merged into one replay event per tick. Lower bridge cost, but loses atomic hit semantics unless extra count/metadata is included.

C. **Hybrid: atomic inside ECS, condensed at managed bridge**
ECS keeps all hit events long enough for ECS-side logic, then bridge sends condensed results to GameObjects. Good compromise if target GameObjects only need final damage.

D. **Typed damage channels**
Some damage types are atomic, others are condensed. Example: direct projectile hits atomic, AoE tick damage condensed. More flexible, but more rules.

A for now, C later.

Working assumptions updated:

* **Damage replay is atomic for now.**
* Every valid hit should produce its own damage replay event.
* Do not condense damage per target per tick in the first rewrite.
* This preserves semantics for:

  * on-hit effects,
  * lifesteal,
  * proc logic,
  * hit counters,
  * “gain X when hit” style gameplay.
* Future direction may become **C: atomic inside ECS, condensed at managed bridge**, but that is not part of the immediate architecture unless bridge cost becomes a proven bottleneck.

Current damage boundary:

```text
Projectile / AoE collision
  -> atomic HitEvent / DamageReplayEvent
  -> managed replay bridge
  -> GameObject target receives damage / animation / callbacks
```

Doc rule:

```text
Damage replay preserves hit identity.

The first rewrite prioritizes semantic correctness and debuggability over minimizing bridge call count. Condensation may be added later as an explicit bridge optimization, but it must not silently change gameplay semantics.
```

Next high-impact question:

How should ECS identify GameObject targets in damage replay events?

A. **Stable integer `TargetId`**
Scene targets register into a managed target registry. ECS stores only `int TargetId`. Damage replay uses the ID to find the GameObject target. Cleanest for unmanaged ECS data.

B. **ECS proxy entity per GameObject target**
Each scene target has an ECS entity proxy. Hits store the target entity, and the bridge maps entity → GameObject. More DOTS-friendly, but adds proxy lifecycle work.

C. **Index into a native target array**
Collision uses dense target indices, and replay events carry that index. Fast, but needs careful sync when targets are added/removed.

D. **Current existing method**
Keep whatever target reference/index system already exists, document it as the boundary, and only revisit later.

B, and this method also enables the game object to push it's own position into ecs in update()

Working assumptions updated:

* Damage replay should reference an **ECS proxy entity** for each GameObject target.
* Scene/GameObject target remains the gameplay/animation owner for now.
* ECS proxy entity exists so DOTS systems can:

  * read target position,
  * participate in collision/spatial queries,
  * be referenced by unmanaged hit/damage events,
  * avoid storing managed object references in ECS data.
* GameObject target can push its own transform/state into ECS during `Update()`.
* Damage replay can then carry:

```csharp
public struct DamageReplayEvent
{
    public Entity TargetProxy;
    public DamageSnapshot Damage;
    public float2 HitPosition;
    public float2 HitDirection;
    // maybe source/projectile metadata if needed
}
```

Current target bridge shape:

```text
GameObject Target
  ├─ owns animation / behavior / final damage callback
  ├─ holds Entity TargetProxy
  └─ pushes position/state into ECS

ECS TargetProxy Entity
  ├─ TargetPosition
  ├─ TargetCollisionShape
  ├─ maybe TargetAlive / Team / Faction
  └─ referenced by collision and damage replay events

ECS DamageReplayEvent
  └─ TargetProxy Entity + atomic damage snapshot

Managed DamageReplayBridge
  └─ maps TargetProxy Entity back to GameObject target
```

Doc rule:

```text
The proxy entity is the unmanaged identity of a scene target inside ECS.
The GameObject remains the managed owner of animation and behavior.
```

Next high-impact question:

Who owns the **target proxy lifecycle**?

A. **GameObject owns proxy lifecycle**
Target MonoBehaviour creates/registers proxy on enable, pushes position each `Update`, disables/destroys proxy on disable/death. Simple bridge ownership.

B. **ECS owns proxy lifecycle**
GameObject requests a target proxy, but ECS systems create/disable/recycle it. Cleaner ECS ownership, more bridge plumbing.

C. **Hybrid pooled proxies**
GameObject owns registration, but proxies are reused like projectiles with `Active`. Good if targets spawn/despawn often, but probably unnecessary for ~hundreds of targets.

My current assumption is **A**.

A, when game object dies, usually due to damage, since damage is a callback death happens on game object, so game object should own it. 

Working assumptions updated:

* **GameObject owns target proxy lifecycle.**
* Target `MonoBehaviour`:

  * creates/registers its ECS proxy on enable/spawn;
  * pushes position/collision/state into ECS during `Update`;
  * receives damage replay callbacks;
  * handles death, animation, cleanup;
  * disables/destroys/unregisters the proxy when it dies or is disabled.
* This ownership is correct because death currently happens through managed damage callbacks on the GameObject side.
* ECS should not try to own final target lifetime yet.

Current bridge rule:

```text
GameObject target is the source of truth for target lifecycle.
ECS target proxy is only the unmanaged representation used by simulation systems.
```

Suggested doc wording:

```text
Target proxies are owned by their GameObject targets.

A target GameObject creates and registers its ECS proxy when it becomes active, pushes its position and collision state into ECS, and removes or disables the proxy when the GameObject dies. This keeps lifecycle ownership aligned with the current damage model: damage replay invokes managed callbacks, and those callbacks may trigger death, animation, and scene-tree cleanup.
```

Next high-impact question:

How should ECS handle damage events whose `TargetProxy` is no longer valid by replay time?

A. **Drop invalid target events silently**
Simple and safe. If the target died earlier in the same frame/tick, later replay events are ignored.

B. **Drop invalid target events with debug counters**
Same runtime behavior, but track dropped damage events for debugging/profiling.

C. **Proxy has `Active` / `Alive` state and bridge checks it**
Damage events may reference the proxy entity, but replay only applies if the proxy is still active/alive.

D. **Keep target valid until end-of-tick replay completes**
GameObjects that die defer proxy cleanup until after damage replay, preserving all events in the tick. More deterministic, but requires lifecycle coordination.

death (when the game object is destroyed and corresponding entity deleted) should be handled in lateupdate(), damage dispatch should happen in update. this should guarantee the proxy to always be alive during update(), state transition happens after simulation. 

Working assumptions updated:

* `TargetProxy` should be considered valid for the whole **simulation + damage dispatch window**.
* Damage replay dispatch happens in `Update()`.
* Target death/destruction/proxy deletion happens in `LateUpdate()`.
* This creates a frame-level lifecycle rule:

```text
Update:
  GameObjects push target state into ECS
  ECS simulation / collision / damage event production runs
  Damage replay bridge dispatches callbacks to GameObjects

LateUpdate:
  GameObjects that died from callbacks perform destruction / cleanup
  Corresponding ECS target proxies are deleted or disabled
```

* Therefore damage replay does not need to handle normal “proxy deleted during dispatch” cases, because deletion is deferred until after dispatch.
* Defensive validation can still exist, but it should be treated as protection against bugs/lifecycle violations, not normal control flow.

Doc rule:

```text
Target proxy deletion is deferred until after damage replay.

A GameObject may decide that it is dead during damage callback execution, but it must not immediately destroy its ECS proxy while the current damage dispatch is still running. The actual scene cleanup and proxy deletion happen in LateUpdate, after the simulation/replay phase has finished.
```

This makes the target bridge much cleaner.

Next high-impact question:

Where does the **ECS simulation step** sit relative to MonoBehaviour `Update()`?

A. **Mono Update pushes target state first, then ECS simulation runs, then damage dispatch runs in the same Update phase**
Best if target positions must be current before collision.

B. **ECS simulation runs first, then Mono Update dispatches damage**
Simpler if ECS world updates through Unity’s default system loop before scripts, but target state may be one frame behind unless pushed earlier.

C. **Manual ECS simulation from a combat driver MonoBehaviour**
A `CombatRuntimeDriver.Update()` explicitly performs: push state → run ECS systems → dispatch damage → LateUpdate cleanup. Most explicit and easiest to document/control.

D. **Current Unity default order; document assumptions only**
Avoid controlling player loop for now, but this can become fragile.

all mono behaviour update() 
ecs simulation
lateupdate()

game object can remove itself from ecs prior to the game object getting deleted from the scene tree. 
something like on death animation will require the GO to still be there but it should not participate in simulation. 

Working assumptions updated:

* Frame order should be treated as:

```text
MonoBehaviour Update()
  - GameObjects push current target state into ECS.
  - GameObjects may also unregister themselves from ECS if they should no longer participate.

ECS Simulation
  - projectile movement/lifetime/collision/tracking/spawn/damage event production
  - only registered/active target proxies participate.

Damage Dispatch
  - ECS damage replay events are dispatched back to GameObjects.

MonoBehaviour LateUpdate()
  - scene-tree cleanup
  - death animation state transitions
  - delayed destruction
```

* A GameObject can remain alive visually while being removed from ECS simulation.
* Death animation does **not** imply the target should still be hittable/collidable.
* Target simulation participation and GameObject lifetime are separate concepts.

Important doc rule:

```text
Scene lifetime and simulation lifetime are not the same.

A target GameObject may remain in the scene for animation, VFX, or cleanup after death, but its ECS proxy must be removed or disabled before the next ECS simulation step if it should no longer be targetable.
```

Next high-impact question:

When a GameObject stops participating in ECS simulation, should its target proxy be handled by:

A. **Destroy proxy entity immediately**
Simple and explicit. But any stale ECS event referencing it needs defensive validation.

B. **Disable `Active` on the proxy entity**
Keeps the proxy entity around while excluding it from simulation queries. Safer for temporary removal/death animation, and consistent with projectile/AoE reuse semantics.

C. **Separate `TargetSimActive : IEnableableComponent`**
Projectile/AoE use generic `Active`, but targets use a clearer target-specific simulation flag. Avoids mixing “pooled projectile slot active” with “scene target participates in simulation.”

D. **Hybrid**
Disable simulation immediately, destroy/unregister the proxy later during cleanup.

delete immediately. nothing should be referencing it at this point, all events should have already been resolved in LateUpdate(). 

Working assumptions updated:

* When a GameObject no longer participates in simulation, its ECS target proxy should be **destroyed immediately**, not disabled/preserved.
* This is safe because lifecycle ordering guarantees that no unresolved ECS damage events should still reference it.
* Any event that references a deleted proxy is a bug/lifecycle violation, not expected runtime behavior.
* Death animation can continue on the GameObject after ECS unregistration, but that GameObject is no longer targetable.

Doc rule:

```text
Target proxy lifetime ends when simulation participation ends.

The GameObject may continue to exist for death animation or visual cleanup, but once it should no longer be hit, tracked, or queried by ECS systems, its proxy entity is deleted. ECS events must be resolved before this deletion point.
```

Current frame/lifecycle model:

```text
MonoBehaviour Update
  - live targets push position/state into ECS
  - dead/non-simulating targets may unregister/delete proxy before simulation

ECS Simulation
  - only existing target proxies participate
  - collision produces atomic damage replay events
  - spawn events/commands are produced and consumed by staged systems

Damage Dispatch
  - atomic damage events are replayed to GameObjects

LateUpdate
  - GameObject visual cleanup / death animation transitions / scene destruction
  - no unresolved ECS events should remain
```

Next high-impact question:

Should the rewrite doc define a **strict ECS system phase order**?

A. **Yes, explicit phase groups**
Example: target state already pushed → lifetime → timed spawn event production → movement/tracking → collision → spawn expansion → spawn apply → damage replay export. Best for clarity/debugging.

B. **Yes, but only broad groups**
Example: input sync → simulation → event expansion → application → replay. Less rigid, easier to change.

C. **No, only local dependencies**
Each system declares update-before/update-after as needed. More flexible, but harder to understand as a full tick pipeline.

My current assumption is **A**, because the main rewrite goal is architecture clarity.

A

Working assumptions updated:

* The rewrite doc should define **strict ECS system phase groups**.
* System order is part of the architecture, not incidental scheduling.
* The goal is to make one tick easy to trace from input sync → simulation → event production → expansion → spawn application → damage replay.
* Local `[UpdateBefore]` / `[UpdateAfter]` dependencies are still useful, but the doc should first define the full intended phase order.

Current high-level phase model:

```text
1. MonoBehaviour Update
   - GameObjects push current target state into ECS.
   - GameObjects that should no longer participate delete their ECS target proxy.

2. ECS Simulation
   2.1 Lifetime / Active state update
   2.2 Timed spawn event production
   2.3 Projectile tracking / movement
   2.4 Projectile collision
   2.5 AoE collision
   2.6 Contact gate / hit filtering
   2.7 Spawn event finalization
   2.8 Spawn expansion: events -> commands
   2.9 Spawn apply: commands -> reused entities / ECB overflow
   2.10 Damage replay export

3. Damage Dispatch
   - Atomic damage replay events are sent to GameObject targets.

4. MonoBehaviour LateUpdate
   - Death animation transitions.
   - Scene cleanup.
   - GameObject destruction.
```

Important doc rule:

```text
The combat runtime is phase-oriented.

A system should have a clear phase, clear input ownership, and clear output ownership. 
Events produced in one phase are not consumed until the producing phase is complete.
```

One detail to settle before drafting the doc structure:

Should **spawned entities become active in the same ECS simulation tick**, or only become visible to simulation on the next tick?

A. **Same tick activation**
Spawn apply enables/reuses entities immediately. Later phases in the same tick may see them if ordered after spawn apply. More responsive, but phase order matters more.

B. **Next tick activation**
Spawn apply creates/enables entities after collision/damage phases, so new projectiles/AoEs do not participate until the next ECS tick. Simpler and avoids recursive same-tick effects.

C. **Mixed rule**
Some spawns are same-tick, but recursive/child/impact spawns are next-tick. More flexible, but easier to make confusing.

B

Working assumptions updated:

* Spawned entities should **not participate in simulation until the next ECS tick**.
* This applies to recursive spawns too:

  * projectile impact spawns AoE;
  * AoE does not collide immediately in the same tick;
  * child projectile does not move/collide immediately in the same tick.
* This strongly simplifies reasoning and prevents same-tick recursive chains.
* Spawn apply should happen late enough that new entities are initialized for the next tick, not fed back into the current tick’s collision/movement pipeline.

Doc rule:

```text
Spawn is a next-tick effect.

Spawn events produced during tick N are expanded and materialized during tick N, but the resulting entities do not participate in simulation until tick N+1. This prevents recursive same-tick behavior and keeps the simulation pipeline acyclic.
```

Updated phase shape:

```text
Tick N

1. Existing active entities simulate.
2. Existing entities produce spawn events and damage events.
3. Spawn events are expanded into commands.
4. Commands are materialized into reused/new entities.
5. Newly materialized entities become part of the world state for tick N+1.
```

Next high-impact question:

How should this **next-tick spawn rule** be enforced?

A. **By system ordering only**
Spawn apply runs after all movement/collision/damage-producing systems. New entities may be `Active = true` immediately, but no later simulation system will process them until next tick.

B. **By explicit `SpawnedThisTick` / `SimulationDelay` component**
Spawned entities are marked and skipped until the next tick clears the marker. Stronger guarantee, but adds extra state and cleanup.

C. **By keeping `Active = false` until next tick**
Spawn apply initializes reused/new entities but does not enable `Active`; a next-tick activation system enables them. Very explicit, but adds another phase and queue/state.

My current assumption is **A**, unless you want a stronger guardrail against accidental future systems running after spawn apply.

A by system execution order. 

Working assumptions updated:

* **Next-tick spawn visibility is enforced by system order only.**
* Spawn apply may initialize and enable entities during tick `N`, but all simulation systems that could process them have already run.
* No `SpawnedThisTick`, delayed activation component, or extra activation queue is required for now.
* This keeps the rule simple:

```text
Simulation phases run first.
Spawn materialization runs late.
New entities are visible to simulation on the next tick because no simulation phase remains in the current tick.
```

Doc rule:

```text
Spawned entities are next-tick participants by phase ordering, not by extra marker state.

The system group order must guarantee that movement, tracking, lifetime, collision, hit production, and timed-spawn production all run before spawn apply systems.
```

Next high-impact question:

Since optional behavior is represented by **component presence**, projectile/AoE slots can have different archetypes. How should spawn apply match commands to reusable dead slots?

A. **Normalize archetypes**
All projectile slots have the same broad set of components, with unused behavior disabled or empty. Reuse is simple, but entities carry unused components.

B. **Archetype key on spawn command**
Each `ProjectileSpawnCommand` carries a compact archetype/component-mask key. Spawn apply groups or filters commands by key, then reuses disabled slots with the matching component set.

C. **Separate spawn apply systems per archetype family**
Example: basic projectile spawn apply, timed-child projectile spawn apply, impact-AoE projectile spawn apply, etc. Very explicit, but more system/code duplication.

D. **Use prefab/prototype entities for archetype creation only**
Reuse still matches by component set, but ECB overflow instantiates from a prototype/prefab entity representing the required archetype.

use IEnableableComponent, reuse job can use entitiy query to query disabled entity list of a given archetype. 

Working assumptions updated:

* Reuse is based on `IEnableableComponent`, specifically disabled `Active`.
* Spawn apply should query **disabled entities for a given archetype/component set**, then overwrite data and enable `Active`.
* The reusable slot is valid only if its component set matches the command’s required entity shape.
* This avoids a generic “reuse any projectile slot” model. A projectile with timed child spawn, impact AoE spawn, contact damage, etc. must reuse a slot that already has the matching components.
* Optional behavior remains component-based, so archetype matters.

Current reuse model:

```text
ProjectileSpawnCommand
  -> belongs to a required projectile archetype/component set

ProjectileSpawnApplySystem
  -> query disabled Active entities with that archetype
  -> initialize component data
  -> enable Active
  -> overflow goes through ECB
```

Doc rule:

```text
Reusable entities are reused only within their archetype family.

A disabled projectile slot is not just “a projectile slot.” It is a slot with a specific component set. Reuse must preserve that shape so spawn apply can overwrite data without structural changes.
```

The remaining key piece is the **overflow path**, because when no reusable disabled slot exists, the system still needs to create the correct entity shape.

Next high-impact question:

How should overflow entities be created when reusable slots are exhausted?

A. **Prototype/prefab entity per archetype**
Each projectile/AoE archetype has a prototype entity. Overflow uses ECB instantiate, then overwrites snapshot/runtime data from the command. Clean and matches archetype reuse.

B. **Create entity from `EntityArchetype`**
Spawn apply maps command archetype key → `EntityArchetype`, creates entity, then sets all components. More explicit, less prefab/prototype dependency.

C. **ECB add components dynamically from command data**
Create a basic entity, then add needed components based on command. Flexible, but this reintroduces structural-change branching and is probably the least clean.

My current assumption is **A**: prototype entity per archetype family, because reuse and overflow should share the same component shape.

C is prefered, let unit handle new entity creation instead of manual. it's probably less structural change this way.

Working assumptions updated:

* Overflow creation should use **ECB-driven component creation**, not manually maintaining prototype/prefab entities per archetype.
* Reuse path still prefers disabled slots with matching component shape.
* Overflow path can do:

```text
ECB.CreateEntity()
ECB.AddComponent<Active>()
ECB.AddComponent<ProjectileCore>()
ECB.AddComponent<Lifetime>()
ECB.AddComponent<Movement>()
ECB.AddComponent<TimedProjectileSpawn>() // only if command requires it
ECB.AddComponent<ImpactAoeSpawn>()       // only if command requires it
...
```

* This keeps the creation logic explicit in the spawn apply system and lets Unity handle actual chunk allocation/structural creation.
* Structural changes still exist for overflow, but overflow is the exceptional path. The normal path is direct component overwrite on reused disabled entities.

Important doc rule:

```text
Reuse is the normal path. ECB creation is the overflow path.

The architecture should not over-optimize overflow creation with prototype entities unless profiling proves it matters. The first rewrite should prefer clarity: command data says what components are needed, and ECB creation builds the entity shape when no matching reusable slot is available.
```

One correction to keep in the doc: `ECB.AddComponent` creation is still structural work. The reason to prefer it is not that structural change disappears, but that it is isolated to overflow and handled in a clear, batched, Unity-native way.

Next high-impact question:

How should a `ProjectileSpawnCommand` describe which optional components are required?

A. **Component mask / archetype key on the command**
Example: `ProjectileComponentMask.HasTimedSpawn | HasImpactAoe | HasContactDamage`. Spawn apply uses the mask to choose reuse query and overflow `AddComponent` set.

B. **Separate command type per projectile archetype**
Example: `BasicProjectileSpawnCommand`, `TimedProjectileSpawnCommand`, `ImpactAoeProjectileSpawnCommand`. Very explicit, but can explode combinatorially.

C. **Nullable/valid snapshot fields inside one command**
Example: command has `TimedSpawnSnapshot` plus `HasTimedSpawn`. Similar to a mask, but tied directly to payload fields.

D. **Hybrid: mask + payload fields**
Command carries a compact mask for routing/reuse/creation, and only the corresponding payload fields are meaningful.

My current assumption is **D**. The mask is for archetype/reuse logic; the payload fields are for component initialization.

Working assumptions updated:

* `ProjectileSpawnEvent` and `ProjectileSpawnCommand` are structurally very similar.
* The main difference is that `ProjectileSpawnEvent` may include **spawn-multiplicity / volley fields**, such as:

```csharp
public int    Count;
public float  SpreadDegrees;
public float  JitterDegrees;
public uint   JitterSeed;
```

* `ProjectileSpawnCommand` is the post-expansion, single-projectile version.
* Expansion removes volley semantics by resolving one event into N concrete commands.

Conceptually:

```text
ProjectileSpawnEvent
  = one spawn occurrence
  = contains base projectile snapshot
  = contains volley/pattern fields

ProjectileSpawnExpansionSystem
  = resolves count/spread/jitter into concrete per-projectile values

ProjectileSpawnCommand
  = one projectile
  = contains resolved position/direction/rotation/etc.
  = contains same fixed-depth snapshot payload needed to initialize that projectile
```

So the phase boundary should be:

```text
Before expansion:
  "Spawn 12 projectiles in a 45 degree spread with jitter."

After expansion:
  "Spawn this projectile at this position with this direction and this copied snapshot data."
```

Important doc rule:

```text
Spawn apply does not understand volleys.

If a spawn apply system sees Count, SpreadDegrees, JitterDegrees, or JitterSeed, the event/command boundary has leaked.
```

Next high-impact question:

Should **all per-projectile math** be resolved during expansion?

A. **Yes** — expansion resolves final position, direction/rotation, speed variation, jitter, child snapshot orientation, etc. Spawn apply only copies fields into components.

B. **Mostly yes, but spawn apply may derive trivial component fields** — expansion resolves gameplay variation, spawn apply may compute simple component initialization like matrix/cache fields.

C. **No** — spawn apply may still compute some projectile-specific spawn variation as long as one command = one projectile.

My current assumption is **A**, because it gives the cleanest separation: expansion interprets spawn pattern; apply only materializes.

A

Working assumptions updated:

* **All per-projectile spawn math belongs in expansion.**
* `ProjectileSpawnExpansionSystem` resolves:

  * count,
  * spread,
  * jitter,
  * final direction,
  * final spawn position,
  * final rotation,
  * any per-projectile randomized variation.
* `ProjectileSpawnApplySystem` should not understand volley/pattern semantics.
* Spawn apply should only:

  * read one `ProjectileSpawnCommand`,
  * find/reuse a matching disabled slot,
  * copy fields into components,
  * enable `Active`,
  * or ECB-create overflow.

Doc rule:

```text
Expansion resolves gameplay multiplicity.
Spawn apply performs entity materialization only.

If spawn apply contains spread, jitter, count, fan/ring/volley, or randomization logic, the phase boundary has failed.
```

Current spawn model:

```text
ProjectileSpawnEvent
  - one gameplay spawn occurrence
  - may represent multiple projectiles
  - contains volley/pattern fields

ProjectileSpawnExpansionSystem
  - expands event into N commands
  - resolves all per-projectile math

ProjectileSpawnCommand
  - one concrete projectile
  - no volley semantics
  - allocation-ready

ProjectileSpawnApplySystem
  - reuse disabled matching slot
  - ECB-create overflow
```

Next high-impact question:

How should `ProjectileSpawnCommand` indicate which optional behavior components the projectile needs?

A. **Mask + payload fields**
Example: command has `ComponentMask`, plus payload fields like `TimedSpawnSnapshot`, `ImpactAoeSnapshot`, `ContactDamageSnapshot`. Mask determines which payloads are meaningful and which components are required.

B. **Nested snapshot includes validity flags**
Example: `TimedSpawnSnapshot.Enabled`, `ImpactAoeSnapshot.Enabled`. No separate mask; each payload says whether it should become a component.

C. **Separate command queues per projectile shape**
Example: one queue for basic projectiles, one for impact-spawning projectiles, one for timed-spawning projectiles. Clean reuse queries, but more queues/systems.

D. **Always create the same broad projectile archetype**
Every projectile slot has all optional behavior components, but unused ones are disabled/empty. Simplifies reuse, but weakens the “component presence means behavior presence” rule.

C

Working assumptions updated:

* `ProjectileSpawnCommand` should **not** carry a component mask.
* Instead, expansion should output to **separate command queues per projectile shape/archetype family**.

Example:

```text
ProjectileSpawnEvent
  -> ProjectileSpawnExpansionSystem
  -> BasicProjectileSpawnCommandQueue
  -> ImpactAoeProjectileSpawnCommandQueue
  -> TimedChildProjectileSpawnCommandQueue
  -> ContactDamageProjectileSpawnCommandQueue
  -> etc.
```

Each command queue has a clear entity shape:

```text
BasicProjectileSpawnCommand
  -> creates/reuses projectile slots with only core projectile components

ImpactAoeProjectileSpawnCommand
  -> creates/reuses projectile slots with ImpactAoeSpawn component

TimedChildProjectileSpawnCommand
  -> creates/reuses projectile slots with TimedProjectileSpawn component

TimedChildImpactProjectileSpawnCommand
  -> creates/reuses projectile slots with both TimedProjectileSpawn and ImpactAoeSpawn
```

This fits your rule:

```text
Component presence defines behavior.
Command queue defines required component shape.
Spawn apply does not branch on masks.
```

The tradeoff is that combinations can grow, but it keeps each system/query simple and explicit. That is aligned with the rewrite goal: clarity over clever generic routing.

Updated phase boundary:

```text
ProjectileSpawnEvent
  - one gameplay spawn occurrence
  - may contain count/spread/jitter

Expansion
  - resolves volley math
  - chooses the correct concrete command queue based on resulting projectile shape

Shape-specific spawn apply
  - reads one command queue
  - queries disabled Active entities with the matching component set
  - overwrites components
  - enables Active
  - ECB-creates overflow with the same shape
```

Doc rule:

```text
Projectile shape is selected during expansion, not during spawn apply.

Spawn apply systems consume already-classified command queues. They should not inspect flags or masks to decide what kind of projectile they are creating.
```

Next high-impact question:

How broad should a “projectile shape” be?

A. **Exact component set**
Each unique combination gets its own command queue and spawn apply path. Very clear, but can create many queues.

B. **Small number of named archetype families**
Example: `Basic`, `ImpactSpawn`, `TimedSpawn`, `TimedImpactSpawn`, `ContactDamage`. Some optional payloads may still be grouped if they are always used together.

C. **Start with exact component set, then merge only if duplication becomes painful**
Most correct for the rewrite; optimize later based on actual shapes.

C

Working assumptions updated:

* Projectile command queues should start as **exact component-set queues**.
* Each unique projectile shape gets its own concrete command queue and spawn apply path.
* Do not prematurely merge shapes for abstraction.
* If queue/system duplication becomes painful later, merge based on real repeated patterns, not guessed generality.

Doc rule:

```text
Start explicit. Generalize only after shapes repeat.

The rewrite should prefer exact component-set command queues because they make reuse queries, initialization logic, and overflow creation obvious. A projectile shape is not a runtime mask; it is a known command queue and apply path.
```

Current projectile shape model:

```text
ProjectileSpawnEvent
  -> expansion resolves volley math
  -> expansion chooses exact command queue

BasicProjectileCommandQueue
  -> BasicProjectileSpawnApplySystem

ImpactAoeProjectileCommandQueue
  -> ImpactAoeProjectileSpawnApplySystem

TimedProjectileCommandQueue
  -> TimedProjectileSpawnApplySystem

TimedImpactProjectileCommandQueue
  -> TimedImpactProjectileSpawnApplySystem
```

This gives the clearest reuse rule:

```text
Command queue == required component set == disabled-slot query shape == overflow creation shape
```

Next high-impact question:

How should **AoE spawning** fit into this model?

A. **Same event → expansion → exact-shape command queue → apply pipeline as projectile**
AoE has its own `AoeSpawnEvent`, `AoeSpawnCommand`, and exact-shape queues if needed.

B. **Simpler AoE path**
AoE spawn events are already concrete enough, so they can skip expansion unless AoE later gains volley/pattern behavior.

C. **AoE uses the same pipeline shape for consistency, even if expansion is currently trivial**
This adds some ceremony now, but keeps projectile and AoE spawning architecturally parallel.

C. i plan to implement similar feature for AOE that can have time based child spawns which can create many aoe depending on some kind of scatter math, this should go through the same pipeline. 

Working assumptions updated:

* **AoE should use the same spawn architecture as projectile**, even if some AoE cases are currently simple.
* AoE spawning should not be treated as a shortcut path.
* AoE will eventually support timed child spawns and scatter-style expansion, so it needs the same separation:

```text
AoeSpawnEvent
  = one gameplay-level AoE spawn occurrence
  = may describe scatter/count/pattern/timed child behavior

AoeSpawnExpansionSystem
  = resolves scatter/pattern math
  = outputs one-AoE commands into exact-shape command queues

AoeSpawnCommand
  = one concrete AoE entity
  = no scatter/count/pattern semantics

AoeSpawnApplySystem
  = reuses disabled matching AoE slots
  = ECB-creates overflow
```

* The architecture should be parallel across spawnable entity domains:

```text
Projectile:
  SpawnEvent -> Expansion -> exact-shape SpawnCommand queues -> Apply

AoE:
  SpawnEvent -> Expansion -> exact-shape SpawnCommand queues -> Apply
```

Doc rule:

```text
All spawnable runtime entity types follow the same phase model.

A spawn event is allowed to describe multiplicity. A spawn command is not.
Projectile and AoE spawning should differ in data and component shape, not in pipeline structure.
```

Next high-impact question:

How should **recursive spawn depth** be enforced?

A. **By fixed-depth snapshot types only**
Example: `ProjectileSnapshot` may contain `TimedChildProjectileSnapshot`, which may contain `ImpactAoeSnapshot`, but there is no generic recursive list/tree. The type layout makes deeper recursion impossible.

B. **By runtime depth field**
Each event/command carries `RemainingSpawnDepth`; systems drop or reject events once depth reaches zero. More flexible, but adds checks everywhere.

C. **Hybrid**
Use fixed-depth snapshot structs as the main guard, plus debug/runtime assertions that reject invalid authored chains deeper than the intended limit.

My current assumption is **C**: fixed-depth data for runtime simplicity, authoring/debug validation to catch invalid chains before they enter ECS.

the shape of the struct enforces it. hard code spawn command to have a nested struct but with hardcoded depth. the spawn command has a nested structure but every time it goes through a system, the top layer is stripped away. 

eg. 
projectile / aoe
spawn condition
  projectile / aoe
  spawn condition
    projectile / aoe.

becomes 
projectile / aoe
spawn condition
  projectile / aoe.

and then 
projectile / aoe.

Working assumptions updated:

* Recursive spawn depth is enforced by **type shape**, not a runtime depth counter.
* Spawn payloads are fixed-depth nested structs.
* There is no generic recursive tree/list of spawn definitions.
* Each time a spawn condition fires, the current top layer is consumed and the nested child payload becomes the new top-level spawn event/command payload.
* This naturally reduces remaining spawn depth:

```text
Initial snapshot:

Projectile or AoE
└─ SpawnCondition
   └─ Projectile or AoE
      └─ SpawnCondition
         └─ Projectile or AoE


After first spawn:

Projectile or AoE
└─ SpawnCondition
   └─ Projectile or AoE


After second spawn:

Projectile or AoE
```

Doc rule:

```text
Recursive spawn is not runtime-recursive.

The payload may contain a hardcoded nested spawn layer, but each spawn system strips one layer when emitting the next spawn event. Since the struct does not contain an unbounded child list or self-reference, deeper recursion is impossible by construction.
```

This is a strong design choice because it makes the limit obvious in code instead of relying on runtime checks.

Next high-impact question:

How should a **spawn condition component** specify what type of child it produces?

A. **Separate components by output type**
Example: `TimedProjectileSpawn`, `TimedAoeSpawn`, `ImpactProjectileSpawn`, `ImpactAoeSpawn`. Each component emits only one event type. Very explicit and matches the separate pipeline model.

B. **One generic spawn condition component with a child kind field**
Example: `TimedSpawn` contains `SpawnKind.Projectile/Aoe`. Fewer components, but reintroduces branching.

C. **Separate by trigger, but child payload can contain either projectile or AoE**
Example: `TimedSpawn` is one component, but contains optional child projectile/AoE payload. Cleaner trigger grouping, but less clean ECS filtering.

My current assumption is **A**: separate trigger-output components, because it keeps system queries and event outputs explicit.

A

Working assumptions updated:

* Spawn condition components should be **separate by both trigger and output type**.
* No generic `TimedSpawn` component with `SpawnKind`.
* No generic `ImpactSpawn` component that branches between projectile/AoE.
* Each component has one clear job: when its trigger condition is met, emit one known spawn event type.

Example shape:

```csharp
public struct TimedProjectileSpawn : IComponentData
{
    public float Interval;
    public float TimeUntilNext;
    public ProjectileSpawnSnapshotDepth1 ChildProjectile;
}

public struct TimedAoeSpawn : IComponentData
{
    public float Interval;
    public float TimeUntilNext;
    public AoeSpawnSnapshotDepth1 ChildAoe;
}

public struct ImpactProjectileSpawn : IComponentData
{
    public ProjectileSpawnSnapshotDepth1 ChildProjectile;
}

public struct ImpactAoeSpawn : IComponentData
{
    public AoeSpawnSnapshotDepth1 ChildAoe;
}
```

Doc rule:

```text
Trigger-output combinations are explicit components.

If a timed behavior emits projectiles, it is a TimedProjectileSpawn component.
If a timed behavior emits AoEs, it is a TimedAoeSpawn component.
Systems should not branch on child spawn kind.
```

Current recursive stripping rule:

```text
TimedProjectileSpawn on current entity
  -> emits ProjectileSpawnEvent using nested child snapshot
  -> child projectile is initialized with one fewer nested spawn layer
```

Next high-impact question:

How should **collision, contact gate, damage, and impact spawn** be separated?

A. **Collision emits raw contact events only**
Then contact gate filters contacts. Then consequence systems consume accepted contacts and emit damage replay events / impact spawn events. Cleanest separation.

B. **Collision emits accepted hit events directly**
Collision system includes contact gate logic, then emits damage and impact spawn events. Fewer systems, but collision owns more gameplay consequence logic.

C. **Collision emits damage/spawn events directly, contact gate only suppresses repeated damage**
Simpler in the current code style, but less clean because collision knows too much about consequences.

My current assumption is **A**: collision detects contacts; contact gate decides whether a contact is accepted; consequence systems produce damage replay and impact spawn events.

Working assumptions updated:

* **Collision qualifies contacts inline.**
* Contact gate checking stays inside collision because it answers:
  “Is this contact a real hit this tick?”
* `ProjectileContactGateSystem` remains separate, but its role is **gate state maintenance**:

  * tick cooldowns,
  * expire old contact entries,
  * clear invalid state.
* Collision does not own consequences like damage, impact spawn, VFX, etc.
* Once collision determines a contact is a valid hit, it emits plain hit/consequence data into streams.
* Consequence systems consume those streams later and independently produce:

  * atomic damage replay events,
  * impact projectile spawn events,
  * impact AoE spawn events,
  * possibly VFX/audio events later.

Doc rule:

```text
Collision owns hit qualification, not hit consequences.

Contact gate checks are part of deciding whether a collision is a valid hit. Damage, impact spawning, replay, and other effects are separate consequences and must be decoupled through streams.
```

Updated collision/consequence shape:

```text
ProjectileContactGateSystem
  - maintains gate state
  - cooldown tick
  - expiry cleanup

ProjectileCollisionSystem
  - detects overlap/contact
  - checks contact gate inline
  - emits qualified hit/consequence streams

DamageConsequenceSystem
  - consumes qualified hit data
  - emits atomic damage replay events

ImpactAoeSpawnConsequenceSystem
  - consumes qualified hit data
  - emits AoeSpawnEvent

ImpactProjectileSpawnConsequenceSystem
  - consumes qualified hit data
  - emits ProjectileSpawnEvent
```

Next high-impact question:

Should collision emit **one generic qualified hit stream**, or emit **separate consequence streams** directly?

A. **One qualified hit stream**
Collision emits `QualifiedProjectileHit`. Damage, impact spawn, VFX, etc. all read it and decide whether they have relevant components/data. Cleanest decoupling, but multiple systems may scan the same hit stream.

B. **Separate consequence streams from collision**
Collision checks which consequence components exist and writes `DamageHit`, `ImpactAoeHit`, `ImpactProjectileHit`, etc. Consequence systems do less filtering, but collision knows more about consequence categories.

C. **Hybrid**
Collision emits one minimal qualified hit stream for shared facts, plus optional specialized streams only for high-volume consequences where avoiding extra scans matters.

B. Separate.

Working assumptions updated:

* Collision emits **separate typed consequence streams**, not one generic qualified-hit stream.
* This avoids every consequence system scanning the same generic hit list.
* Collision still does not *apply* consequences. It only qualifies the hit and writes the relevant stream records.
* Example outputs:

```text
ProjectileCollisionSystem
  - checks overlap
  - checks contact gate inline
  - if valid hit:
      -> DamageHitStream
      -> ImpactAoeSpawnHitStream
      -> ImpactProjectileSpawnHitStream
      -> maybe VfxHitStream later
```

Doc rule:

```text
Collision may route valid hits into typed streams, but it must not execute the consequence.

Writing an ImpactAoe hit stream is allowed.
Spawning the AoE inside collision is not.
Applying damage inside collision is not.
```

Next high-impact question:

For these separate consequence streams, should collision emit **final consequence events** or **intermediate typed hit records**?

A. **Final consequence events directly**
Collision reads the consequence component and writes `DamageReplayEvent`, `AoeSpawnEvent`, `ProjectileSpawnEvent`, etc. Fewer systems, but collision performs more data transformation.

B. **Intermediate typed hit records**
Collision writes `DamageHit`, `ImpactAoeHit`, etc. Later consequence systems convert those into `DamageReplayEvent` or spawn events. Cleaner separation, more systems.

C. **Mixed rule**
Collision may write final events for simple consequences like damage, but uses intermediate records for consequences needing heavier transformation.

Working assumptions updated:

* Collision emits **final typed consequence events directly**.
* There is no intermediate `DamageHit` / `ImpactAoeHit` conversion phase unless later complexity proves it necessary.
* Collision is allowed to read consequence components/snapshots and write final downstream events, but it still must not execute the consequence.

Example:

```text
ProjectileCollisionSystem
  - detects collision
  - checks contact gate inline
  - reads ContactDamage snapshot if present
      -> writes DamageReplayEvent
  - reads ImpactAoeSpawn snapshot if present
      -> writes AoeSpawnEvent
  - reads ImpactProjectileSpawn snapshot if present
      -> writes ProjectileSpawnEvent
```

Important boundary:

```text
Collision may emit final consequence events.
Collision may not apply damage.
Collision may not directly spawn entities.
Collision may not call managed callbacks.
```

Doc rule:

```text
Collision is the final producer of consequence events, not the executor of consequences.

A valid hit can immediately become a DamageReplayEvent or AoeSpawnEvent because those are still plain-data events. The actual side effects remain outside collision: damage is dispatched later, and spawn events still go through expansion and spawn apply.
```

Next high-impact question:

How should **AoE damage/hit timing** work?

A. **Same as projectile hit semantics**
AoE collision emits atomic `DamageReplayEvent`s when contact qualifies. Contact gating/cooldown determines repeat hits.

B. **AoE has explicit tick damage semantics**
AoE collision runs on a damage interval, for example once every N seconds/ticks per target. This is separate from projectile contact gate.

C. **AoE supports both entry hit and periodic tick hit**
Example: damage once on entry, then again every interval while target remains inside.

D. **Undecided / defer**
Document AoE collision as structurally parallel to projectile collision for now, but leave exact damage timing as an open design point.

use existing but also emit final consequence events. 

Working assumptions updated:

* Projectile consume/deactivation behavior should mostly **stay as existing logic**.
* Collision may disable `Active` immediately when the accepted hit means the projectile is consumed.
* This is allowed because collision already owns hit qualification and source-contact state.
* The rewrite should not force projectile deactivation through another event unless there is a specific reason later.
* Existing behavior rules such as destroy-on-hit, pierce, hit count, contact gate, or expiry-on-impact should remain local to collision/source-state logic.

Doc rule:

```text
Collision owns the source projectile state transition caused by a qualified hit.

If the projectile is consumed by the hit, collision may disable Active immediately. This is not treated as a consequence event because it is the state transition of the source entity itself, not an external consequence like damage, impact spawning, or managed replay.
```

Updated boundary:

```text
Collision may:
  - qualify hit
  - update projectile hit/pierce/contact state
  - disable source projectile if consumed
  - emit final consequence events

Collision may not:
  - apply damage callback
  - directly spawn child entities
  - mutate GameObject scene objects
```

Next high-impact question:

Should the rewritten ECS combat simulation run on a **fixed tick** or variable frame delta?

A. **Fixed tick only**
Lifetime, timed spawns, gates, collision, damage replay production all advance by a fixed simulation step. Best for consistent gameplay semantics.

B. **Variable delta**
Use frame `deltaTime`. Simpler with Unity frame loop, but timed gates/spawns can vary with frame rate.

C. **Hybrid**
Core combat simulation uses fixed tick, while rendering/VFX interpolation remains frame-based.

My current assumption is **C**, with combat logic fixed-tick and visuals frame-based.

B

Working assumptions updated:

* Combat simulation uses **variable `deltaTime`**.
* Timed spawn behavior should **catch up fully**, matching existing behavior.
* If a large `deltaTime` crosses multiple spawn intervals, the timed spawn system emits multiple spawn events in that same simulation update.
* Those emitted spawn events still go through the normal pipeline:

```text
Timed spawn component
  -> emits SpawnEvent(s)
  -> expansion resolves multiplicity / scatter / volley
  -> command queues
  -> spawn apply late in update
  -> spawned entities participate next simulation update
```

Doc rule:

```text
Timed spawn systems preserve elapsed-time semantics.

A slow frame should not permanently reduce spawn count. If enough time has accumulated for multiple intervals, the system emits all due spawn events. Burst cost is accepted as the current behavior unless profiling later proves a cap is required.
```

One important implication:

```text
Catch-up applies to event production.
Immediate recursive simulation still does not happen.

Even if a timed spawn emits multiple events in one update, the spawned entities do not move, collide, or emit their own timed spawns until the next simulation update.
```

Next high-impact question:

How should **damage replay events** be stored before managed dispatch?

A. **NativeQueue → NativeArray**, same as spawn pipeline
Collision writes atomic `DamageReplayEvent`s in parallel. After simulation, the bridge converts/finalizes to an array and dispatches to GameObjects.

B. **DynamicBuffer singleton**
More ECS-visible and easier to inspect, but less ideal for high-volume parallel writes.

C. **Managed list only at bridge time**
ECS writes minimal native data, then bridge copies to managed structures before dispatch. Simple for callbacks, but the boundary is less clean.

My current assumption is **A** for consistency with spawn/consequence streams.

A

Working assumptions updated:

* Target proxy entities may have a **managed companion component** linking back to the GameObject-side target object.
* This is acceptable because the managed reference is used only at the **managed bridge boundary**, not inside Burst/job collision logic.
* ECS hot-path systems still reference targets by unmanaged `Entity`.
* Collision/damage production emits:

```csharp
public struct DamageReplayEvent
{
    public Entity TargetProxy;
    public DamageSnapshot Damage;
    public float2 HitPosition;
    public float2 HitDirection;
}
```

* Damage dispatch then resolves:

```text
DamageReplayEvent.TargetProxy
  -> managed companion component on proxy entity
  -> GameObject target callback
```

Important doc rule:

```text
Managed target references are allowed only at the replay boundary.

Collision, spawn, tracking, and damage-event production must not dereference managed target objects. The proxy entity may carry a managed companion reference so the main-thread damage dispatcher can call back into the GameObject after ECS simulation is complete.
```

This keeps the split clean:

```text
Hot path:
  Entity + unmanaged components + native event streams

Bridge path:
  Entity -> managed companion -> GameObject callback
```

Next high-impact question:

How strict should the doc be about **managed companion access**?

A. **Damage dispatch only**
Only the damage replay bridge may read managed target companions. No other ECS system should touch them.

B. **Main-thread bridge systems only**
Damage, VFX, audio, UI replay bridges may read managed companions, but no Burst/job simulation system may access them.

C. **Any main-thread ECS system may access them**
Allowed as long as it is not inside Burst jobs. More flexible, but easier for managed access to leak into simulation.

A damage dispatch only. access should be restricted to specific systems. 

Working assumptions updated:

* Target proxy entities may carry a **managed companion reference**, but access is restricted.
* Only the **damage dispatch system/bridge** may read that managed companion.
* Burst/job simulation systems must only use unmanaged ECS data and `Entity` references.
* Other systems should not casually use the managed companion just because it exists.

Doc rule:

```text
Managed companion access is a controlled boundary.

The target proxy may contain a managed reference so damage dispatch can call back into the GameObject target. No simulation, collision, spawn, tracking, or consequence-production system may access that reference. The only approved reader is the damage dispatch bridge.
```

Updated boundary:

```text
Allowed:
  DamageReplayEvent.TargetProxy
    -> DamageDispatchBridge
    -> managed companion
    -> GameObject damage callback

Not allowed:
  Collision system -> managed target
  Tracking system -> managed target
  Spawn system -> managed target
  VFX/render preparation -> managed target
```

Next high-impact question:

How should the **native event streams/queues be owned across systems**?

A. **One central combat pipeline state owner**
A dedicated owner holds `DamageReplayQueue`, `ProjectileSpawnEventQueue`, command queues, etc. Producer/consumer systems access these known shared containers. Best for architecture visibility.

B. **Each producer system owns its own output queue**
Consumers gather from multiple producer-owned queues. Strong local ownership, but more plumbing and harder global tracing.

C. **Each event domain owns its own queue owner**
Example: `ProjectileSpawnPipelineState`, `AoeSpawnPipelineState`, `DamageReplayPipelineState`. Good balance: not one giant god object, but still clear ownership.

D. **Current implementation style, but documented with stricter phase rules**
Avoid changing ownership model unless it is already causing problems.

D. Current implementation style, but documented with stricter phase rules

Working assumptions updated from the uploaded system listing:

* The rewrite hotspot is mainly the **spawn/event/damage cluster**, not movement/collision math.
* Likely high-impact files:

  * `ProjectileSpawnSystem.cs`
  * `AoeSpawnSystem.cs`
  * `ProjectileChildSpawnSystem.cs`
  * `ProjectileMultiExpandSystem.cs`
  * `ProjectileSpawnCommand.cs`
  * `CombatSpawnConvertJob.cs`
  * `CombatHitDispatchSystem.cs`
  * `CombatHitElement.cs`
  * `CombatHitFlushJob.cs`
* Existing “mostly keep” systems are clearly present too:

  * `ProjectileMovementSystem.cs`
  * `ProjectileTrackingSystem.cs`
  * `ProjectileCollisionSystem.cs`
  * `AoeCollisionSystem.cs`
  * `ProjectileContactGateSystem.cs`
  * `AoeContactGateSystem.cs`
* The split lifetime issue is visible as `ProjectileLifetimeSystem.cs` and `AoeLifetimeSystem.cs`, which supports combining them into a generic lifetime system over `Lifetime + Active`.
* Target bridge files already exist: `CombatTargetRegistry.cs`, `CombatTargetSync.cs`, `CombatTargetSyncSystem.cs`, `CombatTargetSet.cs`, and `ICombatTarget.cs`. That suggests the rewrite should refine the boundary rather than invent it from scratch. 

The current likely document structure is now:

```text
1. Goals and non-goals
2. Current pain points by system cluster
3. Core architectural rules
   - snapshot everything
   - event vs command
   - typed streams
   - next-update spawn visibility
   - no managed access in simulation
4. Proposed ECS phase order
5. Spawn pipeline rewrite
6. Collision consequence event rewrite
7. Damage replay bridge rules
8. Target proxy lifecycle
9. Lifetime/Active unification
10. Concrete migration plan based on current files
11. Risks / validation checklist
```

Next high-impact question:

Which current file should be treated as the **main source of truth for the existing spawn mess**?

A. `ProjectileSpawnSystem.cs`
B. `AoeSpawnSystem.cs`
C. `ProjectileChildSpawnSystem.cs`
D. `CombatSpawnConvertJob.cs`
E. `ProjectileSpawnCommand.cs`

Best next input would be the contents of that one file, starting with whichever currently contains the most tangled event/command/reuse/ECB logic.

