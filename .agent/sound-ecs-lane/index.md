# Sound For Every Spawn: The ECS Lane

## Summary

Follow-up to `.agent/skill-sound-events/`, which shipped complete. That plan built
`AudioRoot` — registry, frame-batched ranking, spatial cull, jitter, pooled
voices, counters — and wired exactly one producer: a managed cast enqueue in
`SkillDriver.Tick` (`SkillDriver.cs:135-148`).

This plan adds the ECS lane the shipped plan deferred, moves emission from the
cast site into the **spawn expansion systems**, and thereby makes every spawn
audible rather than only root casts.

Shipped Decision 3 predicted this shape exactly:

> **When a Burst producer does land**, the addition is a `NativeQueue<SoundEvent>`
> lane plus a small system that drains it into `AudioRoot.Enqueue` before
> `LateUpdate`. Nothing built here changes: `SoundEvent` is already unmanaged, the
> clip is already an `int` id, and `AudioRoot.Enqueue` is already the single merge
> point.

That prediction holds. `AudioRoot` needs no change beyond one added drain source.

## Why now

The shipped implementation emits only where `SkillDriver.Tick` fires — root cast
slots. Everything else is silent:

| Silent today | Spawned by |
|---|---|
| Trigger-linked skills | not a root; `SkillLoadoutCompiler.cs:20-23` excludes nodes with `hasIncomingTrigger` |
| Interval children | `TimedSpawnSystem` |
| On-hit spawns | ECS collision paths |
| Stacking detonations | ECS |

That produces a clip authored on a prefab that plays when the prefab is your
equipped skill and is silent when the **identical prefab** is an interval child
of something else — a distinction invisible from the inspector.

It is also a half-copy of VFX, which the shipped plan chose to mirror. VFX
already emits on nested spawns: `AoeSpawnExpansionSystem.cs:104` emits from
`spawned.VfxIds`, and `SkillIntervalTemplateBuilder.BuildAoeTemplate(child, …)`
stamps each child's own ids. Sound copied VFX's authoring but not its coverage.

## Rationale

### Decision 1 — the lane is now justified, on its own terms

The shipped plan refused the lane because no Burst producer existed and the
native container would have carried zero events. Emitting at expansion creates
that producer: spawn expansion runs as Burst jobs writing in parallel, which is
precisely the pressure the canonical lane shape in `Docs/coding-standards.md`
§*System Encapsulation* exists to solve — parallel writes needing an explicit
`JobHandle` rendezvous before a sink reads.

This is not a reversal of the earlier reasoning; it is that reasoning reaching
its stated trigger.

### Decision 2 — the managed cast enqueue is **removed**, not kept alongside

`SkillDriver.cs:135-148` must go. Root casts reach expansion through
`ExternalSpawnGateSystem` like every other spawn, so leaving the managed enqueue
in place would sound every root cast **twice** — once at the cast site, once at
expansion.

Removing it also fixes two things for free:

- **Mana-rejected casts become silent.** `ExternalSpawnGateSystem` rejects on
  `SpawnRejectionReason.InsufficientMana` before expansion, so no event is
  produced. The shipped version emits at the cast site and accepts sounding a
  cast that `ReceiveSpawnRejected` (`SkillDriver.cs:158`) later refunds.
- **Priority stops being caster-local.** The shipped emit derives priority from
  `SkillDriver`'s serialized `faction` (`SkillDriver.cs:146`), which nested
  spawns never see. `CombatFaction` is on every spawn command
  (`AoeSpawnPipeline.cs:59`), so deriving it in the emit helper covers everything.

`SkillDriver` keeps its `audioRoot` reference — `RegisterSounds` still resolves
clip ids at compile time. Only the `Tick` emission leaves.

**`AudioRoot.Enqueue` stays public.** It loses its only caller here, but UI,
death, and hurt sounds are managed and will want it, and it is the merge point
the dispatch system writes through. Nothing about it changes.

### Decision 3 — the dispatch system transports; `AudioRoot` still decides

`SoundEventDispatchSystem` completes the `ProducerHandle`, drains the queue, and
pushes each event through `AudioRoot.Enqueue`. It culls nothing and ranks
nothing.

This preserves the ownership rule the shipped plan settled on — the manager owns
processing — and matches `CombatAoeVfxDispatchSystem`, which drains its queues
and calls `CombatVfxRoot.DrainAndDispatch` rather than deciding what to render.

It also means the whole frame still ranks as one batch: ECS events land in the
same `pending` list the managed path uses, and `AudioRoot.LateUpdate`
(`AudioRoot.cs:128`) ranks them together. **One ranking per frame, not two.**

