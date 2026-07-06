# Combat Arming (initial delay for projectiles + AOEs)

> Supersedes the earlier armed-delta / two-phase-death draft of this file. That
> model was dropped in design review: on the Arming→Armed crossing the entity
> simply flips to armed and starts fresh (no per-entity armed-delta, no
> mark-then-commit death). Also depends on the completed
> `../combat-gate-consolidation/` (collision tags merged; `CombatRenderActiveTag`
> deleted; render derives from `Active`).

## Summary

Add an authored **arm time** (initial delay / windup / telegraph) to projectiles
and AOEs. While arming, the entity is live and reuse-protected but frozen: no
movement, no collision, no sprite, no lifetime countdown, no timed-spawn, no
tracking — only a telegraph VFX. When the arm timer elapses it becomes armed and
every held system resumes with its already-correct gate values.

Arming is modeled as an **orthogonal pause overlay**, not a gate rewrite:

- Two new components on every combat archetype:
  - `ArmingTag : IComponentData, IEnableableComponent` — enabled iff currently
    arming. This is the single pause switch.
  - `CombatArmingComponent { float Remaining }` — plain data, the arm countdown.
  - They are **two** components on purpose: a single enableable-with-data
    component would force the arming job to take it as both `ref` (Remaining) and
    `EnabledRefRW` (disable), which is the documented UB pattern.
- Spawn apply sets the normal **armed** gate values exactly as today, and
  additionally: if `ArmSeconds > 0`, enable `ArmingTag` and set
  `Remaining = ArmSeconds`; else leave `ArmingTag` disabled. `SpawnStateFor` is
  unchanged in meaning.
- `CombatArmingSystem` (common) counts `Remaining` down on `Active` arming
  entities and disables `ArmingTag` at `<= 0`. Runs before the held systems so a
  just-armed entity acts the same frame.
- Every system that must pause during arming excludes arming entities with
  `WithDisabled<ArmingTag>` (movement, both lifetime jobs, all three collision
  jobs, timed-spawn, tracking). Render prepare additionally degenerates arming
  instances via the `ArmingTag` mask (it runs under
  `IgnoreComponentEnabledState`, so it cannot use a query clause).

Because arming leaves the armed gate bits in place and only suspends systems,
`ArmSeconds == 0` is exactly today's behavior.

Verification is user-run PlayMode (harness cannot build/run Unity). The feature is
testable in isolation by driving `ArmSeconds > 0` through a spawn command and
asserting frozen-then-live.

## Rationale

Initial delay needs an entity that is live and protected from reuse, telegraphed
by VFX, but not drawn, not moving, not colliding, not counting down its lifetime,
and not spawning children. Today no gate expresses "live but not yet acting."

Two rejected shapes and why:

- **Gate-rewrite phase** (hold by clearing collision/render/timed bits at spawn,
  then re-set them on arm-complete): forces the arming system to recompute and
  re-apply per-domain gate values it does not own, duplicating `SpawnStateFor`'s
  decision into a deferred mask. Structural warning: second source of truth for
  gate state.
- **Armed-delta / two-phase death** (preserve the sub-frame slice on the crossing
  frame): large machinery (per-entity `ArmedDelta`, reordered death commit) for a
  sub-frame timing nicety. Dropped by decision — short arm times plus
  `TimedSpawn`'s existing `MaxTicksPerUpdate` catch-up make the lost slice
  invisible.

The pause-overlay keeps `SpawnStateFor` as the sole gate authority and makes
arming a single orthogonal switch.

## Constraints & Invariants

- **Pooling uses disabled `Active`; arming keeps `Active` enabled.** Dead-slot
  reuse (`WithDisabled<Active>`) therefore never grabs an arming entity, and pool
  cleanup counts it as live — exactly the reuse-protection arming needs, for free.
  Sources:
  [ProjectileSpawnApplySystem.cs](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs),
  [AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs).

- **Never `ref` + `EnabledRefRW` the same component.** Arming is split into
  `ArmingTag` (enableable, no data) and `CombatArmingComponent` (plain Remaining).
  Source: memory `reference_ijob_ref_plus_enabledref`;
  [ecs-notes.md](../../Docs/reference/simulation/ecs-notes.md).

- **Do not multiplex `CombatLifetimeComponent`.** Arm time is its own countdown in
  `CombatArmingComponent`, never a second meaning on lifetime. Two prior AOE
  initial-delay attempts died on lifetime multiplexing. Source: memory
  `project_aoe_initial_delay`.

- **External events are intent; state derivation lives in spawn apply.** `ArmSeconds`
  is a plain command field; only spawn apply sets `ArmingTag`/`Remaining`. Source:
  [spawn-events-and-commands.md](../../Docs/contracts/spawn-events-and-commands.md).

- **Registry templates are immutable, plain unmanaged data.** `ArmSeconds` is a
  `float` on `ProjectileSpawnCommand` / `AoeSpawnCommand`, safe inside
  `NativeHashMap<Hash128, …>`. Sources:
  [spawn-template-registry.md](../../Docs/reference/simulation/spawn-template-registry.md),
  [ProjectileSpawnPipeline.cs](../../Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs),
  [AoeSpawnPipeline.cs](../../Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs).

- **Render uses stable slots + degenerate instances.** `CombatRenderPrepareSystem`
  runs under `IgnoreComponentEnabledState` and degenerates via a mask, so arming
  suppression is a mask AND (`Active && !Arming`), not a query clause. Source:
  [CombatRenderPrepareSystem.cs](../../Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs).

