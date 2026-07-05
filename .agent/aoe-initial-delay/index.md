# AOE Initial Delay + Telegraph VFX

## Summary
Give every AOE kind (impact and lingering) an authored **initial delay** (windup)
and an associated **telegraph VFX event**. When an AOE spawns with
`initialDelaySeconds > 0`:

- The ECS entity **is created immediately** and its **sprite renders immediately**
  (the user wants the sprite to indicate "the AOE is now an entity in the ECS").
- Its **telegraph VFX fires immediately** at spawn (a new VFX trigger, `4`).
- **Damage / collision is suppressed** until the delay elapses. At activation the
  AOE behaves exactly as it does today: impact does its one-shot pass and
  deactivates; lingering begins its lifetime countdown + interval spawns + ticking.

When `initialDelaySeconds <= 0` the pipeline is **byte-for-byte identical to
today** (no delay component enabled, no telegraph, immediate activation).

## User decisions (captured this session)
1. **Behavior**: "aoe should be created, damage delayed, vfx event fired
   immediately." → entity + sprite exist on-field during an inert windup; damage
   suppressed; telegraph fired at spawn. Applies to **all** AOE kinds.
2. **VFX**: the new VFX "is intended to be a telegraph" → new trigger `4`, emitted
   immediately at spawn while the AOE winds up. The existing spawn VFX (trigger
   `0`) and the hit VFX (trigger `1`) are **left unchanged**; the impact "boom" at
   contact is already covered by trigger `1`.
3. **Windup visual**: "show both" → sprite renders during the windup alongside the
   telegraph VFX.

## Core mechanism (why this stays small)
Windup state is expressed with components the AOE archetypes already carry, plus
one new enableable timer:

