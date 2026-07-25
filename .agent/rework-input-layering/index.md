# Rework Input Handling: Layered Click-To-Fire

## High-Level Summary

Split pointer input into two layers inside the existing `GameUI` UI Toolkit panel:

- **Bottom layer — world click surface.** A full-screen `VisualElement` inserted
  behind `#bar`. It listens for `PointerDownEvent`/`PointerUpEvent`, captures the
  pointer while held, and exposes a `FireHeld` bool. This is the only "click to
  fire" source.
- **Top layer — UI elements.** Skill buttons and the picker are normal
  `picking-mode: Position` elements, so they consume their own clicks and the
  surface never sees them. An element that wants a click to fall through to the
  world sets `picking-mode: ignore`. This *is* "UI decides whether a click
  propagates to the bottom layer" — expressed structurally through UI Toolkit
  picking, not a boolean gate.

`PlayerRoot` stops reading the mouse `Attack` button. It consumes fire through a
new `IGameplayInputSource` interface (owned by the Game Logic layer) that the
surface (UI layer) registers into. The two ad-hoc gate booleans
(`gameplayInputBlocked`, `pointerOverSkillUi`) collapse into a single
modal-suspend flag; the pointer-over-UI concern disappears because the layering
handles it.

Scope decision from the user: **mouse-only fire** (no gamepad/controller support
exists yet). The `Attack` action's non-mouse bindings simply go unread; the
`.inputactions` asset is not edited.

## Rationale