### Decision 4 — ids ride the spawn command, exactly as `VfxIds` does

`SkillSoundIds` already exists (`SoundEvent.cs:25-28`) and is already resolved
per definition. What is missing is the path from a runtime definition to the ECS
emit site, which VFX solves with two stages this plan copies:

| Stage | VFX | File |
|---|---|---|
| Registry holds ids per type | `SetVfxIds(typeId, vfxIds)` | `AoeTypeRegistry.cs:53` |
| Spawn command carries them | `public AoeVfxIds VfxIds;` | `AoeSpawnPipeline.cs:62` |

Templates are registered once via `RegisterSpawnTemplate` /
`RegisterTimedSpawnTemplate` and reused, so ids ride along at no per-spawn cost —
the same property that makes `VfxIds` free.

Registration must also become **recursive**. The shipped pass is root-only by
design (`implementation-log.md`: "non-recursive root-slot sound registration");
every definition that can spawn now needs its own id, following
`RegisterAoeTypesRecursive` (`SkillDriver.cs:1063-1117`) and covering the same
edges.

## Constraints & Invariants

- **Sim references no PlayGround assembly.** Source: `layer-rules.md` §*Package
  Boundary*. → All new files land in `Assets/Scripts/System/`; no asmdef change.
- **Jobs must not read managed values.** Source: `layer-rules.md` §*Managed And
  Unmanaged Data*. → `SoundEvent` and `SkillSoundIds` are already unmanaged;
  only `int` ids and a `float` radius cross into jobs.
- **Lane singletons own their containers and expose explicit `JobHandle` fields;
  producers combine into `ProducerHandle` on the main thread, sinks complete
  before draining.** Source: `coding-standards.md` §*System Encapsulation*. →
  `SoundEventSingleton` follows the canonical shape.
- **Systems must not reach into other systems' collections.** Same source. →
  Emitters take the lane's `ParallelWriter` as a job field, as `VfxEmit` does.
- **`NativeQueue` contents are unordered under parallel writes.** Source:
  `coding-standards.md` §*Why NativeQueue for These Sinks*. → Ordering comes only
  from `AudioRoot.RankPending`, which already sorts explicitly.
- **Any code creating a native handle owns disposal on every teardown path.**
  Source: `coding-standards.md` §*Native And ECS Handle Ownership*.
- **Runtime data copied from authoring before simulation.** Source:
  `layer-rules.md` §*Runtime, Authoring, And Configuration*. → Ids still resolve
  in `CompileAndRegister`, never at spawn time.
- **Combat paths are allocation-light.** Source: `coding-standards.md`
  §*Allocation Rule*. → The dispatch drain uses a field-held scratch.
- **Presentation must not mutate simulation state except clearing buffers owned
  by that phase.** Source: `Docs/layers/presentation-and-feedback.md`. → The
  dispatch system clears only the sound lane.

## Mechanisms Reused vs. Introduced

**Reused:** lane singleton + `ProducerHandle` + drain-and-clear
(`CombatApplyResultSingleton` / `CombatApplyBridge`,
`CombatAoeVfxDispatchSingleton`); ECS system draining into a root `MonoBehaviour`
(`CombatAoeVfxDispatchSystem` → `CombatVfxRoot`); static emit helper guarding
`id <= 0` (`VfxEmit.cs:15`, `:37`); per-type id registry
(`AoeTypeRegistry.cs:53`); ids on the spawn command (`AoeSpawnPipeline.cs:62`);
recursive registration walk (`SkillDriver.cs:1063-1117`); all of `AudioRoot`.

**Introduced:** `SoundEventSingleton`, `SoundEmit`, `SoundEventDispatchSystem`.
Three types, all close copies of named existing files. `AudioRoot`, `SoundEvent`,
`SoundCategory`, and `SkillSoundIds` are unchanged.

## Design Validation

| Invariant | Held? |
|---|---|
| Sim references no PlayGround assembly | Yes — new files are in Sim; no asmdef change. |
| Jobs never read managed objects | Yes — only `int`/`float`/enum cross. |
| Lane owns container + explicit handle | Yes — mirrors `CombatApplyResultSingleton`. |
| `NativeQueue` order not relied upon | Yes — `RankPending` already sorts. |
| Native handle disposal on every path | Task 001 criterion, including partial create. |
| One ranking per frame | Yes — the lane feeds `AudioRoot.Enqueue`, so ECS and managed events share one `pending` batch. |
| No double sound on root casts | Yes — task 003 removes `SkillDriver.cs:135-148`. |
| Allocation-light | Yes — field-held drain scratch. |
| Counters still meaningful | Yes — `AudioRoot` counters are unchanged and now cover nested spawns. |
| Sound coverage matches VFX coverage | Yes — emits at the same sites. |

