# Coding Agents and a Bullet-Hell ECS Game: Two Stories From the Same Repo

This project is a 2D top-down action game built to survive a very specific kind
of stress test: hundreds of thousands of projectiles, area effects, and
chained attacks on screen at once, all still rendering at a playable frame
rate. Getting there involved two separate but related engineering problems —
how to get AI coding agents to actually help on performance-critical code
instead of quietly "fixing" it into something slower, and how to structure
the simulation itself so it can scale that far in the first place.

A quick primer, since a few ideas recur throughout:

- **Frame budget.** A game re-renders the whole screen many times a second —
  60 times a second (60 FPS) is a common target. That gives you roughly
  16.6 milliseconds to do *everything* for one frame: simulate the world,
  update AI, resolve collisions, render — before the next frame is due. Miss
  it and the game visibly stutters. There's no queue to buffer into and no
  retry; it's a hard real-time budget, every frame, forever.
- **Cache locality.** Modern CPUs are far faster than main memory, so they
  keep small, fast caches of recently-used data close to the core. Reading
  data that's contiguous in memory (an array) is dramatically cheaper than
  chasing pointers scattered across the heap (a linked structure of
  objects), because the CPU can pull a whole contiguous block into cache at
  once instead of paying a cache-miss penalty for every scattered lookup.
- **Out-of-order execution (OOO).** A single CPU core doesn't execute
  instructions strictly one at a time in program order — it looks ahead and
  reorders independent work to keep its execution units busy while
  something slow (like a memory fetch) is in flight. This gives you a
  surprising amount of "parallelism" on a single thread for free, *as long
  as the data is laid out so the CPU can predict what it needs next* —
  a cache miss stalls the pipeline no amount of reordering can hide.
- **SIMD / vectorization.** A CPU core can also apply one instruction to
  several values at once (single-instruction-multiple-data) instead of one
  value per instruction — a large win when a compiler can prove a loop does
  the same simple math across many independent values in a row. It's a real
  benefit of contiguous, uniform data layouts, but it isn't automatic: the
  compiler has to be able to auto-vectorize the loop in the first place,
  which tends to hold reliably for tight, branch-free engine/math code and
  much less reliably for arbitrary game logic full of per-entity branching
  and varying behavior. Don't assume every data-oriented loop in a project
  like this is actually running as SIMD instructions — some of it is, some
  of it isn't, and it depends on the specific code.
- **Why this all favors data-oriented layouts.** Cache locality and OOO are
  the two effects that reliably apply to *any* tight loop over contiguous
  data, vectorized or not, and they're the main reason "data-oriented"
  layouts — flat arrays of the same type — tend to beat "object-oriented"
  layouts (scattered heap objects, virtual calls) for this kind of workload,
  often before a single extra thread or a single vectorized instruction is
  involved. SIMD is a further win on top when it actually kicks in, not the
  base case to assume.
- **Draw calls.** Telling the GPU to render something is itself a costly
  operation, similar in spirit to a network round trip — the overhead is per
  *call*, largely independent of how much geometry is in it. So rendering
  10,000 sprites in one call is far cheaper than 10,000 separate calls, even
  though the GPU does the same amount of actual pixel work either way.

With that vocabulary in hand, here are the two stories.

## A. Coding agents on a performance-critical codebase

### A doc-first, skill-based workflow

Every agent working in this repo starts from the same entry point:
`Docs/project-overview.md`. It's a plain index — links to architecture docs,
data-flow docs, and coding standards — and the rule is that those documents
are authoritative over whatever comments happen to be sitting in the code.
Code comments drift; a maintained doc set, read first on every session,
doesn't.

On top of that, the repo defines its own reusable prompt templates —
"skills" — instead of relying on ad-hoc, differently-worded prompting every
time. Think of a skill as a saved runbook: a fixed set of instructions for a
recurring kind of task, so the agent's behavior for "write a plan" or "run an
implementation step" doesn't depend on how well that request happened to be
phrased that day. This project's skills include:

- **`pre-planning.md`** — an explore-only pass. It reads the docs and code
  and produces `info.md`, a set of links to the relevant places, not a
  summary and not a task list. The idea is to separate "understand the
  terrain" from "decide what to do about it."
- **`plan-changes.md`** — turns a discussion into a grounded implementation
  plan (an `index.md` plus numbered subtask files), and forces an explicit
  comparison between "add something new" and "refactor the existing thing"
  before either is chosen.
