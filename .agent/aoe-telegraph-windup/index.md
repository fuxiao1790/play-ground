# AOE Telegraph — Windup Phase

## Scope

Add an authored pre-impact **windup** + **telegraph VFX** to every AOE (impact and lingering),
built on the clean base produced by [`../aoe-telegraph-refactor/`](../aoe-telegraph-refactor/index.md).
This plan **depends on that refactor being complete** (`LingeringAoeTag` discriminator, plain-data
`CombatLifetimeComponent`). It does not touch the discriminator or lifetime again.

Timeline per AOE:

```
spawn ──Windup(InitialDelaySeconds)──► activate ──► (impact: detonate once / lingering: tick+expire)
        telegraph VFX (trigger 4) +    collision + timed-spawn turn on
        sprite render on
        collision OFF, lifetime frozen
```

`InitialDelaySeconds <= 0` → born already activated → **byte-identical to post-refactor behavior.**

## Adopted design decisions (from prior `project_aoe_initial_delay`)

- Windup applies to **both** impact and lingering AOEs.
- Entity is created and its **sprite renders immediately**; the telegraph VFX draws on top.
- **Damage/collision and timed-spawn are suppressed** during windup; lifetime countdown is frozen.
- Windup is a **fixed authored duration** (not cast-speed scaled).
- Existing spawn(0)/hit(1)/expire(2)/pulse(3) VFX are unchanged; telegraph is a new trigger `4`
  that fires at spawn.

## Core mechanism (orthogonal, no overload)

One new **enableable** component carries the whole windup state; it is present on both AOE
archetypes and is **enabled == "in windup"**:

```csharp
// enabled == in windup. Disabled (default) == activated/normal. Present on both AOE archetypes.
public struct AoeWindupComponent : IComponentData, IEnableableComponent
{
    public float Remaining;        // windup seconds left
    public bool ActivateCollision; // NeedsCollision(cmd) captured at spawn
    public bool ActivateTimedSpawn;// HasTimedSpawner(cmd) captured at spawn (lingering only)
}
```

A new `AoeWindupSystem` (UpdateBefore `CombatLifetimeSystem`, which is already before both collision
systems and `TimedSpawnSystem`) ticks `Remaining`; on expiry it enables `AoeCollisionActiveTag`
(iff `ActivateCollision`) and `TimedSpawnComponent` (iff `ActivateTimedSpawn`), then disables
`AoeWindupComponent`. Activation is therefore visible to lifetime/collision/timed the **same frame**.

Why this stays contained:
- **Collision** already runs only on `WithAll<AoeCollisionActiveTag>` (enabled). Born disabled during
  windup ⇒ collision skips windup AOEs with **zero query change**.
- **Timed spawn** already runs only on `WithAll<TimedSpawnComponent>` (enabled), and it is shared with
  projectiles. Born disabled during windup ⇒ skipped with **zero query change** and **without**
  excluding projectiles.
- **Lifetime** (`AoeLifetimeJob`) and **pulse VFX** (`AoePulseVfxJob`) do not gate on those tags, so
  each gets a single `[WithDisabled(typeof(AoeWindupComponent))]` — both are AOE-only and both
  archetypes carry the component, so this cleanly freezes lifetime and suppresses pulse during windup.
- **Render** stays on during windup (sprite visible) — no change; materialization just enables
  `CombatRenderActiveTag`.

## Constraints & invariants the change must respect

- **`Active` is the occupancy gate.** During windup `Active` must be **enabled** (else the entity
  reads as a dead pool slot at [AoeSpawnApplySystem.cs:50](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L50)/[:264](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L264)).
  So windup sets `Active` on, `AoeCollisionActiveTag` off — never gate windup on `Active`. (code)
- **`NeedsCollision` = visual-only guard.** Post-windup collision must AND with `ActivateCollision`
  (captured `NeedsCollision(cmd)`), so a visual-only AOE never gains collision at windup end
  ([AoeSpawnApplyUtility.NeedsCollision](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L543)). (code)
- **Impact death depends on a collision pass.** An impact AOE dies only by detonating
  (`deactivateAfterPass`), so a windup impact with `ActivateCollision == false` would never die.
  Guard: **impact enters windup only when `NeedsCollision`** (a visual-only impact is inert today,
  born `Active` disabled — keep that path unchanged). Lingering always may enter windup (it dies via
  lifetime regardless). (code — [AoeSpawnApplySystem.cs:210-214](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L210))
