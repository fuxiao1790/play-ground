# Centralized Sound Event Lane (Skill Cast Sounds First)

## Summary

Build one path for sound: producers enqueue a plain `SoundEvent`, one bridge
drains it once per frame, applies selection policy, and hands survivors to a
pooled-voice player. Both ECS/Burst code and GameObject code produce into that
path.

Only **one producer is wired by this plan** — skill cast sounds from
`SkillDriver`. Hit, spawn, and death sounds need no lane change when they land
later; they enqueue the same struct with a different `SoundCategory`. That is
the reason for building the lane now instead of calling `AudioManager.PlaySound`
directly from the cast site.

### Why cast sounds are not a low-count category

`MobRoot.cs:505` calls `skillDriver.Tick(true, ...)`, so **mobs cast too**.
Cast-sound rate is `mobCount × slots / recovery`, not `1 / recovery`, and
`SkillDriver.Tick` fires *every ready slot every frame* while `fireHeld`
(`SkillDriver.cs:96-125`) — it is auto-repeat, not one-per-press. Cast sound
needs real culling and distance culling on day one. A direct `PlaySound` call at
the spawn site would ship a known-broken budget.

### Current state

- `AudioManager` (`Assets/Scripts/Audio/AudioManager.cs`) is a working pooled
  voice player with per-clip cap and same-clip start spacing. **It has zero
  callers.** `SkillDriver.cs:32` serializes it, `SkillDriver.cs:71` resolves it,
  nothing ever calls `PlaySound`.
- No `AudioClip` field exists anywhere under `Assets/Scripts/Skills/`. There is
  no authored sound data to play.
- No audio tests exist.

## Rationale

### Decision 1 — `AudioManager` moves into `PlayGround.Sim`

Load-bearing and non-obvious. `PlayGround.GameLogic.asmdef` sits at
`Assets/Scripts/`, so `Assets/Scripts/Audio/AudioManager.cs` is **GameLogic**.
`PlayGround.Sim.asmdef` (`Assets/Scripts/System/`) references no PlayGround
assembly at all. Per `Docs/architecture/layer-rules.md` §*Package Boundary* the
reference direction is `Ui -> GameLogic -> Sim`, one-way.

The bridge is an ECS presentation system, and layer-rules.md states plainly that
"the combat bridge and ECS-serving presentation components live in Sim with the
ECS runtime." **A bridge in Sim therefore cannot call an `AudioManager` in
GameLogic.**

`CombatVfxRoot` already resolved this exact problem the same way: it is a
`MonoBehaviour` living at `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`, inside
Sim, precisely so Sim-side dispatch can use it while GameLogic (`SkillDriver`)
still references it downward. Audio conforms to that precedent.

Rejected alternatives:

- *Define `ISoundPlayer` in Sim, keep `AudioManager` in GameLogic.* This is
  adapter code whose only purpose is to avoid moving a file — the exact pattern
  `plan-changes.md` flags as a structural warning. `ICombatTarget` earns its
  interface because many unrelated GameLogic types implement it; a single
  audio player does not.
- *Put the bridge in GameLogic instead.* It would compile (GameLogic references
  Unity.Entities) but violates the stated layer rule, and it fails the real test:
  future ECS producers must stamp sound ids into **Sim-owned components** beside
  `AoeVfxIds`. A registry stranded in GameLogic cannot serve Sim components.

The move is mechanical — `AudioManager` imports only `UnityEngine` and
`System.Collections.Generic`, with no GameLogic dependency.

### Decision 2 — the clip registry folds into `AudioManager`, no new root type

Burst cannot hold an `AudioClip` reference (`layer-rules.md` §*Managed And
Unmanaged Data*: jobs must not read ScriptableObjects or managed objects), so
events must carry an `int` clip id resolved through a managed table.

That table goes **on `AudioManager`**, not in a new `CombatAudioRoot`.
`AudioManager` is already the scene-level audio root and already a static
singleton; adding `Register(AudioClip) -> int` plus an id→clip lookup adds no new
concept. A separate root would duplicate the singleton and root-component role to
hold one dictionary — hiding complexity rather than removing it.

Id convention mirrors what the codebase already uses in two places: `0` means
"none", and producers guard with `if (id <= 0) return;` exactly as
`VfxEmit.cs:15` does and as `RuntimeSkillDefinition.RenderId` documents
("0 means 'no render'").

### Decision 3 — two containers, one event type, merged at one point

**This is the constraint that shapes the whole design.** The existing
`.agent/burst-onupdate-work/index.md` §*Why the dual event path stays* records a
decision that applies directly here:

> A raw `NativeQueue.Enqueue` from managed gameplay code has no such protection —
> every caller would have to complete the lane's `ProducerHandle` by hand at an
> unpredictable point in the frame.

So managed producers must **not** write the native queue. Instead:

