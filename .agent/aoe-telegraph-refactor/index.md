# AOE Telegraph — Refactor Phase (un-multiplex `CombatLifetimeComponent`)

## Scope

This plan covers **only the refactor that clears the way for a windup/telegraph**, not
the windup itself. Goal: after this plan the codebase is behavior-identical to today but
`CombatLifetimeComponent` no longer carries hidden meaning, so the later windup step is a
small orthogonal addition. The refactor is independently testable (every subtask leaves
the build green and simulation behavior byte-for-byte unchanged) and is meant to be
validated in isolation before any windup code is written.

Windup itself (a `AoeWindupComponent{Remaining}` + `AoeWindupTag` gate + `AoeWindupSystem`,
plus the telegraph VFX trigger) is a **follow-up plan**, out of scope here. The last
section sketches how the clean base hosts it, so reviewers can see the endpoint.

## Why the two previous attempts hit a wall

`CombatLifetimeComponent` ([CombatEcsComponents.cs:214](../../Assets/Scripts/System/Common/CombatEcsComponents.cs#L214))
is one component read three unrelated ways:

| Read | Meaning | Sites |
|---|---|---|
| present vs absent | lingering vs impact AOE (routing discriminator) | `ImpactAoeCollisionSystem` `WithNone`, `LingeringAoeCollisionSystem` `WithPresent`, both pool dead-slot queries, `CombatPoolCleanupSystemTests` |
| enabled vs disabled | repeating lingering vs pulse one-shot | `LingeringAoeCollisionSystem` `!enabledLifetime`, `AoePulseVfxSystem` `!lifetimeEnabled` |
| `Remaining` value | the lifetime countdown timer | `CombatLifetimeSystem` |

A windup is a **fourth** state (alive + rendering, collision off, timer paused) orthogonal
to all three, and there is no free axis for it.

- **Attempt 1** (additive `AoeDelayComponent`/`AoeDelaySystem`) tried to signal windup by
  *disabling* `CombatLifetimeComponent`. That collides head-on with the "disabled = pulse
  one-shot" reading, and impact AOEs have no lifetime component to disable at all. Dead end.
- **Attempt 2** (commit `d7cabf8`) over-corrected: it baked a `Phase` field *into* lifetime,
  which forced lifetime onto impact, which forced relocating the discriminator, which forced
  multi-hit catch-up and a new VFX duration contract — four load-bearing invariants moving in
  one commit → "not really working" → backed out.

## Key discovery that shrinks this refactor

**The enable-bit overload is already dead.** Nothing in the codebase disables
`CombatLifetimeComponent` — every write is `true` (`AoeSpawnApplySystem` materialization +
reuse, `ProjectileSpawnApplySystem` materialization + reuse, all tests). Therefore the
`!enabledLifetime` branch in `LingeringAoeCollisionSystem` and the `!lifetimeEnabled` skip in
`AoePulseVfxSystem` are unreachable, and `deactivateAfterPass` for lingering is always `false`.
Removing them is a pure dead-code deletion, provably behavior-identical. The impact archetype
(absent lifetime, one-pass-and-die) is the live replacement for the old pulse-one-shot idea.

So the refactor reduces to two behavior-preserving moves plus a de-vestigialization:
1. Give the impact/lingering split its **own name** (a tag), off lifetime presence.
2. **Delete** the dead enable-bit one-shot machinery.
3. Make `CombatLifetimeComponent` **plain (non-enableable) data** so the trap that sank
   attempt 1 (disable-to-mean-something) cannot be re-introduced.

## Rationale for the major decisions

- **Positive `LingeringAoeTag`, not `ImpactAoeTag`.** The real difference is "hits over a
  duration" (lingering) vs "hits once on contact" (impact). Lingering is the one that owns a
  timer/pulse/timed-spawner, so the systems that today say `WithPresent<CombatLifetimeComponent>`
  become `WithAll<LingeringAoeTag>` and the impact ones become `WithNone<LingeringAoeTag>`. The
  tag *names* the difference instead of inferring it from "does it have a timer".
- **Keep `CombatLifetimeComponent` lingering-only.** The refactor does **not** add lifetime to
  impact (that was attempt 2's self-inflicted coupling). Windup will store its own `Remaining`
  on its own component, so impact never needs a lifetime. Keeping lifetime lingering-only keeps
  the diff behavior-identical.
- **De-enableable the component.** Once the enable bit carries no meaning, dropping
  `IEnableableComponent` slams the door on "disable lifetime to signal X" — exactly the trap
  attempt 1 fell into — and removes every `SetComponentEnabled`/`EnabledRef`/`WithPresent`
  subtlety. Safe because nothing disables it.

## Constraints & invariants the change must respect

- **`Active` is the occupancy gate, not lifetime.** Every per-AOE job is `WithAll<Active>`;
  dead pool slots have `Active` disabled ([AoeSpawnApplySystem.cs:50](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L50)/[:264](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L264)).
  Lifetime becoming plain data is safe — nothing uses its enable bit for occupancy. (code)
- **Impact/lingering routing must stay exact.** Impact collision `WithNone`, lingering
  `WithPresent`, and both pool dead-slot queries currently discriminate on lifetime presence.
  Every one of those sites must migrate to the new tag in lockstep or AOEs misroute. (code)
- **`AoePulseVfxJob` is lingering-only via its component params.** It requires
  `AoePulseVfxComponent` (lingering-only) as a job parameter, so it auto-excludes impacts
  regardless of the lifetime param. Removing the `EnabledRefRO<CombatLifetimeComponent>` param
  must keep `ref AoePulseVfxComponent` so the exclusion is preserved. ([AoePulseVfxSystem.cs:43-54](../../Assets/Scripts/System/Aoe/AoePulseVfxSystem.cs#L43)) (code)
- **`CombatLifetimeSystem` and `TimedSpawnSystem` query lifetime by presence** (`WithAll`),
  which survives de-enableable unchanged; both read only `Remaining`. `TimedSpawnJob` is
  already impact-excluded by requiring `TimedSpawnComponent`. ([CombatLifetimeSystem.cs:97](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs#L97), [TimedSpawnSystem.cs:81](../../Assets/Scripts/System/Common/TimedSpawnSystem.cs#L81)) (code)
- **Projectiles share `CombatLifetimeComponent`.** They set it enabled at spawn and read
  `Remaining` via `ref` only — never toggle or `EnabledRef` it ([ProjectileCollisionSystem.cs:163](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs#L163)).
  De-enableable ripples to `ProjectileSpawnApplySystem` (drop `SetComponentEnabled` + `lifetimeMask`)
  and must stay behavior-identical. (code)
- **Pool reuse writes every field.** Reuse jobs and the ECB reset paths set the full component
  set on a reused slot; removing the lifetime-enable writes must not leave a stale enabled/disabled
  state readable by anything (it won't — the bit ceases to exist). (code)

## Mechanisms reused vs. introduced

Reused:
- **Zero-size discriminator tag** — mirrors existing tag components (`AoeTag`,
  `ProjectileTag`, `AoeCollisionActiveTag`); the new `LingeringAoeTag` is the same pattern.
- **`Active` occupancy gate** — untouched; remains the pool discriminator.
- **Presence-based `WithAll`/`WithNone` queries** — the routing just keys on the named tag
  instead of on lifetime presence.

Introduced (justified):
- **`LingeringAoeTag` (zero-size `IComponentData`)** — removes complexity: it replaces two
  overloaded readings (`WithNone`/`WithPresent<CombatLifetimeComponent>`) with one named
  discriminator; it does not add a parallel data path.

Removed:
- `IEnableableComponent` from `CombatLifetimeComponent`; the dead `!enabledLifetime` one-shot
  branch; `EnabledRefRO<CombatLifetimeComponent>` reads; all `SetComponentEnabled<CombatLifetimeComponent>`
  calls and `lifetimeMask` writes.

No new **system** and no new **data path** are introduced.

## Design validation (against the invariants)

- *Routing:* every presence-based site migrates to `LingeringAoeTag` in the same subtask
  (001) → no window where old and new discriminator disagree. ✓
- *Occupancy:* jobs keep `WithAll<Active>`; plain-data lifetime unaffected. ✓
- *Pulse exclusion:* `ref AoePulseVfxComponent` param retained → impacts still excluded. ✓
- *Timer semantics:* `Remaining` reads unchanged in `CombatLifetimeSystem`/`TimedSpawnSystem`/
  `ProjectileCollisionSystem`; only the (always-true) enable bit is removed. ✓
- *Projectiles:* only `SetComponentEnabled`/`lifetimeMask` writes removed; `Remaining` flow
  identical. ✓
- *Dead-code deletion:* the one-shot branch was unreachable (nothing disables lifetime), so
  removing it changes no runtime behavior. ✓

## Minimal/additive vs. refactor comparison

**Minimal/additive** (leave `CombatLifetimeComponent` as-is; later bolt windup on a new
component and hope):
- data flow: discriminator stays inferred from lifetime presence; enable bit stays present
  but vestigial; windup added as a parallel gate around an already-overloaded component.
- new concepts/types: eventually a windup component *plus* the still-live ambiguity of the
  lifetime enable bit.
- copies/translations: none, but the "disable lifetime to mean X" trap remains armed for the
  next implementer (this is precisely what sank attempt 1).
- long-term cost: four implicit encodings persist; every future AOE-lifecycle change re-derives
  which reading of lifetime applies.

**Refactor** (this plan):
- data flow: one named discriminator tag; `Remaining` is the only meaning left on lifetime;
  windup later attaches as a genuinely orthogonal component.
- concepts changed/removed: lifetime stops being the discriminator and stops being enableable;
  the dead pulse-one-shot path is deleted.
- copies/translations removed: two overloaded query readings collapse to one tag; the enable
  bit disappears.
- long-term benefit: single source of truth per concept; the windup step cannot re-introduce
  the attempt-1 trap; adding future lifecycle phases is orthogonal.

**Decision: refactor.** Reason: three of the four concepts multiplexed onto one component are
the disease behind both failed attempts; the refactor is provably behavior-identical here (the
enable-bit path is already dead), so the usual migration risk that would argue for additive
does not exist.

## Default decision rule

Two representations of the same domain concept (impact/lingering, and the lifecycle phase)
were multiplexed onto one component; collapse toward one source of truth unless a compatibility
or migration reason blocks it. There is none — no serialized data depends on the lifetime enable
bit, and nothing disables it.

## Task list

- [001](001-lingering-tag-discriminator.md) — Introduce `LingeringAoeTag`; migrate all
  impact/lingering routing off `CombatLifetimeComponent` presence. (behavior-identical)
- [002](002-delete-dead-oneshot-path.md) — Delete the dead pulse-one-shot enable-bit reads
  in `LingeringAoeCollisionSystem` and `AoePulseVfxSystem`. (behavior-identical)
- [003](003-lifetime-plain-data.md) — Drop `IEnableableComponent` from `CombatLifetimeComponent`;
  remove all `SetComponentEnabled`/`lifetimeMask`/`EnabledRef`/`WithPresent` uses across AOE,
  projectile, and tests. (behavior-identical)
- [004](004-docs-and-comments.md) — Update component doc-comments and ecs-notes to describe the
  named discriminator + plain lifetime, and mark where windup will attach. (no behavior)

Order: 001 → 002 → 003 → 004. 002 must precede 003 (003 removes the component's enableability,
which requires the `EnabledRef` reads deleted first).

## How the clean base hosts the later windup (out of scope, for context)

After this refactor, windup is a small orthogonal addition — no change to the discriminator or
to lifetime:
- new `AoeWindupComponent { float Remaining }` + enableable `AoeWindupTag`, present on **both**
  archetypes (impact stores its own windup timer here — no lifetime needed);
- new `AoeWindupSystem` (UpdateBefore collision) ticks `Remaining`, and on expiry enables
  `AoeCollisionActiveTag` (ANDed with `NeedsCollision`) and `TimedSpawnComponent`;
- during windup: render on, `AoeCollisionActiveTag` off, lifetime/pulse/timed jobs gated with
  `WithDisabled<AoeWindupTag>` so they skip windup entities;
- telegraph VFX rides the existing dispatch queue on a new trigger, emitted at windup start;
- `InitialDelaySeconds == 0` → born past windup → byte-identical to post-refactor behavior.

## Open questions / considerations

- **Tag polarity** is decided (`LingeringAoeTag`, positive on lingering). If a reviewer prefers
  tagging impact, it is a mechanical flip but inverts every migrated query — flagged, not blocking.
- **Whether to de-enableable in the same PR as 001/002** — kept as separate subtask 003 so it can
  be validated/reverted independently; it is the highest-surface (touches projectiles + tests)
  though still behavior-identical.