- **Timed-spawn catch-up cap is preserved.** `TimedSpawnJob` keeps
  `MaxTicksPerUpdate`; arming only adds a `WithDisabled<ArmingTag>` gate. Source:
  [TimedSpawnSystem.cs](../../Assets/Scripts/System/Common/TimedSpawnSystem.cs).

- **VFX is visual-only.** Telegraph is a `VfxPendingSpawn` with a new
  `Trigger = 4` (arming); it carries no damage/status authority. Existing triggers:
  `0=spawn 1=hit 2=expire 3=pulse`. Source:
  [vfx-requests.md](../../Docs/contracts/vfx-requests.md),
  [VfxEcsComponents.cs](../../Assets/Scripts/System/Vfx/VfxEcsComponents.cs).

## Mechanisms reused vs. introduced

- **Reused:** enableable-gate chunk-skipping (arming is one more gate);
  `SpawnStateFor` stays the gate authority; `Active` provides reuse-protection;
  the `WithDisabled<ArmingTag>` query clause is the standard hold mechanism, same
  family as existing enable-gates.
- **Introduced:** `ArmingTag`, `CombatArmingComponent`, `CombatArmingSystem`,
  `ArmSeconds` command field, VFX trigger `4`. Each is minimal and orthogonal;
  none duplicates an existing data path.

## Design validation

- Reuse-protection during arming: `Active` enabled → PASS.
- No `ref`+`EnabledRefRW` UB: two-component split → PASS.
- No lifetime multiplex: separate `CombatArmingComponent` + lifetime held via
  `WithDisabled<ArmingTag>` (arm time does not consume lifetime) → PASS.
- `ArmSeconds == 0` behavior-identical: `ArmingTag` disabled → every clause matches
  as before, arming system skips, render mask false → PASS.
- Single gate authority: `SpawnStateFor` unchanged; arming adds no gate mask → PASS.
- Dead slot with stale enabled `ArmingTag` (died mid-arming): arming system and all
  consumers require `Active`; render degenerates on `Active` off; reuse re-sets
  `ArmingTag`. Harmless → PASS. (So arming has no dependency on the death-unify
  task.)

## Minimal/additive vs. refactor comparison

- **Additive (gate-rewrite phase):**
  - data flow: spawn clears gates, arming system re-applies them from a stored mask.
  - new concepts/types: `ArmingTag`, `CombatArmingComponent`, **plus** a deferred
    armed-gate mask duplicating `SpawnStateFor`.
  - copies/translations: gate intent copied spawn→mask→apply.
  - long-term cost: two sources of truth for armed gate state.
- **Refactor (pause overlay, chosen):**
  - data flow: gates set once at spawn (armed values); arming only suspends systems.
  - new concepts/types: `ArmingTag`, `CombatArmingComponent`, `CombatArmingSystem`.
  - copies/translations: none; no gate re-derivation.
  - long-term benefit: `SpawnStateFor` stays sole gate authority; arming is one
    orthogonal switch.
- **Decision:** refactor / pause-overlay.
- **Reason:** avoids a second representation of armed gate state; fewer data paths.

## Default decision rule

Arm state has one representation (`ArmingTag` + `Remaining`) and gate state has one
authority (`SpawnStateFor`); the two do not overlap, so no collapse is pending.

## Tasks

Independence: **001, 002, and 003 are mutually independent** and each
behavior-verifiable on its own. 003 does **not** require 002 (a stale arming bit on
a dead slot is harmless, see design validation). **004 depends on 003** (needs the
`ArmSeconds` command field). 001/002/003 touch some shared spawn/death files in
non-overlapping spots — no ordering dependency, only a same-time-branch merge.

- [001-projectile-spawnstate-mirror.md](001-projectile-spawnstate-mirror.md) —
  give the projectile spawn-apply a single `SpawnStateFor`-style gate helper (AOE
  already has one); collapse its two hand-written gate sites. Behavior-identical.
- [002-unify-death-kill-helper.md](002-unify-death-kill-helper.md) — one shared
  `Kill` helper for the four scattered despawn gate-drops. Behavior-identical.
- [003-arming-mechanism.md](003-arming-mechanism.md) — `ArmingTag` +
  `CombatArmingComponent` + `CombatArmingSystem` + `WithDisabled<ArmingTag>` on the
  held systems + render arming-mask + `ArmSeconds` command field + spawn wiring +
  telegraph VFX. Self-contained; tested via command-driven `ArmSeconds > 0`.
- [004-author-arm-seconds.md](004-author-arm-seconds.md) — author `ArmSeconds`
  through `SkillDefinition` / `CombatRoot` into the command. Depends on 003.

## Open questions / considerations

- **Stats count during arming:** `CombatStatsGatherSystem` counts `Active`
  projectiles/AOEs, which will include arming ones. Acceptable (they are live). If
  a "visible count" is wanted instead, add `WithDisabled<ArmingTag>` there too —
  deferred, not in scope.
- **Telegraph VFX emission point:** emit `Trigger = 4` once at spawn when
  `ArmSeconds > 0`, co-located with the existing spawn-VFX emission
  ([AoeSpawnExpansionSystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs)
  emits `Trigger = 0`; confirm the projectile spawn-VFX site during 003).
- **Impact AOE** has no `CombatLifetimeComponent`; it gains `ArmingTag` +
  `CombatArmingComponent` and, while arming, is excluded from its one-shot
  collision pass — so it telegraphs, then does its single pass when armed.
