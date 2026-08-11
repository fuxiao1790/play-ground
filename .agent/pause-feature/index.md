# Pause Feature

## Summary

Add a pause state to the game: `Time.timeScale = 0` as the single clock
authority, gameplay input blocked while paused, a pause overlay with menu
content, and the correctness fixes that make a zero-length frame actually
inert.

The project is unusually well-placed for this. Every combat simulation system
reads `SystemAPI.Time` (13 sites, no exceptions), and the ECS world is the
default world appended to the player loop
(`Assets/Scripts/System/Platform/CombatEcsWorld.cs:25-32`), so its clock comes
from `UpdateWorldTimeSystem` = `UnityEngine.Time.deltaTime` clamped by
`World.MaximumDeltaTime`. Setting `timeScale = 0` freezes `DeltaTime` and
`ElapsedTime` together — the dt-integrating systems and the absolute-timestamp
systems (`StatusProcessSystem`, `CombatApplyFinalizeSingleSystem`) stay
consistent with each other. Physics2D stops because `FixedUpdate` stops.
Animators freeze (no `m_UpdateMode` override anywhere). VFX Graph freezes in
place (all 10 `.vfx` assets are `m_UpdateMode: 0`, so no
`VFXUpdateMode.IgnoreTimeScale`).

What does **not** follow from the clock is the actual work of this plan:
MonoBehaviour `Update` keeps running, and code that acts without consulting dt
keeps acting.

## Correctness Gaps This Plan Closes

1. **Skill casts leak on the pause frame.** `SkillDriver.Tick`
   (`Assets/Scripts/Skills/SkillDriver.cs:92-126`) ticks cooldowns by
   `Time.deltaTime` but gates firing only on `fireHeld && IsReady`. Any slot
   already ready when pause begins fires immediately — for the player, and for
   every mob, since `MobRoot.cs:505` passes `fireHeld: true` unconditionally.
   The spawn lands in the scope buffer (`CombatRoot.cs:259`) and is applied the
   same frame at dt 0: a visible burst at the instant of pause.
2. **Mob spawner can drain a backlog.** `ContinuousStreamBehaviour.cs:43-48`
   grows its accumulator by `dt` but the `while (accumulator >= 1f)` drain runs
   regardless, up to `maxSpawnsPerTick` per frame, every frame, whenever the
   accumulator is saturated.
3. **Aura VFX emits once.** `PlayerVfxAura.cs:57-64` — `Time.time` is frozen so
   it self-gates after one emit, but that one emit still happens.
4. **Click-to-fire is not blocked.** UI Toolkit pointer events and the Input
   System both run on real time. Without an explicit block, a click on the pause
   menu can reach the world surface, and a pointer held across the pause leaves
   `fireHeld` true so the player fires the instant they resume.
5. **Two independent input suspenders collide.** `gameplayInputSuspended` is a
   single bool (`PlayerRoot.cs:65`) toggled by `SkillLoadoutUi.cs:313/355`.
   Since the loadout stays usable while paused, closing the picker during pause
   would clear the suspension while the game is still frozen.

## Architectural Decisions

**D1 — `Time.timeScale` is the only pause clock.** No second clock, no
per-system pause flag, no pause-aware time singleton. The ECS world already
derives from it.

**D2 — Drivers become dt-honest, not pause-aware.** The three leaking sites
gain a `dt <= 0` guard instead of a pause-flag check. No pause state is injected
into any driver. This matters most for `MobRoot`: mobs are pooled and created at
runtime (`SpawnController.cs:98`), so the only way to hand them a pause
reference is `SpawnController.WireMob` — a second bind path that would have to
stay in sync, including seeding the correct state for a mob spawned while
paused. Respecting dt is something every driver must do anyway; pause then falls
out of it for free, and slow-motion (`timeScale = 0.1`) works without further
change.