## Minimal/Additive vs. Refactor Comparison

**Minimal/additive** (rejected): keep the cast-site enqueue and add a second
managed emit for nested spawns from some main-thread bridge.

- resulting data flow: two managed emit paths, neither of which can see an
  interval child — `TimedSpawnSystem` runs in Burst and has no main-thread
  replay point equivalent to `CombatApplyBridge`.
- new concepts/types: none, but the coverage gap simply cannot close.
- long-term cost: sound permanently diverges from VFX coverage, and the prefab
  slot keeps lying about when it plays.

**Refactor** (chosen): add the lane, move emission to expansion, delete the
cast-site enqueue.

- resulting data flow: expansion jobs → lane → dispatch → `AudioRoot.Enqueue` →
  one ranking in `LateUpdate` → pooled voices.
- existing concepts/types changed: `SkillDriver.Tick` loses its emit; three spawn
  command structs gain an ids field; registration becomes recursive.
- copies/translations avoided: no second emit path, no main-thread mirror of
  spawn expansion.
- long-term benefit: adding hit or death sounds is a prefab slot plus an emit
  call at a site that already emits VFX.

**Decision: refactor.** The additive version cannot express the requirement at
all — the emitters live in Burst, and no managed vantage point sees them.

## Default Decision Rule Applied

The managed `List<SoundEvent>` and the native `NativeQueue<SoundEvent>` now both
describe "a pending sound event". They stay separate for the reason recorded in
`.agent/burst-onupdate-work/index.md` §*Why the dual event path stays*: different
safety contracts, main-thread managed write versus parallel job write. They share
one element type and merge at one point (`AudioRoot.Enqueue`), so there is still
a single source of truth for what a sound event is, and a single ranking.

## Task List

Sequential, except 004.

| # | Task | Scope |
|---|---|---|
| [001](./001-sound-event-lane.md) | `SoundEventSingleton`, `SoundEmit`, `SoundEventDispatchSystem` | Small |
| [002](./002-ids-to-spawn-commands.md) | Registry + spawn command ids + recursive registration | Medium |
| [003](./003-emit-at-expansion.md) | Emit at expansion and arming; remove the cast-site enqueue | Medium |
| [004](./004-docs.md) | Update the shipped contract doc | Small |

## Open Questions

**One, and it needs a Unity run to settle — agents do not run Unity.**

`SoundEventDispatchSystem` runs in `PresentationSystemGroup`; `AudioRoot` drains
in `LateUpdate` (`AudioRoot.cs:128`). Both live in the player loop's
`PreLateUpdate` phase, and which runs first determines whether ECS events are
ranked in the frame they were produced or the next one.

- **If `PresentationSystemGroup` runs before `ScriptRunBehaviourLateUpdate`:**
  nothing to do. Events land in `pending` and rank the same frame.
- **If it runs after:** ECS events rank one frame late (~16 ms — inaudible), but
  they rank *alongside the next frame's* events, which slightly skews the
  per-clip frame cap. Acceptable, or fixed by having `AudioRoot.LateUpdate` pull
  the lane itself instead of being pushed.

Task 001 states both branches and the decision rule. Confirm the actual order in
the profiler before choosing; do not guess.

## Testing

All coverage remains **EditMode**; the new work is ECS wiring, which EditMode
cannot exercise meaningfully.

| Platform | Test class | Status |
|---|---|---|
| EditMode | `AudioClipRegistryEditModeTests` | shipped — must keep passing |
| EditMode | `AudioRootSelectionEditModeTests` | shipped — must keep passing |
| EditMode | `SkillCastSoundEditModeTests` | shipped — **task 003 changes its subject**; it asserts the cast-site enqueue that is being removed |
| EditMode | `SkillSoundRecursiveRegistrationEditModeTests` | new, task 002 |

Emit coverage itself is verified by playing with the debug counters visible —
`accepted` rising on interval spawns is the observable signal.

Export results to `Logs/TestResults-EditMode-SoundEcsLane.xml` and report only
against that XML.

## Editor Work (User)

No new scene or prefab work. `spawnSound` slots already exist on all four prefab
classes from the shipped plan; nested spawns become audible using the clips
already assigned.
