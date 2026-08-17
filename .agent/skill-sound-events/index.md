# Skill Cast Sounds Through One Frame-Batched Audio Root

## Summary

Producers enqueue a plain `SoundEvent`. One scene root drains the whole frame's
events in `LateUpdate`, ranks them, and plays the survivors through a pooled
voice set. Only one producer is wired: skill cast sounds from `SkillDriver`.

The batch drain — not a per-call `PlaySound` — is the point of the design. The
stated requirement is *prioritize many kinds over many copies*, and that decision
cannot be made one event at a time; it needs the frame's whole set at once.

### What changed from the previous revision of this plan

The previous revision preserved `AudioManager` (move to Sim, extend, keep its
API) and built an ECS lane + presentation bridge around it. Both are dropped:

- **`AudioManager` is deleted, not moved.** It has zero callers
  (`SkillDriver.cs:32` serializes it, `SkillDriver.cs:72` resolves it, nothing
  calls `PlaySound`). There is no behavior to preserve and no migration to
  perform, so it is not a constraint on the design — it is a file to delete.
- **The `NativeQueue` lane, `SoundEventSingleton`, and `SoundEventBridge` are
  dropped.** They existed to serve Burst producers. There are none, today or in
  this plan. See Decision 3.

Net effect: six tasks become four, and the runtime gains one type instead of
four.

### Why cast sounds are not a low-count category

`MobRoot.cs:505` calls `skillDriver.Tick(true, ...)`, so **mobs cast too**. Cast
rate is `mobCount × slots / recovery`, not `1 / recovery`, and `SkillDriver.Tick`
fires *every ready slot every frame* while `fireHeld`
(`SkillDriver.cs:100-129`) — auto-repeat, not one-per-press. Cast sound needs
real culling and distance culling on day one; a direct per-call play at the spawn
site would ship a known-broken budget.

### Current state

- `Assets/Scripts/Audio/AudioManager.cs` — pooled voice player with per-clip cap
  and same-clip spacing. **Zero callers.** Referenced by the
  `BenchmarkLarge.unity` scene (GUID `944e3f8cd2ab47d3a41aa6f16f7d3a21`) and by
  `Assets/_Recovery/0.unity`.
- No `AudioClip` field exists anywhere under `Assets/Scripts/Skills/`. There is
  no authored sound data to play.
- No audio tests exist. `Assets/Tests/` has no reference to audio at all.

## Rationale

### Decision 1 — one new type, `AudioRoot`, replacing `AudioManager` outright

`AudioManager` is dead code. Keeping it would mean designing around an API
(`PlaySound(clip, position, priority, maxSimultaneous)`) that is actively wrong
for this plan: it is a per-call greedy allocator that returns `false` when the
pool is full (`AudioManager.cs:154`), so whoever calls earliest wins regardless
of value. That is precisely the decision the batch drain has to take away from
it. Preserving it would leave two selection points, one of which must be
neutered.

So: delete the file, write `AudioRoot` for the shape this plan needs. Its pool,
per-clip cap, and same-clip spacing logic are worth **re-using as reference
material** — they are correct — but they are re-derived inside a type whose drain
is frame-batched rather than grafted onto one whose drain is per-call.

Naming follows `CombatVfxRoot`, the sibling this most resembles.

### Decision 2 — `AudioRoot` lives in `PlayGround.Sim`

`Docs/architecture/layer-rules.md` §*Package Boundary* fixes the reference
direction as `Ui -> GameLogic -> Sim`, one-way, and `PlayGround.Sim.asmdef` lists
only Unity packages.

Sim is therefore the **only** location callable from both sides: GameLogic
(`SkillDriver`) may reference down into Sim, and any future ECS presentation code
is already in Sim. GameLogic is callable from one side only.

This costs nothing — `AudioRoot` needs only `UnityEngine`,
`System.Collections.Generic`, and `Unity.Mathematics` — and it is a folder
choice, not an extra type or an extra data path. `CombatVfxRoot` is the exact
precedent: a `MonoBehaviour` at `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`,
inside Sim, referenced downward by `SkillDriver.cs:31`.