- **ECS/Burst producers** → `SoundEventSingleton.Events`, a
  `NativeQueue<SoundEvent>` written through `.AsParallelWriter()` and chained on
  `ProducerHandle`. Standard lane shape.
- **GameObject producers** → `AudioManager.Enqueue(in SoundEvent)`, appending to
  a plain managed `List<SoundEvent>`. Main-thread only by construction, so no
  safety contract exists to violate.
- The bridge **merges both** at drain, exactly as the four spawn expansion
  systems already merge a scope `DynamicBuffer` with a lane `NativeQueue`.

Two containers, but **one event type and one merge point**, which is what
"centralized" has to mean here. A managed `List` is chosen over a
`DynamicBuffer` because sound has no entity to own a buffer and no need for ECS
dependency tracking — the list is written and read on the main thread in the
same frame.

### Decision 4 — selection policy lives in the bridge, not in `PlaySound`

`AudioManager.PlaySound` decides one event at a time, so it is
first-come-first-served: a mob-cast flood that arrives early wins the pool and a
later, more valuable sound is refused (`AudioManager.cs:154` returns `null` when
full). The bridge sees the **whole frame's events at once**, so ranking is a
sort over a batch rather than a greedy guess.

Split accordingly:

- **Bridge** — distance cull, dedupe by clip, rank by category budget and by
  novelty (a clip's first instance outranks its Nth), take the top N.
- **`AudioManager`** — pooled voice allocation, playback, pruning, counters. Its
  per-clip cap and spacing become a backstop rather than the primary limiter.

Priority ordering by *kind over count* is the user's stated requirement: a
repeated sound thinned is inaudible, a whole sound kind missing is audible.

## Constraints & Invariants

- **Assembly reference direction is one-way `Ui -> GameLogic -> Sim`; Sim
  references no PlayGround assembly.** Source:
  `Docs/architecture/layer-rules.md` §*Package Boundary*; verified against
  `PlayGround.Sim.asmdef`, which lists only Unity packages. → Forces Decision 1.
  No task may add a PlayGround reference to `PlayGround.Sim.asmdef`.
- **Jobs and simulation systems must not read GameObjects, ScriptableObjects, or
  managed values.** Source: `layer-rules.md` §*Managed And Unmanaged Data*. →
  `SoundEvent` is fully unmanaged; the clip is an `int` id.
- **Runtime ECS data must be copied from authoring data before simulation.**
  Source: `layer-rules.md` §*Runtime, Authoring, And Configuration*. → Clip ids
  resolve once at compile/registration time, never by reading the authoring
  `Skill` asset at cast time.
- **Lane singletons own their native containers and expose explicit `JobHandle`
  fields; producers combine into `ProducerHandle` on the main thread, sinks
  complete before draining.** Source: `Docs/coding-standards.md` §*System
  Encapsulation*. → `SoundEventSingleton` follows the canonical shape; the
  bridge completes before drain, like `CombatApplyBridge.cs:33`.
- **Systems must not reach into other systems' collections;
  `GetExistingSystemManaged` + field access is banned.** Same source. → The
  managed producer path goes through `AudioManager`, a scene root, not through a
  system field.
- **`NativeQueue` contents are an unordered set under parallel writes.** Source:
  `coding-standards.md` §*Why NativeQueue for These Sinks (Not Ordering)*. →
  The bridge must sort explicitly; nothing may depend on enqueue order.
- **Any code creating a native handle owns disposal on every teardown path.**
  Source: `coding-standards.md` §*Native And ECS Handle Ownership*. → The bridge
  creates `Events` in `OnCreate` and disposes in `OnDestroy`, including the
  partial-setup path.
- **Combat paths are allocation-light.** Source: `coding-standards.md`
  §*Allocation Rule* (VFX dispatch is named a hot path). → The bridge reuses
  scratch containers across frames, as `CombatApplyBridge.cs:20` does with its
  `static readonly List<StatusStackSnapshot> statusScratch`. No per-event
  allocation.
- **Every scalable system needs an obvious budget and fallback; "impact sounds
  can be culled by same-clip and priority rules".** Source: `coding-standards.md`
  §*Performance Budget Rule*. → The bridge's selection policy is that budget.
- **`audio requests culled` is a required debug counter.** Source:
  `Docs/performance.md` §*Required Counters*; §*Audio* names pooled AudioSources,
  same-clip duplicate culling, and priority/distance culling. → Counters are a
  deliverable, not optional, and are not a test-only hook under
  `coding-standards.md` §*Test Hooks*.
- **Audio is a scene object.** Source: `performance.md` §*Runtime Strategy*
  ("scene objects for player, mobs, walls, camera, spawners, **audio**, and
  debug"). → `AudioManager` stays a `MonoBehaviour`; the move changes assembly,
  not kind.
- **Cross-MonoBehaviour work belongs in `OnEnable`/`Start`, not `Awake`.**
  Source: `coding-standards.md` §*Awake vs OnEnable Boundary*. → `SkillDriver.cs:71`
  currently resolves `AudioManager.Instance` in `Awake`, which races
  `AudioManager.Awake` (`AudioManager.cs:38`). Pre-existing latent bug; fixed in
  task 005 rather than carried forward.
- **Presentation must not mutate simulation state except clearing buffers owned
  by that phase.** Source: `Docs/layers/presentation-and-feedback.md`
  §*Forbidden Dependencies*. → The bridge clears only the sound lane.

## Mechanisms Reused vs. Introduced

**Reused:**

- Lane singleton + `ProducerHandle` + drain-and-clear, copied from
  `CombatApplyResultSingleton` / `CombatApplyBridge`.
- `PresentationSystemGroup` bridge shape: `CompleteDependency()` →
  `ProducerHandle.Complete()` → drain → clear (`CombatApplyBridge.cs:23-59`).
- Managed id registry with dedupe and `0` = none, copied from
  `CombatVfxRoot.Register` (`CombatVfxRoot.cs:52-110`, `idsByAsset`).
- The `RenderId`-style "resolve an int id during `CompileAndRegister`" pass in
  `SkillDriver`, alongside the existing `RegisterProjectileTypes` /
  `RegisterAoeVfx` calls.
- Dual managed/native producer path merged at one sink — the shape the spawn
  expansion systems already use and that `burst-onupdate-work` explicitly
  ratified.
- `AudioManager`'s existing pool, per-clip cap, spacing, and counters. No
  rewrite; it is demoted from decision-maker to voice pool.

**Introduced:**

- `SoundEvent` + `SoundCategory` — one new data contract, the point of the plan.
- `SoundEventSingleton` — one new lane, conforming exactly to the canonical
  lane list in `coding-standards.md` §*System Encapsulation*.
- `SoundEventBridge` — one new presentation system.

No new root component, no new interface, no adapter.

## Design Validation

| Invariant | Held? |
|---|---|
| Sim references no PlayGround assembly | Yes — `AudioManager` moves *into* Sim (task 001); no asmdef reference is added. |
| Jobs never read managed objects | Yes — `SoundEvent` is `int`/`float2`/enum only; clip resolution happens in the bridge, on the main thread. |
| Authoring data copied before simulation | Yes — clip id resolved in `CompileAndRegister` (task 005), same pass as `RenderId`. |
| Lane owns container + explicit handle | Yes — `SoundEventSingleton` mirrors `CombatApplyResultSingleton`. |
| Managed code never raw-enqueues a native lane | Yes — Decision 3; managed producers use a `List<SoundEvent>` on `AudioManager`. |
| `NativeQueue` order not relied upon | Yes — bridge sorts before selecting; ordering is never assumed. |
| Native handle disposal on every path | Task 003 acceptance criterion; disposal in `OnDestroy` including partial create. |
| Allocation-light | Yes — reused scratch containers, no per-event allocation; task 004 criterion. |
| Budget + fallback exists | Yes — bridge selection policy is the budget; overflow degrades by dropping redundant copies first. |
| `audio requests culled` counter exposed | Yes — task 004 splits culls into redundant vs novel and publishes both. |
| Presentation does not mutate sim state | Yes — bridge clears only its own lane. |
| Awake/OnEnable boundary | Fixed — task 005 moves `AudioManager` resolution out of `SkillDriver.Awake`. |

## Minimal/Additive vs. Refactor Comparison

**Minimal/additive approach** (rejected): leave `AudioManager` in GameLogic, add
an `AudioClip` field to `Skill`, call `AudioManager.Instance.PlaySound(...)`
directly at the cast site in `SkillDriver.Tick`.

- resulting data flow: `SkillDriver` → `PlaySound`, one event at a time, greedy
  first-come-first-served allocation. ECS has no path to sound at all.
- new concepts/types introduced: none now — but hit/spawn sounds later cannot
  reach `AudioManager` from Sim, forcing either an `ISoundPlayer` interface plus
  a registration shim, or a second sound path built in GameLogic.
- copies/translations added: none now; later, an adapter layer whose only job is
  crossing an assembly boundary that Decision 1 removes outright.
- long-term cost: the greedy allocator is retained as the selection point, so the
  "many kinds over many copies" requirement can never be implemented properly —
  it needs whole-frame visibility that a per-call API cannot provide. Two sound
  paths (managed direct-call, ECS lane) must then be kept in sync.

**Refactor approach** (chosen): move `AudioManager` into Sim, fold the registry
into it, add the lane and the bridge, wire one producer.

- resulting data flow: managed producers → `AudioManager` managed list; Burst
  producers → lane `NativeQueue`; **one** bridge merges, ranks, and plays.
- existing concepts/types changed or removed: `AudioManager` changes assembly and
  namespace and loses its role as selection authority; `SkillDriver.cs:32/71`
  wiring is replaced rather than left dead.
- copies/translations removed or avoided: no interface shim, no second sound
  path, no per-call world lookup, no adapter between GameLogic and Sim.
- long-term benefit: adding hit or spawn sounds is a `SoundCategory` value and a
  producer call — no plumbing. Selection policy has exactly one home.

**Decision: refactor.** Reason: the additive version's cost is not extra code, it
is that the one requirement driving this work — prioritize kinds over copies —
is unimplementable behind a per-call API, and the assembly boundary blocks ECS
producers entirely. Both are structural, and both are removed by a file move plus
one lane.

## Default Decision Rule Applied

One representation pair was examined and **deliberately not collapsed**: the
managed `List<SoundEvent>` and the native `NativeQueue<SoundEvent>` both describe
"a pending sound event". They stay separate because they carry different *safety*
contracts (main-thread managed write vs. parallel-job write) — the same concrete
reason recorded in `.agent/burst-onupdate-work/index.md` for the spawn buffer/queue
pair. They share one element type and one merge point, so there is still a single
source of truth for what a sound event *is*. Recorded so a later reader does not
reopen it as an oversight.

## Task List

Sequential — each task depends on the one before it, except 006.

| # | Task | Scope |
|---|---|---|
| [001](./001-move-audiomanager-to-sim.md) | Move `AudioManager` GameLogic → Sim | Small, mechanical |
| [002](./002-audio-clip-registry.md) | Clip id registry on `AudioManager` | Small |
| [003](./003-sound-event-lane.md) | `SoundEvent`, `SoundCategory`, `SoundEventSingleton` | Small |
| [004](./004-sound-event-bridge.md) | `SoundEventBridge` + selection policy + counters | **Largest** |
| [005](./005-skill-cast-producer.md) | Cast clip authoring → id → enqueue | Medium |
| [006](./006-docs.md) | Contract doc + layer/folder doc updates | Small |

**Dependency notes.** 001 must land first — everything else assumes Sim. 004 is
the only task with real design content; 001-003 are plumbing sized to be
reviewable on their own. 006 may land any time after 004 fixes the shape.

## Open Questions

None blocking. Three judgment calls were resolved from code and docs rather than
escalated:

- *Managed producers writing the native queue directly?* — No. Resolved from
  `.agent/burst-onupdate-work/index.md` §*Why the dual event path stays*, which
  already rejected exactly this for spawn events. Recorded under Decision 3.
- *New `CombatAudioRoot` type?* — No. Resolved from `coding-standards.md`
  §*Root Component Rule*: `AudioManager` is already the audio root. Recorded
  under Decision 2.
- *Fail loud or drop silently when the lane is unavailable?* — Split, because
  `coding-standards.md` §*Fail Fast Validation* bans "hiding bad setup with no-op
  behavior" while a missing sound must never throw mid-combat. Resolution:
  **wiring** is validated fail-fast at setup (task 005 validates the serialized
  reference), **individual events** drop into a counter (task 004). The
  distinction is deliberate and is not the fail-loud-singleton rule that applies
  to sim-critical combat singletons — audio is not sim-critical.

One item is deliberately deferred, not forgotten: **distance culling needs a
camera-space reference in Sim**. Task 004 implements it against the bridge's own
serialized/queried view rectangle; if that proves awkward, the fallback is stated
in the task file rather than here.

## Testing

Per `Docs/project-overview.md`, agents do not run tests. There is currently **no
audio test coverage at all** — `Assets/Tests/` contains no reference to
`AudioManager`, `PlaySound`, or audio. Tasks 002 and 004 add EditMode tests for
the registry and the selection policy respectively; both are pure functions over
data and need no scene.

After all tasks land:

```
Unity.exe -runTests -batchmode -projectPath "e:/UnityHub/projects/play-ground" -testPlatform EditMode -testResults "e:/UnityHub/projects/play-ground/TestResults/skill-sound-events-editmode-results.xml"
```

```
Unity.exe -runTests -batchmode -projectPath "e:/UnityHub/projects/play-ground" -testPlatform PlayMode -testResults "e:/UnityHub/projects/play-ground/TestResults/skill-sound-events-playmode-results.xml"
```

Report only against the exported XML.

## Editor Work (User)

Agents do not edit Unity YAML. These steps are yours:

1. Move the `AudioManager` GameObject's script reference if Unity does not
   auto-resolve the namespace change (task 001).
2. Assign a cast `AudioClip` on each `Skill` asset you want audible (task 005).
3. Place an `AudioManager` in any scene under test — it currently exists only in
   `Assets/Scenes/BenchmarkLarge.unity`.