- **`implement-tasks.md` / `implement-single-task.md`** — execute the plan,
  one subtask at a time.
- **`focus-on-structural-change.md`** — a mode swap for refactors that
  explicitly favors getting the structure (ownership, phase boundaries)
  right over getting the immediate logic right, and pushes back on
  "misleading safety" like null checks and fallbacks that paper over unclear
  ownership instead of fixing it.
- **`vfx-graph.md`** — for working with Unity's **VFX Graph** tool. VFX
  Graph is a node-based editor for authoring particle effects (sparks,
  explosions, trails): instead of writing code, you wire together nodes for
  "spawn rate," "initial velocity," "color over lifetime," and so on, and
  Unity compiles that graph into GPU-driven particle simulation. The catch
  for an agent is that the result is saved as a serialized graph asset — a
  large, Unity-internal representation of node positions and connections —
  which is not meaningful to read as raw text, and a screenshot only shows
  the visual layout, not the actual parameter values. So this skill goes
  through a custom CLI bridge (`AgentVFX`) that queries the live graph
  data directly, read-only, and plans new effects against an 8-axis
  visual-fidelity checklist instead of guessing from either the serialized
  text or a picture of the node graph.
- **`create-asset.md`** — a shared template for generating pixel-art asset
  prompts (particles, characters, props) consistent with the project's style
  reference. The reason this needs a template at all is the same reason
  under-specified coding prompts go wrong: without enough grounding, the
  space of "plausible pixel art" (or plausible code) an agent could produce
  is enormous, and it'll confidently fill that space with something that
  looks right in isolation but doesn't match anything real — the same
  failure mode as an LLM hallucinating a fact it was never actually given.
  A style reference narrows that space down to "consistent with this
  project's existing art" instead of "generically plausible pixel art," the
  same way pointing an agent at this project's actual coding standards and
  existing ECS code (rather than leaving it to guess) narrows its output
  down to "consistent with this codebase" instead of "generically plausible
  C#." Both are the same fix for the same underlying problem: an
  under-constrained prompt doesn't produce an "average" or "safe" answer,
  it produces something ungrounded that happens to look plausible.
- **`caveman.md`** — an intentionally terse response mode, used purely to
  cut output tokens.

The harness also carries persistent, file-based agent memory of its own —
user preferences, project state, prior corrections — across sessions. That's
a capability of the coding-agent tooling itself, not something this repo
defines or configures; it's included here only as context for why the
overall workflow holds together session to session.

### Fighting the agent's default bias

General-purpose coding agents have a consistent bias toward maintainability
and "safe" changes, and it comes from at least two compounding sources.
One is training data: most code an agent has learned from is ordinary
application code, where those patterns are simply the right answer.
Another is the harness itself: the tooling and system-level instructions
layered around the model also default toward caution — favoring smaller,
more reversible, easier-to-review changes over ones that touch more surface
area. How much each source contributes isn't something this project has a
way to separate out — only that the resulting behavior is real and has to
be corrected for either way. Left to its own judgment under both pulls, an agent
reaches for clean interfaces over specialized fast paths, LINQ (C#'s query
syntax over collections) over tight loops, small layered helper methods over
flattened hot-path code, dictionaries over arrays, defensive copies over
mutating a preallocated buffer, and safe locking over partitioned lock-free
work.

This isn't a theoretical concern. There have been multiple cases where the
prompt alone — however carefully worded, in the moment — wasn't enough to
shift the agent off this bias, and the actual fix was tearing out a section
of the codebase and reimplementing it, or just discarding the agent's work
outright and starting over. That's part of why the fix described below is
structural rather than a matter of better-worded instructions: something
written once into standards, skills, and the codebase itself has to hold up
across sessions in a way that a single good prompt doesn't.

In an ordinary backend service, most of that bias is *correct* — you're
usually optimizing for readability and correctness over microseconds. In
this codebase it's actively wrong, because the core design target is extreme
attack scaling: tens or hundreds of thousands of projectiles and effects
per frame, all inside that same ~16.6ms budget. For scale, a typical game
doesn't even reach 5,000 active logical objects on screen at once — and the
performance work described in this post buys enough headroom that a
"normal" game built with the same engine could comfortably run at 144 FPS
(about 6.9ms per frame) in a busy scene. The 16.6ms budget above isn't the
hard ceiling this project is actually pushing against; it's just a common
reference point. A few concrete translations of why each "safe" default is
expensive here:

One thing sharpens all of this further: this project's combat code runs
inside **Burst-compiled** ECS jobs, which compile down to native machine
code — but only for unmanaged, blittable data, never for ordinary managed
C# references (the fuller picture of what that split actually means is in
Part B, under the architecture section). That split isn't a spectrum, it's
closer to a cliff edge. A managed type or feature — `Dictionary<>`, LINQ, a
closure capturing a reference, a C# `lock` — isn't merely *slower* inside a
Burst job, it's usually not something Burst can compile *at all*. The
moment code meant to be Burst-compiled touches one, it either fails to
compile under Burst or the surrounding code has to fall back out of Burst
entirely and run as regular managed C# — giving up the whole native-code
benefit, not just paying one extra allocation. With that as the backdrop:

- **LINQ and closures.** Beyond being Burst-incompatible outright, every
  `.Where()` or captured lambda in ordinary managed code can also allocate —
  garbage that runtime's own garbage collector has to clean up later, and a GC pause
  inside a real-time frame budget is a visible stutter. A plain loop over an
  array is both Burst-compilable and allocation-free.
- **Dictionaries over arrays/bitsets.** Managed `Dictionary<>` isn't usable
  inside a Burst job at all — Burst's native container library has its own
  fixed-capacity hash-map type instead, and it's a heavier tool than
  reaching for an array indexed by a small integer ID, which is both a
  single predictable memory access and trivially Burst-friendly.
- **Defensive copies over mutating preallocated buffers.** Copying "to be
  safe" means allocating again, every frame, for data that's going to be
  thrown away in a few milliseconds anyway — true whether or not Burst is
  involved, since even native allocations inside a job aren't free.
- **Safe locking over partitioned lock-free work.** The managed `lock`
  statement isn't available inside Burst jobs either; the native
  alternative still needs care, and a lock that many threads contend for
  turns parallel work back into serial work with extra overhead on top —
  worse than not parallelizing at all in the hot path. (One concrete example
  the coding standards call out: `NativeList.ParallelWriter`, which looks
  like the "safe" choice, is actually the more contended write path
  compared to the alternative used here, `NativeQueue`.)

Rather than leaving this as something a human has to remember to correct
every time, the fix was written into the codebase itself:

- `coding-standards.md` makes the rules explicit and checkable: an
  Allocation Rule that treats per-frame LINQ, closures, and broad lookups as
  bugs unless measured harmless; a Performance Budget Rule; System
  Encapsulation rules that keep systems talking through defined singleton
  components instead of reaching across into each other's internals.
- The `plan-changes` skill forces the additive-vs-refactor comparison and a
  default rule — "if two representations describe the same domain concept,
  refactor toward one source of truth" — before any plan is accepted. That
  closes off the agent's instinct to just bolt a second, safer path
  alongside the fast one.
- `focus-on-structural-change` is an explicit mode swap for refactors:
  prioritize getting ownership and phase boundaries right over immediate
  logic correctness, and reject "misleading safety" — null checks and
  fallbacks that hide unclear ownership rather than resolve it.

None of this makes the bias go away — it's still there today, and still has
to be caught in review. What it does do is give the agent something to
anchor on besides its own training-driven priors. The bias was strongest
early on, when the project was small and there wasn't much existing
performance-critical code for the agent to pattern-match against; every
suggestion was drawn almost entirely from the "safe, general-purpose C#"
prior. As the codebase itself grew into a larger body of Burst-compiled,
data-oriented ECS code, that became something an agent reading the
surrounding files could imitate directly, on top of the explicit standards —
so the bias didn't disappear, but it now has real local precedent pulling
against it instead of nothing.

### How this compares to GitHub's Spec Kit

Spec Kit is GitHub's own agentic-planning pipeline, and it turns out to
share the same basic shape as this repo's skill set almost stage for stage:
`specify.md` → `plan.md` → `tasks.md` → `implement.md` mirrors
`pre-planning.md` → `plan-changes.md` → `implement-tasks.md` →
`implement-single-task.md`. Both are: explore/spec, then plan, then break
into tasks, then execute — with each stage's output written to disk and read
back by the next, rather than living only in a chat transcript.

**Where they match:**

- **Same task contract.** Spec Kit's `tasks.md` is one task per checklist
  line, with a `[P]` marker for parallelizable work and an explicit file
  path. This repo's numbered subtask files encode the same idea — one
  discrete, reviewable change with an explicit dependency — just with a
  different notation.