**D3 — Never disable driver components to pause.** `MobRoot.OnDisable` deletes
the combat target proxy and unregisters (`MobRoot.cs:155-159`), and
`PlayerRoot.OnDisable` clears its registries (`PlayerRoot.cs:180-184`).
Disabling drivers to freeze them would destroy every combat proxy in the scene.

**D4 — Input blocking is two layers, matching the documented model.**
`Docs/ui.md:85-112` already defines the layering: `GameplayInputSurface` is the
only click-to-fire source and UI Toolkit picking decides whether a click reaches
it. So pause sets `pickingMode = Ignore` on the world surface and clears
`fireHeld` (hygiene: no click-through, no stuck capture), while `PlayerRoot`
gates its own action reads (authority: move, dash, aim, fire).

**D5 — `gameplayInputSuspended` becomes a reason mask.** Two concurrent
suspenders exist, so a bool cannot express the state. This is a fix to the
existing mechanism, not a parallel one.

**D6 — Pause state is an authority with no outgoing dependencies.**
`PauseController` owns `IsPaused`, `Time.timeScale`, and `AudioListener.pause`,
and raises `PausedChanged`. `PlayerRoot`, `GameplayInputSurface`, and
`PauseMenuUi` each subscribe and handle their own state. This keeps the
assembly direction legal in both directions of knowledge: `PauseController`
lives in Game Logic and must never reference the UI types that observe it.

**D7 — Esc comes from the existing `UI/Cancel` action.** The
`.inputactions` asset already defines `UI/Cancel`, and `PlayerRoot.cs:136`
already reaches into the UI map for `UI/Point` and enables that single action.
Same pattern, no asset edit.

## Constraints And Invariants

| # | Invariant | Source |
|---|---|---|
| I1 | `PlayGround.Ui -> PlayGround.GameLogic -> PlayGround.Sim`, one way. Game Logic and Sim must not reference UI types. | `Docs/architecture/layer-rules.md:33-42`, `Docs/ui.md:19-24` |
| I2 | ECS world time is `UnityEngine.Time.deltaTime`; every sim system reads `SystemAPI.Time`. | `Library/PackageCache/com.unity.entities@.../UpdateWorldTimeSystem.cs:35-43`; 13 call sites under `Assets/Scripts/System/` |
| I3 | `OnDisable` on actor roots performs combat proxy teardown. | `MobRoot.cs:155-159`, `PlayerRoot.cs:180-184` |
| I4 | Queued loadout edits resolve only inside `SkillDriver.Tick`, via `ProcessPendingEdit` before any cooldown or fire work. | `SkillDriver.cs:86`, `:349`, `:1201-1225` |
| I5 | `GameplayInputSurface` is the only mouse click-to-fire source; picking decides world pass-through; pointer capture holds a click until release or capture loss. | `Docs/ui.md:85-112`, `GameplayInputSurface.cs:82-100` |
| I6 | Editor wiring is a user operation. No hand-edited scene, prefab, asset, or meta YAML. | `Docs/ui.md:152-153` |
| I7 | `Awake` sets up self-owned state and validates required refs fail-fast; cross-MonoBehaviour work belongs in `OnEnable`/`Start`. | `Docs/coding-standards.md:45-58`, `:102-120` |
| I8 | `OnDestroy`/teardown may only touch state the component itself installed. | `Docs/coding-standards.md:60-100` |
| I9 | Root components coordinate and expose API; gameplay logic lives in focused classes. | `Docs/coding-standards.md:6-43` |
| I10 | UI is a projection: it reads owner state and sends commands, never duplicating gameplay state. | `Docs/ui.md:67-80` |
| I11 | Root UXML stays small; repeated or dynamic pieces are separate templates cloned by C#. | `Docs/ui.md:52-60` |

## Mechanisms Reused vs Introduced

**Reused**

- `Time.timeScale` as the clock for the whole hybrid runtime (I2).
- The existing input-suspension concept on `PlayerRoot` — widened from a bool to
  a reason mask rather than joined by a second flag (D5).