| State | Windup (delaying) | Activated (today's live state) |
|---|---|---|
| `Active` | enabled (prevents pool reuse, keeps entity alive) | enabled |
| `CombatRenderActiveTag` | enabled (sprite shows) | enabled |
| `AoeCollisionActiveTag` | **disabled** (no damage) | enabled iff `NeedsCollision` |
| `CombatLifetimeComponent` (lingering only) | **disabled** (does not tick) | enabled |
| `TimedSpawnComponent` (lingering only) | **disabled** (no interval spawns) | enabled iff configured |
| `AoeDelayComponent` (new, enableable) | **enabled**, `Remaining` counts down | disabled |

Disabling `CombatLifetimeComponent` during windup reuses the **exact pattern pulse
one-shots already use** (see `LingeringAoeCollisionSystem`'s `WithPresent` comment
and `AoePulseVfxSystem`'s `!lifetimeEnabled` early-out). Consequences, verified
against current queries:
- `CombatLifetimeSystem.AoeLifetimeJob` → `WithAll<CombatLifetimeComponent>`
  (enabled-only) ⇒ **skips windup AOEs** (lifetime not consumed). No change.
- `TimedSpawnSystem.TimedSpawnJob` → `WithAll<CombatLifetimeComponent,
  TimedSpawnComponent>` ⇒ **skips windup AOEs** (no interval spawns). No change.
  (Projectiles are unaffected — they never carry `AoeDelayComponent`, so we never
  gate on it; we gate on the components they already share the query semantics
  with.)
- `AoePulseVfxSystem.AoePulseVfxJob` → already early-outs on `!lifetimeEnabled`
  ⇒ **skips windup AOEs**. No change.
- Impact + lingering collision systems require `AoeCollisionActiveTag` enabled ⇒
  **skip windup AOEs**. No change.

So the only new runtime system is `AoeDelaySystem` (tick + activate); every
existing consumer system is untouched.

## Constraints & invariants respected
- **Archetype/reuse discriminator** (`project_unify_combat_archetypes`,
  code: `AoeSpawnApplySystem`): impact = `WithNone<CombatLifetimeComponent>` +
  dead slot `WithDisabled<Active>`; lingering = `WithAll<CombatLifetimeComponent>`
  + `WithDisabled<Active>`. Windup keeps `Active` **enabled**, so a delaying AOE is
  never mistaken for a dead pool slot. Adding `AoeDelayComponent` to both
  archetypes does not change either dead-slot query's matching set. **Impact never
  gains `CombatLifetimeComponent`**, so the discriminator is preserved.
- **Parallel enableable mutation**: `AoeDelaySystem` flips `AoeCollisionActiveTag`
  / `CombatLifetimeComponent` / `TimedSpawnComponent` / `Active` /
  `CombatRenderActiveTag` / `AoeDelayComponent` per-entity via `EnabledRefRW` in a
  `ScheduleParallel` job — identical to `CombatLifetimeSystem`. Each entity is
  independent, so this is safe.
- **VFX contract** (`project_vfx_flush_removal`, code: `VfxPendingSpawn`,
  `CombatVfxDispatcher`): trigger is a `byte`, key = `typeId*256 + trigger`, so
  trigger `4` is free and non-breaking. Telegraph is enqueued as an ordinary
  fire-and-forget `VfxPendingSpawn` on the shared queue via the existing producer
  chaining in `AoeExpansionCore` (already a VFX producer). Registration mirrors
  the existing `Register(typeId, 0..3, ...)` calls in `PlayerSkillDriver`.
- **Data-path single source of truth**: `initialDelaySeconds` rides the existing
  `AoeSpawnCommand`/`AoeSpawnRequest` template pipeline like every other authored
  field (`Lifetime`, `TickIntervalSeconds`); no parallel command or event type.
- **`initialDelay <= 0` regression guard**: default `0` ⇒ no `AoeDelayComponent`
  enabled at apply, no telegraph, immediate activation ⇒ existing content and
  tests unaffected (new struct field defaults to `0`).

## Mechanisms reused vs. introduced
- **Reused**: enableable-component gating (windup = lifetime disabled, mirrors
  pulse one-shots); the `AoeSpawnCommand` template pipeline; the VFX trigger/queue
  + producer-chaining; the pooled reuse/cold-create apply split; the
  `CombatLifetimeSystem` per-entity tick-then-flip job shape.
- **Introduced**: one enableable timer `AoeDelayComponent`; one system
  `AoeDelaySystem`; one VFX trigger (`4`) + one authored asset (`DelayEffect`);
  one authored scalar (`initialDelaySeconds`).

## Minimal/additive vs. refactor comparison
- **Minimal/additive (chosen)**:
  - resulting data flow: `initialDelaySeconds` + `DelayEffect` flow through the
    unchanged `AoeSpawnCommand` template + VFX registration; windup expressed by
    toggling components the archetypes already own + one new timer.
  - new concepts/types: `AoeDelayComponent`, `AoeDelaySystem`, VFX trigger `4`,
    authored `initialDelaySeconds`/`DelayEffect`.
  - copies/translations added: none — no new event/command struct, no new data
    path; consumer systems unchanged.
  - long-term cost: one extra enableable component in the AOE archetypes and one
    early-frame tick job.
- **Refactor alternative considered**: hoist "delay-before-active" into a shared
  cross-archetype component usable by projectiles too, or fold the timer into a
  generalized `TimedSpawnState`-style lifecycle.
  - existing types changed/removed: would touch projectile archetypes + spawn
    apply for a feature projectiles do not request.
  - copies/translations removed: none (there is no duplication to collapse — the
    additive path introduces no second representation of any existing concept).
  - long-term benefit: speculative reuse only.
- **Decision: choose additive.** The additive path triggers **no** structural
  warning: it adds no second data type for an existing concept, no parallel
  old/new path, no shim, and keeps one source of truth (`AoeSpawnCommand`). The
  refactor would generalize ahead of a real second consumer. `AoeDelayComponent`
  sits alongside the existing AOE-specific `AoePulseVfxComponent` by the same
  rationale.

## Default decision rule
`initialDelaySeconds` describes the same "authored AOE timing" concept as
`Lifetime`/`TickIntervalSeconds` and lives on the same `AoeSpawnCommand` — one
source of truth, no migration needed.

## Design validation (against each invariant)
- Reuse discriminator: windup keeps `Active` enabled ⇒ not a dead slot; impact
  never gains lifetime ⇒ discriminator intact. ✓
- Consumer systems skip windup via existing enableable queries (proved above). ✓
- Parallel enableable flips mirror `CombatLifetimeSystem`. ✓
- VFX trigger `4` non-colliding; telegraph uses existing producer chaining. ✓
- `delay <= 0` ⇒ identical to today. ✓
- Activation-frame timing: `AoeDelaySystem` runs `UpdateBefore` collision/lifetime,
  so a just-activated impact AOE one-shots the same frame and a lingering AOE
  begins ticking the same frame (loses ≤1 dt of lifetime — negligible). ✓

## Task list
- `001-ecs-delay-component.md` — `AoeDelayComponent`, archetype additions,
  `AoeSpawnCommand.InitialDelaySeconds`.
- `002-apply-windup-init.md` — apply systems set windup vs immediate initial state
  (reuse jobs + cold-create record paths, impact + lingering).
- `003-aoe-delay-system.md` — new `AoeDelaySystem` (impact + lingering tick/activate
  jobs).
- `004-telegraph-vfx.md` — emit trigger `4` at spawn from `AoeExpansionCore`; update
  trigger legend.
- `005-authoring-plumbing.md` — `initialDelaySeconds` + `DelayEffect` through
  definitions/prefabs/config/type-def/runtime-def/request/command builders +
  trigger-`4` registration.
- `006-tests.md` — windup damage-suppression, telegraph emission, lifetime-not-
  consumed-during-windup, `delay=0` regression.

## Open questions / considerations
- **Cast/attack-speed scaling**: `initialDelaySeconds` is treated as a fixed
  authored duration (like `lifetimeSeconds`), not scaled by cast speed. Flag if a
  scaled windup is wanted (would ride the same modifier path as other stats).
- **Telegraph `AreaSize` contract**: registered with `requireAreaSizeContract:
  true` like the other AOE VFX; the telegraph graph must expose `AreaSizes` so it
  can scale to the AOE footprint.
- **Visual-only + delay edge**: an AOE with `NeedsCollision == false` and a delay
  shows sprite+telegraph for the delay, then `AoeDelaySystem` deactivates it
  (impact) / lets it linger visually (lingering). Covered by the `ActivateCollision`
  flag; called out in 002/003.