- **Same "orchestrator doesn't redesign" boundary.** Spec Kit's
  `/implement` is explicitly told to execute `tasks.md` without reopening
  design decisions. `implement-tasks.md`/`implement-single-task.md`
  forbid new architecture decisions or reopening the plan mid-execution the
  same way.
- **Same principles gate, in spirit.** Spec Kit has a "Constitution Check"
  run before and after design, which errors on unjustified complexity.
  `plan-changes.md`'s structural-warning check — flag a second data type, a
  second code path, or an adapter shim and require justification — plays
  the identical role: stop unreasoned complexity before it's built.

**Where they diverge:**

Underlying most of these differences is a difference in what each pipeline
is built for. Spec Kit is a generic tool, meant to work reasonably across
arbitrary tech stacks and projects it knows nothing about in advance — which
is exactly why it invests in things like auto-detected ignore files, a
non-technical spec stage, and a general-purpose extension/hook system: it
can't assume anything about the project it'll be dropped into. This repo's
skills were written for one specific codebase, aimed at problems that
codebase has actually run into implementing changes — Burst/ECS's
compilation constraints, VFX Graph's opaque serialized format, this
project's own test-evidence policy — so they're narrower and more
opinionated by design, not because they're an earlier or less complete
version of the same idea.

- **Test execution is inverted.** Spec Kit's `/implement` runs tests itself
  and checks TDD ordering and coverage as part of the command. This repo's
  rule runs the other way: agents never run the test suite themselves — the
  user runs Unity's test runner and hands back a machine-generated XML
  result file as the only accepted evidence.
- **Spec Kit provisions real repo state.** Numbered feature directories
  under `specs/`, git branch creation via hooks, auto-generated ignore files
  based on detected stack. None of that exists here; this repo's task
  folders are a flat `./.agent/<task_name>/`, picked by hand, with no
  scaffolding side effects.
- **Spec Kit's spec stage is technology-agnostic**, written for
  non-technical stakeholders — it bans implementation details and uses
  Gherkin-style scenarios with P1/P2/P3 priority. This repo has no
  equivalent: `pre-planning.md`'s `info.md` output is explicitly
  code-and-doc-linked exploration for the next *agent*, never meant for a
  non-technical reader.
- **Spec Kit generates more artifacts per feature** — `spec.md`,
  `research.md`, `data-model.md`, `contracts/`, `quickstart.md`, `tasks.md`,
  `checklists/` — versus this repo's `info.md` + `index.md` + numbered
  subtask files.
- **Spec Kit has a real extension system**: `.specify/extensions.yml` with
  before/after hooks on every command, plus per-OS bootstrap scripts that
  actually execute. This repo's skills are pure prompt instructions with no
  hook or plugin layer.
- **Context passing is coarser in Spec Kit.** `/implement` is told to read
  `tasks.md` + `plan.md` + `data-model.md` + `contracts/` + `research.md` +
  `constitution.md` + `quickstart.md` in full for every task. This repo's
  `implement-tasks.md` instead builds a compacted
  `implementation-context.md` plus a per-task execution packet, specifically
  to avoid re-sending the entire plan to every task's executor.

**Rough size comparison:** Spec Kit's four core command instructions total
about 50.7K characters; this repo's four equivalent skill files total about
30.0K characters — roughly 69% lighter for the same stage, before Spec Kit's
optional `clarify`/`analyze`/`checklist` commands (11.7K–22.3K characters
each) are even considered. Extrapolating to one medium feature (about three
subtasks) and adding each side's read-back overhead — Spec Kit's constitution
read, filled spec/plan/tasks docs, and a final re-read of `tasks.md`; this
repo's per-subtask reload of `implement-single-task.md` and its compact
generated docs — the total context load lands at roughly **~108K characters
(~27K tokens) for Spec Kit versus ~62K characters (~16K tokens) here, about
1.7x, or ~11K more tokens**. Character count is only a rough proxy for token
count, and this is an order-of-magnitude estimate built on stated
assumptions rather than a benchmark — actual cost depends on feature size,
how many optional Spec Kit commands run, and how compact either pipeline's
generated docs stay in practice.

## B. The Prototype game

### The architecture split

The game splits its runtime into two different execution models, chosen per
workload rather than applied uniformly.

Low-count, authored gameplay — the player, mobs, walls, the camera, debug
UI — runs as ordinary Unity **GameObjects** with real **Physics2D**.
GameObjects are Unity's default object model: each one is a container of
components (scripts, colliders, renderers) with its own place in a scene
hierarchy, updated through virtual method calls. It's flexible and
well-tooled, but each instance carries real per-object overhead.