- UI Toolkit picking as the world click-through gate, exactly as `Docs/ui.md`
  describes it (D4, I5).
- The `UI/Point`-style single-action enable pattern for `UI/Cancel` (D7).
- The template-cloned-into-the-HUD-root pattern the skill UI already uses, for
  the pause overlay (I11).

**Introduced**

- `PauseController` — one new MonoBehaviour. Justified: pause state has to live
  somewhere with a clear owner, and stuffing it into `GameRoot` would violate
  I9. It holds no gameplay logic beyond the toggle.
- `GameplayInputBlock` `[Flags]` enum — replaces a bool that can no longer
  express the state. Not a new concept, a correctly typed old one.
- `PauseMenuUi` + UXML/USS — required by the overlay-with-menu-content scope.

Nothing new is introduced on the ECS side, and no driver gains a new dependency.

## Design Validation

- **I1** — `PauseController` and `GameplayInputBlock` live in Game Logic;
  `PauseMenuUi` and `GameplayInputSurface` live in `PlayGround.Ui` and subscribe
  downward. `PauseController` holds no reference to any UI type, which is why
  D6 uses an event rather than direct calls.
- **I2** — Setting `timeScale` is the entire ECS-side change. No system is
  modified, no singleton added, so the "all sim time flows through
  `SystemAPI.Time`" property is preserved by construction.
- **I3** — D3 forbids the component-disable approach outright. Every driver
  keeps running; only its dt-dependent work stops.
- **I4** — The `SkillDriver` guard is placed *after* `ProcessPendingEdit()` and
  before the cooldown loop. Loadout edits keep resolving while paused, as
  required. This is the single most misplaceable line in the plan; task 002
  states it as an acceptance criterion.
- **I5** — Blocking is done by flipping the surface's own `pickingMode` and
  clearing its own `fireHeld`, inside `GameplayInputSurface`. No other component
  reaches into its pointer state, and the overlay does not have to win a sibling
  ordering race to be safe.
- **I6** — All scene and inspector work is isolated in task 005 as user
  instructions.
- **I7** — `PauseController` validates its `InputActionAsset` in `Awake` and
  throws; subscribers wire up in `OnEnable`.
- **I8** — `PauseController` restores `Time.timeScale` and `AudioListener.pause`
  in `OnDisable`. Both are global state it installed itself, so this is inside
  the boundary, and it prevents a disabled controller from leaving the game
  frozen or silent.
- **I9** — `GameRoot` is untouched. `PauseController` is a focused sibling
  component.
- **I10** — `PauseMenuUi` renders `IsPaused` and calls `SetPaused(false)`; it
  stores no pause state of its own.
- **I11** — `PauseMenuUi.uxml` is a new template cloned into the existing HUD
  root, not an addition to `SkillLoadoutUi.uxml`.

## Minimal/Additive vs Refactor Comparison

**Minimal/additive approach** — `IPausable` interface plus a registry on
`PauseController`; every driver implements it and is told when pause changes.

- Resulting data flow: pause state fans out from one owner to N drivers; pooled
  mobs additionally need the state pushed at bind time in
  `SpawnController.WireMob`, alongside `BindCombatRoot`/`BindVfxRoot`.
- New concepts/types: `IPausable`, a registration/deregistration lifecycle tied
  to mob pooling, a per-driver `SetPaused`, and a bind-time state seed.
- Copies/translations added: the pause bool is duplicated into every driver and
  must be kept coherent with `Time.timeScale`.
- Long-term cost: every new driver must remember to register or it silently
  leaks work during pause; registration lifetime is coupled to `MobPool`
  rent/return, which is exactly the kind of parallel bookkeeping the pooling
  code already got wrong once.

**Refactor approach** — one clock authority, dt-honest drivers, and the
existing suspension bool widened to a reason mask.

- Resulting data flow: `Time.timeScale` → `Time.deltaTime` → drivers. No
  fan-out, no registry, no per-driver state.