- **Ordering for same-frame activation.** `AoeWindupSystem` must be `UpdateBefore CombatLifetimeSystem`
  (itself `UpdateBefore` both collision systems, [CombatLifetimeSystem.cs:20](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs#L20)). (code)
- **Shared VFX queue producer chaining.** The telegraph emit rides the existing
  `CombatVfxDispatchSingleton.PendingSpawns` in the expansion jobs that already emit trigger 0;
  it must chain `ProducerHandle` the same way (it is inside the same job — automatic). (code + [vfx notes])
- **VFX graph contract unchanged.** Telegraph uses the existing `Positions`+`AreaSizes`+`SpawnCount`
  contract (registered `requireAreaSizeContract: true`); no per-event duration is added
  ([CombatVfxDispatcher.cs:60](../../Assets/Scripts/System/Vfx/CombatVfxDispatcher.cs#L60)). Fixed windup
  ⇒ the telegraph graph animates over its own baked duration. (code)
- **Pool reuse writes every field.** Both reuse jobs and the ECB reset paths must set
  `AoeWindupComponent` + its enabled state so a reused slot never inherits a stale windup. (code)
- **`InitialDelaySeconds == 0` is byte-identical.** The zero path must set `AoeWindupComponent`
  disabled and leave all other enable states exactly as today. (invariant)

## Mechanisms reused vs. introduced

Reused:
- **Enableable-tag phase gating** — windup flips `AoeCollisionActiveTag`/`TimedSpawnComponent`, the
  same tags materialization already sets; consumers are unchanged.
- **Expansion-time VFX emit** — telegraph enqueues into the same queue and job as spawn VFX (trigger 0).
- **`NeedsCollision`/`HasTimedSpawner`** conventions — captured into the windup component verbatim.
- **Existing VFX register/dispatch contract** — telegraph is just trigger `4`.

Introduced (justified):
- **`AoeWindupComponent` (enableable, holds `Remaining` + 2 flags)** — one orthogonal phase signal;
  removes the need to overload any existing component (the exact trap that sank attempt 1).
- **`AoeWindupSystem`** — single new system, two Burst jobs (impact/lingering, split because impact
  lacks `TimedSpawnComponent`/`CombatLifetimeComponent`), mirrors the collision-system split.
- **Telegraph authoring slot + `InitialDelaySeconds`** on the authoring/command path.

## Design validation (against the invariants)

- *Occupancy:* windup sets `Active` on, `AoeCollisionActiveTag` off. ✓
- *Visual-only:* activation ANDs with `ActivateCollision`. ✓
- *Impact never-dies:* impact enters windup only if `NeedsCollision`. ✓
- *Same-frame activation:* `AoeWindupSystem` before `CombatLifetimeSystem` before collision. ✓
- *Projectiles unaffected:* timed-spawn suppression via `TimedSpawnComponent` disable, not a new
  filter; `AoeLifetimeJob`/`AoePulseVfxJob` filters are AOE-only. ✓
- *Zero-delay identity:* windup disabled at spawn ⇒ every other enable state as today. ✓
- *VFX:* existing contract; telegraph fire-and-forget. ✓

## Minimal/additive vs. refactor comparison

This feature is **additive by construction on the refactored base** — and that is now the correct
call, not a structural warning, precisely because the refactor already removed the overload:
- **Minimal/additive (chosen):** one enableable `AoeWindupComponent` + one system + two consumer
  filters. New concepts: the windup component/system. Copies/translations: none. It does **not**
  re-introduce a second representation of any existing concept (windup is a genuinely new, orthogonal
  phase), so none of the additive structural warnings apply.
- **Refactor alternative (rejected):** folding windup into a `CombatLifetimeComponent` `Phase` field
  (attempt 2). Rejected: it forces lifetime onto impact, forces discriminator relocation, and drags in
  overshoot/catch-up — the exact blast radius that failed. The refactor plan already bought the
  clarity; windup should not re-open lifetime.

**Decision: additive on the refactored base.** Reason: windup is a new orthogonal concept, so a new
component is the one-source-of-truth representation, not a hidden overload.

## Default decision rule

Windup is a distinct domain concept (a lifecycle phase gate), not a second representation of lifetime
or of the impact/lingering split — so a dedicated component is the single source of truth. No existing
representation is duplicated.

## Task list

- [001](001-windup-component-and-system.md) — `AoeWindupComponent` + `AoeWindupSystem` (impact +
  lingering jobs) + the two consumer filters. (inert until materialization sets it)
- [002](002-command-threading.md) — Thread `InitialDelaySeconds` through `AoeSpawnCommand` /
  `AoeSpawnRequest` and every builder site. (default 0 everywhere; no behavior yet)
- [003](003-materialization-windup-entry.md) — Add `AoeWindupComponent` to both archetypes; set
  windup state at spawn across reuse-job + ECB-reset paths (impact + lingering), with the impact
  `NeedsCollision` guard.
- [004](004-telegraph-vfx-and-authoring.md) — `TelegraphEffect` + `initialDelaySeconds` authoring
  fields, register trigger `4`, emit trigger `4` in both expansion systems when `InitialDelaySeconds > 0`.
- [005](005-tests.md) — Windup sim tests (suppression, activation timing, zero-delay identity,
  pool-reuse reset) + telegraph emit test.

Order: 001 → 002 → 003 → 004 → 005. After 003 the windup gameplay works headless (testable without
the telegraph art); 004 adds the visual.

## Open questions / considerations

- **No overshoot carry.** On the frame `Remaining <= 0`, the AOE activates and lifetime/collision begin
  next tick from full; the sub-frame overshoot is dropped. Deliberate (windup is short + fixed) —
  avoids attempt 2's catch-up complexity. Flag if any AOE needs sub-frame-accurate windup.
- **Telegraph-only look** (hide the AOE sprite during windup) is a one-line follow-up: gate
  `CombatRenderActiveTag` on windup at materialization. Not in this plan (decision: sprite visible).
- **Duration-driven telegraph graphs** (a per-event `Durations` buffer) are deferred; fixed authored
  windup makes them unnecessary. Revisit only if telegraphs must scale to a runtime-variable windup.