High-count combat simulation — projectiles, area effects, targeted and
chained attacks — runs on **Unity Entities/DOTS** instead: an
**ECS** (Entity Component System) architecture. Where a GameObject bundles
data and behavior into one heap object per instance, ECS keeps data in flat,
contiguous arrays (components) indexed by lightweight entity IDs, processed
by systems that iterate straight over those arrays. This is the SoA
(struct-of-arrays) layout from the primer, applied at the component level —
each component type gets its own contiguous array — and it's compiled
through Unity's Burst compiler down to tight native code instead of managed
C#, with cache-friendly, auto-vectorization-eligible loops as the target,
not a guarantee that every loop actually ends up running as SIMD
instructions (see the primer's SIMD note).

**Burst compiles a restricted subset of C#** straight to native machine
code, on par with C/C++ — but only for unmanaged, blittable data (structs
and native arrays, no references onto a garbage-collected heap). Ordinary
managed C# — the kind every general-purpose coding agent defaults to —
instead runs through one of Unity's own C# runtimes, not Microsoft's .NET
runtime. Inside the editor that's Mono, an independent implementation of the
C#/.NET language and standard library. In a shipped build it's usually
IL2CPP instead: a build step that translates that same managed C# into C++,
which is then compiled to a native executable alongside the Burst-compiled
code — so it ends up as native machine code too, just by a completely
different path than Burst, and still paying managed-runtime costs like
garbage collection along the way. Either way, "it's .NET" is an easy but
wrong assumption to carry in from backend work — Unity's runtimes only
implement the same language and standard library, not the same underlying
execution engine.

It isn't a rule that every individual field needs its own array, though.
Two fields that are always read and written together — say, a position and
a rotation that make up one transform — are often kept together in the same
component/struct on purpose, so they land in the same cache line instead of
forcing two separate array reads for data that's always used as a pair:
array-of-structs *within* that one component, nested inside struct-of-arrays
at the component level. The actual principle isn't "always split every
field apart," it's laying data out to match how it's actually accessed, to
get the most out of every cache line fetched.

The reason for the split, rather than picking one model for everything: one
GameObject per projectile collapses under GameObject and Physics2D overhead
once you're spawning tens of thousands of them — each one is a real object
with real per-instance cost, and Physics2D trigger colliders don't scale to
that count either. The opposite extreme, full-ECS actors, was rejected too:
it would trade away too much of Unity's mature GameObject-based authoring
tooling for something that's still comparatively low in count and doesn't
need ECS's throughput.

A single managed bridge crosses the boundary between the two worlds: game
logic on the GameObject side submits spawn intent and registers
render/VFX resources, while a lightweight proxy stands in for a GameObject
on the ECS side — so combat hit-testing runs against cheap, minimal proxy
data instead of against thousands of live Physics2D trigger colliders.

Keeping actors as GameObjects also means player and mob movement, animation,
and navigation can lean on Unity's own mature GameObject tooling —
`Animator`, `NavMeshAgent`, Physics2D — instead of reimplementing equivalents
in ECS, where that tooling is comparatively immature. Not all of it is used
yet (mob movement today is a simple wander/target-acquire loop, not navmesh
pathfinding), but the option stays open specifically because actors live as
GameObjects. The split also keeps the ordinary in-editor authoring workflow
intact: attacks and mobs stay prefabs, skill data stays
ScriptableObject-authored (Unity's serialized data-asset format), and visual
effects stay VFX Graph assets — an artist keeps working in the editor as
normal while the simulation underneath scales out separately.

### Inside the ECS simulation

High-count combat entities — and their VFX/rendering data — all live on the
ECS side:

- **Projectiles** split into two lanes with different hit-testing needs:
  a *discrete* lane (footprint-only hit test, can home toward a target) and
  a *continuous* lane (swept travel corridor, never homes). They run as
  separate systems rather than one generalized path because the two hit
  tests are different enough to not share cleanly.
- **Area effects** come in two flavors — a single pulse, and a lingering
  area that re-ticks on an interval — both gated so a target isn't hit again
  on every single frame it happens to overlap the area.
- **Chained/targeted attacks** resolve as a walk across a sequence of
  targets with falloff per link, existing only for the duration of that
  walk rather than as a persisted entity sitting around afterward.
- **Skill numbers are precompiled** into a flat runtime snapshot ahead of
  time, rather than re-evaluating a graph of modifiers every single frame —
  conceptually the same move as memoizing a computed config instead of
  recomputing it on every request. One shared base/added/increased/
  multiplier fold is used for every stat, so there's one formula to reason
  about instead of many.
- **Spawning** follows a request → allocate → apply pipeline that reuses
  entities from a disabled pool before ever cold-creating a new one — the
  same idea as connection pooling in a backend service: reusing an existing,
  already-allocated resource is much cheaper than tearing down and
  reconstructing one.
- **Visual effects** are dispatched as one-shot data requests into a shared
  VFX graph. Because one graph can be reused by different effects firing at
  different times, each particle has to copy the values it needs at spawn
  time rather than read from a shared buffer — otherwise a later request
  could overwrite the data before an earlier one reads it, a straightforward
  aliasing/race hazard.
- **Rendering** draws all projectile and AOE sprites in a single batched
  pass through a shared texture atlas (many small sprite images packed into
  one big texture), instead of issuing one draw call per effect instance —
  directly avoiding the per-call GPU overhead described in the primer.

### The authored side

Player, mobs, walls, camera, and debug UI stay as authored GameObjects —
low enough in count that Unity's Physics2D and inspector-driven workflow are
still simpler than folding them into the ECS simulation. Mob behavior today
is a simple loop rather than a full state machine: wander until a target is
acquired, then attack using the same underlying attack system the player
uses, with straightforward cleanup on death.

### Optimization history: a bottleneck-by-bottleneck account

This is the part most directly relevant to anyone who's chased performance
in any system, game or otherwise. Six areas were optimized over the course
of the project, and the record — commits, an abandoned branch, a reverted
architecture, explicit profiling numbers — is worth walking through
honestly, including the parts that *didn't* work.

**Collision.** This was ECS/Burst from the very first commit — there was
never a Physics2D-trigger phase to move away from. The real progression: a
naive per-projectile scan against every target (checking every pair, cost
growing quadratically with count) on day one, replaced the next day by a
coarse **spatial hash** — dividing the world into a grid of cells and only
checking pairs of objects that share a cell, the same idea as sharding or
indexing to avoid a full scan. That was refined a week later, then unified
into a single shared per-frame spatial-hash "singleton" that every
collision/tracking system reads, instead of each system building its own
copy — the same win as sharing one cache instead of every caller computing
its own. Later, a **SIMD BVH** broad-phase was built to try to replace the
spatial hash — a BVH (bounding volume hierarchy) is a tree of nested
bounding boxes that lets you quickly discard whole regions of space during a
query, and building/traversing it with SIMD applies the same instruction to
multiple box comparisons at once. It was benchmarked directly against the
existing spatial hash at ~217k active projectiles and ~16ms frame time, and
found no real improvement — abandoned on an unmerged branch. The spatial
hash is still what ships. The lesson here isn't "spatial hashing beats
BVHs" in general — it's that a more sophisticated-sounding algorithm isn't
automatically a win, and the only way to know is to benchmark it against
what you already have, not assume it from the name.

**Hit gating.** Partial progress. AOE lingering-hit gating originally used a
per-target cooldown buffer, decremented and compacted every tick by its own
dedicated system. That system was deleted and replaced with one scalar
"next tick" timer per AOE plus an allocation-free scratch list for
within-pass deduplication — removing both the buffer's growth over time and
an entire scheduled system in one move. The same fix was never applied to
the projectile side, which still runs the original linear-scan gate buffer
today. Worth stating plainly: this is an asymmetry that still exists, not a
project-wide "gating used to be slow, now it's fast" story.

**Damage application — the largest throughput win of the six, and the one
with the clearest lesson about threading.** The path went: a per-hit managed
callback into the target, to a batched hit list, to a genuinely
multi-threaded bucketed finalize pass using a `NativeParallelMultiHashMap`
(a thread-safe multi-map — conceptually similar to a concurrent hash map)
to bucket hits per target, resolved with parallel jobs (Unity's job system
is roughly analogous to a thread pool with structured, data-dependency-aware
scheduling) — and then explicitly reverted back to a single-threaded job a
week later. The commit message records the result directly: multi-threading
it "doesn't provide performance increase... main thread work scaling with
the problem size is required to distribute it." In plain terms: once the
per-item work is small, the fixed overhead of distributing it across
threads — synchronizing, bucketing, waiting on workers — costs more than
the parallelism saves, and single-core out-of-order execution was already
extracting a fair amount of throughput from the work without needing
explicit threads at all.

What actually stuck wasn't parallelism — it was aggregation. Every hit
against a target in a given frame now collapses into a single call carrying
one summed damage value, instead of one managed call per hit. That matters
because Burst-compiled ECS code and ordinary managed C# code sit on either
side of a real boundary — crossing from native/Burst code into managed code
has a fixed per-call cost, similar in kind to a syscall or FFI boundary
crossing. Fewer, bigger calls across that boundary beats many small ones,
independent of how much total work is being done. Reported throughput went
from roughly 1.5k events/ms before this change to about 10k events/ms after.
It's worth being precise about the evidence here: the git history doesn't
carry that exact pair of numbers. The one profiling note actually committed
from that period instead records "about 6.7k damage events costing multiple
milliseconds on the main thread even with callbacks disabled" as the
pre-optimization baseline — a different measurement (a raw event count, not
an events/ms rate) from the same "before" state, not a direct confirmation
of 1.5k → 10k specifically.

The threading revert wasn't the end of the attempts, either — though the
next round stayed off to the side rather than landing in the game itself.
Four more techniques were tried against damage application, each as a
**synthetic benchmark**: a small, standalone harness built to reproduce just
that one workload in isolation, not a change ever merged into or run inside
the actual project. That distinction matters for reading the result
correctly — nothing below was implemented in this codebase, only measured
against a stand-in for it. The four: **software prefetching** (issuing an
instruction that hints the CPU to start pulling data into cache before it's
actually needed, to hide memory latency behind other work), sorting hit
events by target ID with **pdqsort** (a fast general-purpose comparison
sort), sorting by target ID with a **radix sort** (a non-comparison sort
that buckets values by digit/key, which can beat comparison sorts when the
key range is well-behaved), and a further parallel variant. None were
performance-positive — results ranged from 175% of baseline run time up to
6000% (60x slower) in the worst case. Two caveats worth keeping attached to
this one: it's a strong data point that "obviously good" micro-optimizations
(prefetching, sorting for locality) can make things dramatically worse in
practice, and — precisely because it was synthetic, standalone work — it's
also not independently git-verifiable: no matching commits, branches, or
plan docs exist for this round in this repository, unlike every other
finding here, so it's the project owner's own recollection of that external
benchmarking rather than something checkable here.

**Rendering — the other big win, and the one with a clean, directly measured
before/after.** Batched GPU instancing was the starting point from commit
one, via `Graphics.RenderMeshInstanced`, capped at 1023 instances per call —
meaning scaling projectile count still meant scaling draw-call count, not
just paying a fixed per-frame cost. A shared texture atlas replaced
per-effect textures in early July (avoiding the cost of switching textures
mid-batch, which — like a context switch — has its own overhead). That same
week, the project tried a real **indirect-draw** architecture: a pair of
GPU buffers driven by a custom render pass, aimed at collapsing rendering to
one draw call regardless of count. It was reverted four days later — the
indirect draw doesn't execute inside URP's 2D renderer's sorting-layer pass
(the mechanism that controls draw order for compositing 2D sprites against
each other), so combat sprites couldn't composite correctly against
player/mob/VFX layers.

What replaced it, and ships today, keeps the "one draw call regardless of
count" goal but gets there through a real Unity `Renderer` instead of a
custom render pass, specifically so it still participates in normal 2D
sorting. One persistent mesh — sized to the largest instance count seen so
far, doubled whenever it needs to grow, and never shrunk back down, so
normal count fluctuation doesn't repeatedly reallocate it — is drawn by one
ordinary `MeshRenderer` every frame. Every projectile and AOE contributes
one quad (a two-triangle rectangle) to that single mesh, all sharing one
material and one shared atlas texture page, so from Unity's own sorting and
culling system's point of view it's just one object, not thousands.

The per-instance data that actually varies frame to frame — each sprite's
position, rotation, and a packed metadata value — travels to the GPU
separately, in a `StructuredBuffer`: a flat, GPU-resident array a shader can
index directly, conceptually similar to handing the GPU one big table and
letting each vertex look up its own row. Each quad's own vertices carry
their instance's row number stashed in a spare per-vertex data channel
(`TEXCOORD1.x`, a leftover UV-coordinate slot repurposed as an index rather
than an actual texture coordinate), so the shader knows which row of that
table describes the sprite it's currently drawing. Because the atlas is
packed once ahead of time rather than rebuilt at runtime, each sprite kind's
UV mapping into the shared atlas texture (accounting for the packer
possibly rotating a sprite 90 degrees to pack it more tightly) is uploaded
once into a second, much smaller lookup buffer, rather than recomputed or
re-uploaded per instance per frame. Net effect: the *only* per-frame GPU
traffic that scales with active projectile count is that one compact
per-instance buffer upload — 32 bytes per instance — everything else about
the draw (mesh capacity, atlas pixels, per-kind UV mapping) stays fixed
unless the active count actually grows past the mesh's current capacity.

The measured numbers, from checking out the last commit on the
`RenderMeshInstanced` path (`d5c71802`, 2026-07-02, immediately before the
atlas rewrite) and profiling it against current `master` in the same
benchmark scene: the old path's render-system marker cost 9.33ms at ~80k
active projectiles; the current path's equivalent marker costs 2.43ms at
~200k active projectiles. Normalized per projectile (the two runs weren't at
the same count, so raw milliseconds alone would be misleading): about
116.6ns/projectile old versus about 12.2ns/projectile now — **roughly a
9.6x per-projectile improvement**.

**Spawn/despawn.** The driving constraint here isn't avoiding
`Instantiate`/`Destroy` for its own sake — it's minimizing **chunk churn**.
In ECS, entities of the same component makeup are stored together in
contiguous memory blocks called archetype chunks (this is the SoA layout
again, at the entity-storage level). Creating or destroying an entity, or
adding/removing a component, is a **structural change**: it moves the
entity to a different chunk, which forces a synchronization point — every
worker thread has to stop and agree on the new layout before continuing,
similar in spirit to a stop-the-world GC pause. Toggling an "enabled" flag
on an entity that already exists does neither: no chunk move, no sync
point, just flipping a bit.

Projectiles were ECS-native from the first commit, so there's no
`GameObject.Instantiate`/`Destroy`-per-projectile phase in this history
(that pattern only ever existed for mobs) — but the very first ECS version
still destroyed the entity itself on expiry, which is exactly the
chunk-moving cost the later design eliminates. The design went through
several real iterations: destroy-on-expiry (day one) → an enableable
"active" tag adopted the same day, but without a dedicated reuse-allocation
system yet → an actual reuse pool landing five days in as an explicit
"fix spawn leak" commit, turning a despawn/respawn cycle into two flag
toggles instead of a destroy-then-create → that pool's slot-claiming
rewritten from a sequential main-thread scan to a per-chunk parallel
claim/reset two weeks later → spawn intent split from allocation in a
larger rewrite two weeks after that, which explicitly kept the by-then-proven
reuse/cold-create machinery "almost verbatim" rather than touching it → a
dedicated, bounded, rate-smoothed cleanup system added about five weeks in
to reclaim genuinely excess disabled slots — but only once the smoothed
despawn rate stays ahead of the spawn rate by a margin, never during a
climb or busy equilibrium. (An earlier hand-written trim plan for the same
problem was drafted but superseded before it was ever implemented.) The
throughline across every iteration: keep the hot despawn/respawn path as
reuse — cheap, no chunk move — and push every genuine entity destroy into a
bounded, deferred, off-hot-path system instead.

**VFX dispatch.** Each effect root originally owned its own dispatcher
instance and its own per-type resources; that got pulled into one dedicated
dispatch subsystem, and a later pass deduplicated resource ownership by
effect *asset* instead of by trigger *type*, and removed a per-frame request
cap entirely. The clearest single optimization here is a **counting sort**
job that buckets a whole frame's queued VFX spawn requests by effect type
into contiguous arrays — counting sort is a non-comparison sort that works
in linear time when the range of keys (effect types, here) is small and
known ahead of time, and it replaced a main-thread loop that dequeued and
staged requests one item at a time.

**The pattern across all six.** The fix that actually stuck was almost
never "make it parallel." It was collapsing many small, scattered operations
— per-target scans, per-hit callbacks, per-item dequeues, per-instance draw
calls — into one contiguous, batched pass. Two separate attempts at heavier
parallelism (the damage finalize pass, the BVH collision broad-phase) were
explicitly tried, measured, and rejected as not worth their overhead. If
that sounds familiar from backend work — batching N small database calls
into one query beating "parallelizing" the N calls, or discovering that
adding worker threads to a memory-bandwidth-bound job doesn't help because
the cores are all fighting over the same bus — that's because it's the same
underlying principle. The medium is different; the reasoning isn't.