- Existing concepts changed: `SkillDriver.Tick`,
  `ContinuousStreamBehaviour.Runtime.Tick`, and `PlayerVfxAura.Update` gain a
  `dt <= 0` guard; `PlayerRoot.gameplayInputSuspended` becomes
  `GameplayInputBlock`.
- Copies/translations removed or avoided: no pause state stored anywhere except
  `PauseController`; `SpawnController.WireMob` is untouched.
- Long-term benefit: a driver that respects dt is pause-correct by default;
  slow-motion works with no extra code; there is one answer to "is the game
  frozen" and it is `Time.timeScale`.

**Decision: refactor.**

Reason: the additive version creates a second data path into pooled mobs and
duplicates ownership of a fact `Time.timeScale` already owns. Both are listed
structural warnings. The refactor changes three call sites and one field type,
and leaves no parallel system to keep in sync.

## Default Decision Rule

If two representations of the same domain concept appear, collapse to one source
of truth unless there is a concrete migration reason not to. Applied here:
"is the game frozen" has exactly one representation (`Time.timeScale`, surfaced
as `PauseController.IsPaused`), and "is gameplay input blocked" has exactly one
(`GameplayInputBlock` on `PlayerRoot`), with pause as one of its reasons rather
than a competing flag.

## Tasks

| # | Task | Depends on |
|---|---|---|
| 001 | [Pause controller and clock authority](./001-pause-controller.md) | — |
| 002 | [Zero-dt driver guards](./002-zero-dt-driver-guards.md) | — |
| 003 | [Gameplay input block reasons](./003-input-block-reasons.md) | 001 |
| 004 | [Pause menu overlay UI](./004-pause-menu-ui.md) | 001 |
| 005 | [Editor wiring (user steps)](./005-editor-wiring.md) | 001, 003, 004 |
| 006 | [Pause PlayMode tests](./006-pause-playmode-tests.md) | 001-005 |

002 is independent and can land first; it is the correctness core and is
verifiable on its own. 001 is the foundation for everything else.

## Explicitly Out Of Scope

**ECS CPU cost while paused.** `timeScale = 0` freezes state, not work: every
system in `SimulationSystemGroup` still runs each frame with dt 0, iterating
full queries and scheduling jobs. Deferred by decision — correctness first. The
design leaves the door open: an early return in each `OnUpdate`, or disabling
`SimulationSystemGroup` from a Game Logic call into a Sim-side API, both remain
available later. Note for whoever picks that up: MonoBehaviour `Update` runs
*before* `SimulationSystemGroup` in the player loop, so anything the drivers
pushed into the spawn-event buffers that frame would sit queued and burst on
resume — which is safe only because task 002 stops the drivers producing during
pause. The two changes are coupled in that order.

## Open Questions And Considerations

- **Esc while the skill picker is open.** Today Esc does nothing anywhere —
  `Docs/ui.md:192` lists "no Escape key, backdrop cancel" as a known gap. This
  plan makes Esc always toggle pause. If picker-close-on-Esc is added later, one
  of the two has to win; decide then, and note that pause-while-picker-open is
  a supported state by design.
- **`AudioListener.pause` silences everything, including UI.** If the pause menu
  later wants click sounds, they need a source that bypasses the listener pause
  or the listener pause has to go. Flagged, not solved — the current menu is a
  Resume button with no audio.
- **Autosave keeps running while paused.** `PlayerSaveController.cs:83` uses
  `Time.unscaledTime` deliberately. Left as-is; assumed desirable.
- **VFX dispatched on the pause frame lands on resume.** Spawn contexts drain
  queued `SendEvent`s on the effect's next update, which never comes at dt 0
  (`CombatAoeVfxDispatcher.cs:231-236`). Cosmetic, and task 002 stops new
  dispatches from accumulating during pause. Worth an eyeball check during the
  task 006 manual pass.