Namespace `PlayGround.System.Combat.Audio`, matching the sibling
`PlayGround.System.Combat.Vfx`.

### Decision 3 — no ECS lane, no bridge system, until a Burst producer exists

**This is the largest change from the previous revision.**

The previous revision built `SoundEventSingleton` (a `NativeQueue<SoundEvent>`
plus `ProducerHandle`) and a `SoundEventBridge` `SystemBase` in
`PresentationSystemGroup`, then merged that native path with a managed
`List<SoundEvent>` at drain. On day one, the native half carries zero events:
the only producer in this plan is `SkillDriver.Tick`, a main-thread
`MonoBehaviour` call reached from `PlayerRoot.Update` (`PlayerRoot.cs:234`) and
`MobRoot.Update` (`MobRoot.cs:505`).

That is copying a mechanism's *stage count* rather than its *concepts*. The lane
shape in `Docs/coding-standards.md` §*System Encapsulation* exists to solve a
specific pressure — parallel job writes that need an explicit `JobHandle`
rendezvous before a sink reads. Sound has no such writer, so the lane would be
pure ceremony: a native container to allocate, dispose on every teardown path,
complete, drain, and clear, all to move zero elements.

Instead: one `List<SoundEvent>` on `AudioRoot`, appended on the main thread,
drained in `LateUpdate`. Unity runs every `Update` before any `LateUpdate`, so
the drain sees the complete frame's cast events. `Docs/coding-standards.md`
§*Update Timing* already assigns exactly this role to `LateUpdate` ("queued
target proxy deletion on actors ... keeps proxy entities valid through the
simulation and presentation work that may still reference them in the current
frame").

**When a Burst producer does land**, the addition is a `NativeQueue<SoundEvent>`
lane plus a small system that drains it into `AudioRoot.Enqueue` before
`LateUpdate`. Nothing built here changes: `SoundEvent` is already unmanaged, the
clip is already an `int` id, and `AudioRoot.Enqueue` is already the single merge
point. The extension is additive by construction, which is why deferring it costs
nothing.

Two supporting observations, recorded so this is not reopened as an oversight:

- `SoundEvent` stays fully unmanaged (`int` / `float2` / enum) even though
  nothing needs that yet. Unlike the lane, it is a data-shape choice with zero
  added code, so it is free insurance rather than speculative machinery.
- Hit sounds — the most likely next producer — may not need a native lane
  either. `CombatApplyBridge.cs:83-107` already replays every combat tick result
  on the main thread during presentation, with `HitCount` and `CritCount` in
  hand. A hit-sound producer can enqueue from that loop directly.

### Decision 4a — sound is authored on the basic prefab, mirroring VFX

Clips live on the prefab beside the `VisualEffectAsset` they accompany, and
travel the same five-stage path VFX already travels: prefab field → definition
pass-through → abstract on the definition base → compiler copy → driver resolves
an int id into an ids struct.

| Stage | VFX | Sound |
|---|---|---|
| Prefab holds the asset | `BasicAoePrefab.cs:20` `spawnEffect` | `spawnSound`, `spawnSoundRadius` |
| Definition passes through | `SkillDefinition.cs:99` | `SpawnSound`, `SpawnSoundRadius` |
| Compiler copies | `SkillSetCompiler.cs:362` | same site |
| Driver resolves id | `SkillDriver.cs:1250` → `AoeVfxIds` | `RegisterSounds` → `SkillSoundIds` |

Nothing presentation-shaped lives on the `Skill` ScriptableObject today, so
putting clips there — as an earlier revision of task 003 did — would have created
a second authoring home for the same kind of data, and left sound as the only
presentation asset a designer looks for somewhere other than the prefab.

Two consequences worth stating up front:

- **`BasicAttackPrefab` gets a sound slot with no VFX sibling.** Projectiles
  author no VFX at all today — there is no `spawnEffect` on that prefab and no
  `RegisterProjectileVfx`. A projectile skill still has to be audible when cast,
  so it gets the field regardless. It also lives in `PlayGround.Sim` rather than
  GameLogic; `AudioClip` is `UnityEngine`, so the field is legal and adds no
  assembly reference.
- **Only the `spawnSound` slot is added, not the full Spawn/Hit/Expire/Pulse/
  Arming set.** A `hitSound` field with no producer is an authored clip that
  silently never plays — worse in the inspector than an absent field. The chain
  above is what makes each further slot a one-line change per stage, which is the
  whole reason for conforming to it.

### Decision 4b — the clip registry lives on `AudioRoot`

Events carry an `int` clip id resolved through a managed table, not an
`AudioClip` reference.

Justified by today's needs, not by the future Burst constraint: the selection
pass groups and counts events **per clip**, and an `int` key is what that
grouping uses. It also matches how skills already carry presentation identity —
`RuntimeSkillDefinition.RenderId` (`RuntimeSkillDefinition.cs:10`) is an `int`
resolved once during `CompileAndRegister`, and `SkillSoundIds` sits in exactly
that slot beside it — the direct analogue of `AoeVfxIds`
(`AoeVfxEcsComponents.cs:19-26`).

The table goes on `AudioRoot` — the same place `CombatVfxRoot` keeps `idsByAsset`
(`CombatVfxRoot.cs:23`), and `Docs/coding-standards.md` §*Root Component Rule*
puts one coordinating root per concern. A separate registry type would duplicate
the root role to hold one dictionary.

Id convention, matching two existing precedents: `0` means "none", and producers
guard with `if (id <= 0) return;` exactly as `VfxEmit.cs:15` and `VfxEmit.cs:37`
do.

### Decision 5 — `AudioRoot` owns all processing; there is no policy type

Culling, ranking, jitter, and playback are all `AudioRoot`'s job. No
`SoundSelection` class, no settings struct, no play-request struct, no cull-count
struct. Producers hand over an event and stop having opinions.

An earlier revision split the ranking into a pure static class for testability.
That bought four types to serve one method — decomposition that hides the logic
rather than removing any — against `Docs/coding-standards.md` §*Root Component
Rule*, which puts one coordinating root per concern. If profiling later shows the
drain is hot enough to need a different shape (a job, a burst-compiled sort), that
is the evidence that would justify splitting it. Absent that evidence, it stays
one type.

The testability concern is answered without a type: the ranking method takes
`now`, `listenerPosition`, and `freeVoices` as **parameters** and touches no
`AudioSource`, `Time`, or `Camera`. Tests call it on a plain `AudioRoot` instance
and never trigger playback — which also sidesteps EditMode having no audio device
and no advancing `Time.time`.

Ranking, in order:

1. **Spatial cull** against each event's own `AudibleRadius`, falling back to a
   serialized default.
2. **Group by `ClipId`**, assigning a copy index.
3. **Per-clip frame cap** — drop copies beyond `maxCopiesPerClipPerFrame`
   **regardless of free voices**. Identical clips started on one frame are
   sample-aligned and sum coherently (`+6 dB` at two copies), so they read as one
   loud sound; spare voices do not fix that. Duplicates are capped by kind,
   scarcity only decides how many kinds survive.
4. **Rank** by `Priority` descending, then by copy index ascending — a clip's
   first copy outranks any clip's second copy. This is the rule that implements
   "kinds over copies".
5. **Take the top N**, `N = free voice count`.
6. **Volume falloff on repeats** — a copy plays at
   `repeatVolumeFalloff^copyIndex`. With the cap at `3` the ladder terminates at
   `1.0, 0.8, 0.64`, and the headroom freed is what lets other kinds be heard.

`AudioRoot` then adds pitch jitter to every voice and a short random start delay
to copies after the first. Decorrelating aligned duplicates is response policy,
so the values are `AudioRoot` configuration and the per-copy choices are locals
in the drain — none of it reaches a payload.

**The per-category voice floor from the previous revision is cut.** With `Cast`
as the only producing category it is inert by the previous plan's own admission,
and an inert mechanism cannot be validated by any test that reflects real
behavior. `SoundCategory` still rides on the event — one byte, used for counters
and debug readout — so adding the floor when a second category ships is a change
to one function, not to the data contract.

### Decision 6 — the payload is sized for localization; the machinery is not

`SoundEvent` carries `Position`, `Velocity`, `AudibleRadius`, and `Category` so a
later localization pass reads this struct rather than defining its own. The line
that keeps this forward-sizing from becoming a dumping ground:

> **A field belongs in `SoundEvent` if it describes what happened in the world.
> It belongs on `AudioRoot` if it describes how audio should respond.**

World facts: what sounded, where, moving how fast, carrying how far, what kind,
how much it mattered. Every one of those is knowledge only the producer has.

Response policy: pitch jitter, repeat delay, volume falloff, per-clip cap, pool
size, spacing. Every one of those is the manager's decision, and a producer that
could set them would be reaching across into playback. They are serialized fields
on `AudioRoot` and locals in the drain — **no jitter value appears in any payload
or in any struct crossing between producer and manager.**

`Velocity` is Doppler input nobody reads today; producers write `default`. That
is acceptable under the rule because it is a world fact that simply has no
consumer yet — unlike a policy field, it will never need to move.

Also rejected:

- **Per-sound base volume.** A property of the clip, not the occurrence. Belongs
  in the registry beside the clip, authored once, not copied into every event.
- **An emitter handle to follow** (`Transform`, `Entity`, emitter id). A sound
  tracking a moving emitter is not fire-and-forget — the pool must re-position
  that voice every frame and release it when the emitter dies. That is a lifetime
  model, not a field. It arrives as a second play mode with its own task, if ever.

The scene's `AudioListener` and the object the cull measures from **must be the
same object**. Otherwise the game culls sounds it would have panned and pans
sounds it culled, and it presents as "sounds cut out near the screen edge" with
nothing visibly wrong in either component. `AudioRoot` serializes the listener as
a `GameObject`, which makes the check direct —
`listenerObject.GetComponent<AudioListener>()` — at setup and on every
`BindListener` (task 002).

## Constraints & Invariants

- **Assembly reference direction is one-way `Ui -> GameLogic -> Sim`; Sim
  references no PlayGround assembly.** Source:
  `Docs/architecture/layer-rules.md` §*Package Boundary*; verified against
  `PlayGround.Sim.asmdef`, which lists only Unity packages. → Forces Decision 2.
  No task may add a PlayGround reference to `PlayGround.Sim.asmdef`.
- **Runtime ECS/simulation data must be copied from authoring data before
  simulation.** Source: `layer-rules.md` §*Runtime, Authoring, And
  Configuration*. → Clip ids resolve once in `CompileAndRegister`, never by
  reading the authoring `Skill` asset at cast time.
- **Do not hide expensive setup inside repeated runtime calls; pool creation
  happens during setup.** Source: `Docs/coding-standards.md` §*Update Timing*. →
  Voice pool is prewarmed at setup; `Register` is called from
  `CompileAndRegister`, not from the cast site.
- **Cross-MonoBehaviour work belongs in `OnEnable`/`Start`, not `Awake`.**
  Source: `coding-standards.md` §*Awake vs OnEnable Boundary*. →
  `SkillDriver.cs:72` currently resolves `AudioManager.Instance` in `Awake`,
  racing `AudioManager.Awake` (`AudioManager.cs:38`). Pre-existing latent bug;
  the replacement wiring resolves in `OnEnable` (task 003).
- **`Update()` for cooldowns and input, `LateUpdate()` for queued drains that
  must see the finished frame.** Source: `coding-standards.md` §*Update Timing*.
  → Producers enqueue during `Update`; `AudioRoot` drains in `LateUpdate`. A
  producer that enqueues from `LateUpdate` has undefined ordering against the
  drain — recorded as a restriction in the contract doc (task 004).
- **Combat paths are allocation-light.** Source: `coding-standards.md`
  §*Allocation Rule*. → The pending list, the selection scratch, and the output
  list are fields reused across frames. No per-event allocation.
- **Every scalable system needs an obvious budget and fallback; "impact sounds
  can be culled by same-clip and priority rules".** Source: `coding-standards.md`
  §*Performance Budget Rule*. → The selection policy is that budget; overflow
  degrades by dropping redundant copies before novel kinds.
- **`audio requests culled` is a required debug counter**, and §*Audio* names
  pooled AudioSources, same-clip duplicate culling, and priority/distance
  culling. Source: `Docs/performance.md` §*Required Counters*, §*Audio*. →
  Counters are a deliverable, not a test-only hook under `coding-standards.md`
  §*Test Hooks*. `AudioRoot` exposes them; `DebugOverlay` owns the label
  (§*Debug UI Ownership*).
- **Audio is a scene object.** Source: `performance.md` §*Runtime Strategy*
  ("scene objects for player, mobs, walls, camera, spawners, **audio**, and
  debug"). → `AudioRoot` is a `MonoBehaviour`.
- **Prefer serialized references over scene scans; fail fast on bad setup.**
  Source: `coding-standards.md` §*Unity Object Access*, §*Fail Fast Validation*.
  → `SkillDriver` gets a serialized `AudioRoot`; the
  `FindAnyObjectByType<AudioManager>()` fallback at `SkillDriver.cs:72` is
  removed, not carried over.
- **Presentation must not mutate simulation state.** Source:
  `Docs/layers/presentation-and-feedback.md` §*Forbidden Dependencies*. → No
  gameplay decision may read a sound event or the audio counters.

## Mechanisms Reused vs. Introduced

**Reused:**

- Scene-root-with-static-`Instance` shape, plus a managed id registry with
  dedupe and `0` = none — `CombatVfxRoot.cs:13`, `:23`, `:52-110`.
- `id <= 0` producer guard — `VfxEmit.cs:15`, `VfxEmit.cs:37`.
- "Resolve an int presentation id during `CompileAndRegister`" — the existing
  `RenderId` / `TypeId` pass in `SkillDriver` (`SkillDriver.cs:532`, `:1179`).
- The whole prefab → definition → compiler → ids-struct authoring chain, copied
  stage for stage from VFX (`BasicAoePrefab.cs:20`, `SkillDefinition.cs:79/99`,
  `SkillSetCompiler.cs:362`, `SkillDriver.cs:1240-1260`, `AoeVfxIds`).
- Reused scratch containers across frames — `CombatApplyBridge.cs:20`
  (`static readonly List<StatusStackSnapshot> statusScratch`).
- `LateUpdate` as the drain point for work queued during `Update` —
  `coding-standards.md` §*Update Timing*, as already used for queued proxy
  deletion.
- `AudioManager`'s pool / per-clip cap / spacing logic, as **reference material**
  for the equivalent code inside `AudioRoot`.

**Introduced:**

- `AudioRoot` — one `MonoBehaviour`, replacing `AudioManager`.
- `SoundEvent` + `SoundCategory` — one data contract.
- `SkillSoundIds` — the `AoeVfxIds` analogue; a struct rather than a bare `int`
  so the remaining authoring slots cost one field each.

Three small types. No lane singleton, no ECS system, no selection class, no
settings/request/count structs, no interface, no adapter, no second root.

## Design Validation

| Invariant | Held? |
|---|---|
| Sim references no PlayGround assembly | Yes — `AudioRoot` is written *into* Sim (task 001); no asmdef reference is added. |
| Authoring data copied before simulation | Yes — clip id resolved in `CompileAndRegister` (task 003), same pass as `RenderId`. |
| No managed object reaches a job | Yes, vacuously — no job touches audio. `SoundEvent` is unmanaged anyway, so the future lane inherits the property. |
| Setup not hidden in repeated runtime calls | Yes — pool prewarmed at setup; `Register` runs at compile time and dedupes, so recompiles are free. |
| Awake/OnEnable boundary | Fixed — task 003 resolves the reference in `OnEnable`, not `Awake`. |
| Drain sees the whole frame | Yes — every producer enqueues from `Update`; Unity completes all `Update` before any `LateUpdate`. |
| Allocation-light | Yes — three reused list fields, no per-event allocation; task 002 criterion. |
| Budget + fallback exists | Yes — selection policy is the budget; overflow drops redundant copies before novel kinds. |
| Priority/distance culling for impact spam (`performance.md` §*Audio*) | Yes — per-event `AudibleRadius` cull plus faction-derived priority, both in task 002. |
| Same-clip duplicate culling (`performance.md` §*Audio*) | Yes — `maxCopiesPerClipPerFrame` within a frame, `sameSoundStartSpacingSeconds` across frames. |
| `audio requests culled` counter exposed | Yes — task 002 splits culls into distance / redundant / novel and publishes all three. |
| Serialized reference, no scene scan | Yes — task 003 removes `FindAnyObjectByType`. |
| Presentation does not mutate sim state | Yes — nothing reads back from audio. |
| No native handle to own | Yes, by construction — no native container is created, so §*Native And ECS Handle Ownership* has nothing to bind. |

## Minimal/Additive vs. Refactor Comparison

The relevant comparison is no longer "keep `AudioManager` vs. move it" — it is
**"build the ECS lane now vs. build the managed drain now"**.

**Additive approach** (rejected): keep the previous revision's
`SoundEventSingleton` + `SoundEventBridge` alongside the managed list.

- resulting data flow: managed producers → `List`; Burst producers → `NativeQueue`
  → bridge merges both in `PresentationSystemGroup` → `AudioRoot`.
- new concepts/types introduced: a lane singleton, a `SystemBase`, a
  `ProducerHandle` rendezvous, a native container lifetime.
- copies/translations added: one drain-and-merge of a container that carries zero
  elements until a Burst producer exists.
- long-term cost: four types must be kept correct — allocation, disposal on every
  teardown path including partial construction, handle completion, clear-on-early-
  out — with no test that can exercise the native half, because nothing writes it.
  Every one of those is a real failure mode (a missed clear replays stale sound;
  a missed dispose leaks) bought for zero present benefit.

**Deferred approach** (chosen): managed list + `LateUpdate` drain only.

- resulting data flow: producers → `AudioRoot.Enqueue` → `LateUpdate` drain →
  cull, rank, play. One path, one merge point, one type doing the work.
- existing concepts/types changed or removed: `AudioManager` deleted;
  `SkillDriver.cs:32`/`:72` dead wiring replaced rather than carried forward.
- copies/translations removed or avoided: no native container, no handle
  rendezvous, no merge of an empty queue, no ECS system lifetime.
- long-term benefit: when a Burst producer appears, the lane is added *then*,
  against a real writer and a test that can observe it. `SoundEvent` is already
  unmanaged and `Enqueue` is already the merge point, so the addition touches no
  existing code.

**Decision: defer the lane.** Reason: the lane's stages solve a pressure — parallel
job writes needing an explicit rendezvous — that this domain does not yet have.
Copying stage count instead of concepts buys four lifetime obligations and zero
behavior. The requirement that actually drives this work, *kinds over copies*, is
served by the batch drain, and the batch drain is independent of where the batch
came from.

## Default Decision Rule Applied

No two representations of the same concept survive this plan. The previous
revision deliberately kept a managed `List<SoundEvent>` and a native
`NativeQueue<SoundEvent>` as separate carriers of "a pending sound event",
justified by their different safety contracts. With the native half removed there
is one carrier, one element type, and one merge point, so the rule is satisfied
outright rather than by exception.

The exception returns — with the same justification recorded in
`.agent/burst-onupdate-work/index.md` §*Why the dual event path stays* — if and
when a Burst producer needs the lane. That is the correct time to accept it.

## Task List

Sequential — each task depends on the one before it, except 004.

| # | Task | Scope |
|---|---|---|
| [001](./001-audio-root.md) | Delete `AudioManager`; add `AudioRoot` — pool, registry, `SoundEvent` | Medium |
| [002](./002-drain-and-selection.md) | `AudioRoot` drain: spatial cull, ranking, playback, counters | **Largest** |
| [003](./003-skill-cast-producer.md) | Cast clip authoring → id → enqueue; `SkillDriver` wiring fix | Medium |
| [004](./004-docs.md) | Contract doc + layer/folder doc updates | Small |

**Dependency notes.** 002 is the only task with real design content; 001 is the
vessel it needs. 004 may land any time after 002 fixes the shape.

## Open Questions

None blocking.

- *Where does the listener position come from?* — Resolved. `AudioRoot`
  serializes a `GameObject listenerObject` and reads its transform. **Nothing is
  camera-specific**: player, camera, or a dedicated marker are all valid, and
  `AudioRoot` never inspects which. No `Camera.main` fallback —
  `coding-standards.md` §*Unity Object Access* prefers serialized references and
  §*Fail Fast Validation* bans hiding bad setup behind convenience resolution.
  `BindListener(GameObject)` handles runtime rebinding, following the existing
  `SkillDriver.BindCombatRoot` / `BindVfxRoot` idiom.

  A radius cull rather than a view rectangle: for a top-down game the two differ
  only at the corners, and the rectangle costs rotation handling and a per-frame
  camera-size query for that. Making the listener a plain object rather than a
  camera is what lets the radius stand on its own — it is a distance from a
  point, not an approximation of a frustum.

  The camera-vs-player question the previous revision left open is not a design
  decision at all under this shape; it is a field assignment. The one binding
  rule is Decision 6's: whatever object you assign must carry the scene's
  `AudioListener`, validated at setup and on rebind.
- *Fail loud or drop silently when audio is unavailable?* — Split.
  **Wiring** is validated fail-fast at setup (task 003 validates the serialized
  reference). **Individual events** drop into a counter (task 002). A missing
  sound must never throw mid-combat. This is deliberately *not* the fail-loud
  singleton rule that governs sim-critical combat singletons — audio is not
  sim-critical and nothing reads back from it.
- *Reuse `AudioManager` instead of deleting it?* — No. Zero callers means zero
  migration cost, and its per-call greedy allocator is the exact decision point
  this plan moves into the batch drain. Recorded under Decision 1.

## Testing

Per `Docs/project-overview.md`, agents do not run tests. There is currently **no
audio test coverage at all**. Tasks 001 and 002 add EditMode tests for the
registry and the selection policy; both are pure functions over data and need no
scene.

All coverage this plan adds is **EditMode**. No PlayMode test is introduced —
nothing here needs a running scene, and naming a PlayMode run with nothing behind
it would just be ceremony.

Tests to run after all tasks land:

| Platform | Test class | Added by |
|---|---|---|
| EditMode | `AudioClipRegistryEditModeTests` | [001](./001-audio-root.md) |
| EditMode | `AudioRootSelectionEditModeTests` | [002](./002-drain-and-selection.md) |
| EditMode | `SkillCastSoundEditModeTests` | [003](./003-skill-cast-producer.md) |
| EditMode | `ProjectileContinuousAuthoringEditModeTests` | pre-existing — regression guard for "no audio root is silent, not fatal" |

Export results to `Logs/TestResults-EditMode-SkillSoundEvents.xml` and report only
against that XML.

## Editor Work (User)

Agents do not edit Unity YAML. These steps are yours:

1. **After task 001**, `Assets/Scenes/BenchmarkLarge.unity` will hold a missing
   script where `AudioManager` was (GUID `944e3f8cd2ab47d3a41aa6f16f7d3a21`).
   Remove that component and add `AudioRoot` to the same GameObject.
   `Assets/_Recovery/0.unity` references the same GUID; leave it or clean it up,
   it is not in the build.
2. **After task 003**, assign the `AudioRoot` reference on each `SkillDriver`
   (player and mob prefabs), and assign `spawnSound` on the **prefab** of each
   skill you want audible — `BasicAoePrefab`, `LingeringAoePrefab`,
   `TargetedPrefab`, or `BasicAttackPrefab` — beside the `spawnEffect` slot you
   already fill in. Not on the `Skill` asset. Set `spawnSoundRadius` only where
   the default reach is wrong. Prefabs with no clip stay silent by design.
3. Assign `listenerObject` on the `AudioRoot` — whichever object carries the
   scene's `AudioListener`. Leaving it empty is a setup error and the game runs
   silent by design, so this is not optional.
4. Place an `AudioRoot` in any scene under test.