- The current gate is fake-layered: `pointerOverSkillUi` is only set while the
  picker modal is open ([SkillLoadoutUi.cs:228](../../Assets/Scripts/SkillUi/SkillLoadoutUi.cs#L228)),
  never on hover over `#bar`. So every click on a bar button also fires the skill
  ([PlayerRoot.cs:330](../../Assets/Scripts/Player/PlayerRoot.cs#L330)), which
  puts the skill on cooldown, which makes any cooldown-gated edit
  (`IsCooldownBlocked`, [SkillDriver.cs:327](../../Assets/Scripts/Skills/SkillDriver.cs#L327))
  effectively always rejected. Both reported symptoms trace to the same root
  cause: fire and UI share one raw mouse-button read with no real layering.
- UI Toolkit already has a first-class layering/propagation mechanism
  (z-order + `picking-mode` + pointer capture). Reusing it removes the need to
  maintain any "is pointer over UI" bookkeeping in gameplay code.

## Constraints & Invariants The Change Must Respect

1. **One-way assembly references** — `PlayGround.SkillUi → PlayGround.GameLogic →
   PlayGround.Sim` (Docs/architecture/layer-rules.md "Package Boundary";
   confirmed in the three `.asmdef` files). The UI layer may reference and push
   into Game Logic; Game Logic must not reference the UI layer. → The
   `IGameplayInputSource` interface lives in Game Logic next to `PlayerRoot`; the
   concrete surface lives in the UI layer and registers itself into `PlayerRoot`,
   mirroring the existing `SetGameplayInputGate` push direction.
2. **Awake vs OnEnable boundary** — cross-MonoBehaviour wiring (registering with
   another component, subscribing to events, touching `UIDocument.rootVisualElement`)
   belongs in `OnEnable`/`Start`, not `Awake` (Docs/coding-standards.md "Awake vs
   OnEnable Boundary"). Unity runs all `OnEnable`s before any `Start`, so the
   surface can register in `OnEnable` and `PlayerRoot.Start` can observe it.
3. **Input sampling in `Update`** — fire is read once per frame in `Update`
   (Docs/coding-standards.md "Update Timing"). `PlayerRoot.Update` already does
   this; it keeps doing so, just from a different source.
4. **Fail-fast, DI via inspector** — required references validated once at setup;
   no repeated scene scans on hot paths (Docs/coding-standards.md "Fail Fast
   Validation", "Unity Object Access"). Surface resolves `UIDocument` via
   `RequireComponent` and `PlayerRoot` via a serialized reference (fallback find
   in `Awake`, matching `SkillLoadoutUi`).
5. **Allocation-light** — no per-frame allocations/closures. Pointer callbacks
   registered once at setup; `FireHeld` is a plain bool read
   (Docs/coding-standards.md "Allocation Rule"). This is not a hot combat path,
   but the rule still applies.
6. **UI Toolkit runtime pointer delivery** — runtime panels receive pointer
   events only via an `EventSystem` + `InputSystemUIInputModule` (or an equivalent
   Unity 6 default). If the current bar buttons already respond to clicks, the
   surface's `PointerDownEvent` is delivered by the same mechanism with no new
   dependency. This is an editor-side fact to verify (see task 005).

## Mechanisms Reused vs. Introduced

Reused:
- **UI Toolkit z-order + `picking-mode` + pointer capture** for layering and
  click propagation — the "UI decides propagation" behavior the user asked for.
- **UI-layer-pushes-into-PlayerRoot** wiring direction — already used by
  `SkillLoadoutUi.SetGameplayInputGate`. The fire-source registration follows the
  same direction; the modal suspend replaces the old gate call.
- **`SkillDriver.Tick(bool fireHeld, ...)`** — unchanged signature; only the
  source of `fireHeld` changes.

Introduced:
- **`IGameplayInputSource`** (Game Logic) — one bool contract, `FireHeld`. Small,
  and it *removes* PlayerRoot's direct Input System coupling for fire rather than
  adding a parallel path. Justified: it is the seam that lets the UI layer own the
  layered fire decision without Game Logic depending on UI.
- **`GameplayInputSurface`** (UI layer) — the bottom-layer element owner.

## Design Validation Against Invariants

- *One-way refs*: interface in Game Logic, implementation in UI layer, push
  direction UI→GameLogic. Holds. ✔
- *Awake/OnEnable*: surface touches `rootVisualElement` and registers with
  `PlayerRoot` in `OnEnable`; validation of own refs in `Awake`. All surface
  `OnEnable`s complete before `PlayerRoot.Start`, so Start-time observation is
  safe. Holds. ✔
- *Update timing*: fire still sampled in `PlayerRoot.Update`. Holds. ✔
- *Fail-fast / DI*: serialized refs + `RequireComponent(UIDocument)`; single
  fallback find in `Awake`. Holds. ✔
- *Allocation-light*: callbacks registered once; no per-frame allocation. Holds. ✔
- *Pointer delivery*: no new input dependency beyond what current buttons already
  require; flagged for editor verification. Holds pending task 005. ✔

## Minimal/Additive vs. Refactor Comparison

**Minimal/additive approach** — add `IGameplayInputSource` + surface, but keep
`attackAction` and both gate booleans in `PlayerRoot`, OR-ing the surface in.
- Resulting data flow: two fire sources (raw `Attack` action gated by booleans,
  plus the surface) feeding one `fireHeld`.
- New concepts/types: interface + surface, on top of the retained gate booleans.
- Copies/translations added: the pointer-over-UI state is still tracked in
  parallel with the structural layering that already answers the same question.
- Long-term cost: two sources of truth for "is the player firing," two places to
  keep in sync, the original bug class stays reachable through the raw path.

**Refactor approach (chosen)** — remove the mouse `Attack` read and the
`pointerOverSkillUi` gate from `PlayerRoot`; make the surface the single fire
source; collapse the remaining gate to one modal-suspend flag.
- Resulting data flow: one fire source (`IGameplayInputSource`) → `PlayerRoot` →
  `SkillDriver.Tick`. Modal suspend is a separate, explicit input-mode gate.
- Existing types changed: `PlayerRoot` (drop `attackAction`, `pointerOverSkillUi`);
  `SkillLoadoutUi` (call `SetGameplayInputSuspended` instead of the two-arg gate).
- Copies/translations removed: no more parallel pointer-over-UI tracking; the
  layering answers it structurally.
- Long-term benefit: single source of truth for fire; the "click UI also fires"
  bug becomes structurally impossible, not gated.

**Decision:** choose refactor. Reason: the additive path leaves two data paths for
the same "is firing" concept and keeps the bug reachable; the refactor collapses
to one source of truth with clearer ownership.

## Default Decision Rule Applied

"Is the player firing?" is one domain concept. Today it is computed from a raw
action plus two booleans. The plan refactors toward one source of truth
(`IGameplayInputSource`), consistent with the default rule; the only retained gate
(`modal suspended`) is a genuinely distinct concept (input mode during loadout
editing), not a second representation of firing.

## Task List

- [001-gameplay-input-source-interface.md](001-gameplay-input-source-interface.md)
  — add `IGameplayInputSource` in Game Logic.
- [002-world-click-surface.md](002-world-click-surface.md) — add
  `GameplayInputSurface` (bottom-layer element + pointer capture + `FireHeld`).
- [003-playerroot-consume-fire-source.md](003-playerroot-consume-fire-source.md)
  — `PlayerRoot` consumes the fire source, drops `attackAction`, collapses gates
  to modal-suspend.
- [004-skillloadoutui-gate-cleanup.md](004-skillloadoutui-gate-cleanup.md) —
  `SkillLoadoutUi` calls `SetGameplayInputSuspended`; add surface/pass-through USS.
- [005-editor-wiring-and-verification.md](005-editor-wiring-and-verification.md)
  — user editor steps: add surface component to `GameUI`, wire refs, verify
  EventSystem/pointer delivery, playtest.

Dependency order: 001 → 002 → 003; 004 depends on 003; 005 depends on all.

## Open Questions / Decisions

1. **Missing fire source at runtime** — if a scene has the player but no surface,
   should `PlayerRoot` throw at `Start` (fail-fast) or warn once and treat
   `FireHeld` as false (robust/HUD-optional)?
   *Recommendation:* warn + no-fire, so headless/benchmark-style scenes without
   the HUD still run. Firing is inherently coupled to the panel by this design;
   a warning makes the coupling visible without hard-crashing unrelated scenes.
2. **Click-outside-to-close the picker** — a full-screen modal backdrop would
   both block world clicks and close the picker on outside-click. Not required
   (modal suspend already blocks fire); listed as an optional follow-up, out of
   scope unless wanted.
3. **`Attack` action cleanup** — the action keeps its bindings but goes unread by
   the player. Left as-is (no `.inputactions` edit). Can be pruned later if
   desired.
