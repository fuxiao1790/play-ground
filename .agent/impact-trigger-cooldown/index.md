# Impact-Trigger Cooldown

## Goal

Add an authored **cooldown** to both on-impact triggers — `OnImpactAoeTrigger`
and `OnImpactProjectileTrigger` — so a single source entity only re-fires its
impact spawn every `cooldownSeconds`, **per source entity** (the semantics the
user chose). `0` = fire on every hit (current behavior, the default).

The cooldown gates **only the impact spawn** (the impact AOE / impact projectile
burst). Direct damage, hit VFX, pierce, and the projectile contact-gate are
untouched — a piercing projectile still damages every target, it just does not
spawn a new impact effect on every one of them.

## Key architectural fact (reuse-first)

Both impact triggers already converge on **one** ECS slot per source:
`OnHitSpawnRef` (`ProjectileHitPayload.OnHitSpawn` for a projectile source,
`AoeHitSpawnComponent.OnHitSpawn` for an AOE source), discriminated by
`IntervalChildKind`. `SkillDriver.BuildOnHitSpawnRef` picks at most one of the
compiled impact defs. And slot adjacency is `i -> i+2`, so a `SkillSetSlot` has
**at most one** outgoing trigger — the two impact triggers can never both wire the
same source.

=> A **single cooldown scalar** on `OnHitSpawnRef` covers both triggers with no
ambiguity and no new parallel data path. We extend the existing on-hit-spawn
concept rather than adding a second mechanism.

## Data model (three tiers, matching the existing config/state split)

| Tier | Where | Field | Lifetime |
|---|---|---|---|
| Authored config | `OnImpactAoeTrigger`, `OnImpactProjectileTrigger` (SOs) | `float cooldownSeconds` | edit-time |
| Compiled config | `RuntimeProjectileDefinition` / `RuntimeAoeDefinition` `OnHitSpawnCooldownSeconds`, then `OnHitSpawnRef.CooldownSeconds` | recompile; **content-hashed into the spawn template** | equip-time |
| Hot per-entity state | `ProjectileHitComponent` / `AoeHitSpawnComponent` `OnHitSpawnCooldownRemaining` | mutable; **reset to 0 on spawn/reuse** | per instance |

`OnHitSpawnRef.CooldownSeconds` is authored config identical for every instance
of a compiled trigger, so it correctly belongs in the content-hashed template
(two builds differing only in impact cooldown get distinct `TemplateKey`s — same
as `RepeatHitCooldown` and other behavior fields already do). The mutable
countdown is per-entity, matching how `ProjectileHitComponent.PierceRemaining`
and `AoeHitGateComponent.Remaining` already mix config + state on the same
component.

## Runtime behavior

- **Gate (at emission).** In `ProjectileCollisionJob` and
  `AoeCollisionCore.EmitHit`, before emitting the on-hit spawn event:
  `ready = OnHitSpawn.CooldownSeconds <= 0 || OnHitSpawnCooldownRemaining <= 0`.
  On emit, if `CooldownSeconds > 0` set `OnHitSpawnCooldownRemaining = CooldownSeconds`.
  Because the remaining is set immediately and only decremented next frame, a
  piercing projectile / lingering-AOE pass that hits many targets in one frame
  spawns the impact effect **once**, then is gated for the rest of the pass —
  exactly per-source-entity semantics.
- **Tick (per frame).**
  - Projectile: decrement `ProjectileHitComponent.OnHitSpawnCooldownRemaining`
    in `ProjectileContactGateJob` — already the per-projectile hit-cooldown
    ticker, runs every frame `[UpdateBefore(ProjectileCollisionSystem)]`.
  - AOE: decrement `AoeHitSpawnComponent.OnHitSpawnCooldownRemaining` at the top
    of `LingeringAoeCollisionJob.Execute` (runs every frame, *before* the
    `hitGate.Remaining` early-return). Impact (pulse) AOEs deactivate in a single
    pass so they need no per-frame tick; they still share `EmitHit`, so the
    impact-AOE job passes the ref too (harmless: remaining starts at 0, fires
    once, entity dies).

## Constraints & invariants respected

- **Concurrency** (`ProjectileCollisionSystem`, `*AoeCollisionSystem`): each
  entity is owned by one worker in `ScheduleParallel`, so mutating its own
  `*HitComponent` is safe. `AoeHitSpawnComponent` is **not** enableable, so
  taking it as a data `ref` does not hit the ref+EnabledRef UB from
  `[[reference_ijob_ref_plus_enabledref]]`. (source: those system files)
- **Explicit-query enableable gotcha** `[[reference_ijobentity_explicit_query_enableable]]`:
  the AOE systems build **explicit** `EntityQuery`s. `AoeHitSpawnComponent` is
  already listed (`WithAll`); changing the `Execute` param from `in` to `ref`
  needs it listed as **RW** (`WithAllRW<AoeHitSpawnComponent>()` in
  `LingeringAoeCollisionSystem.OnCreate`; the impact query lists it via
  `.WithAll` — switch to RW there too since its `Execute` now writes it). The
  projectile system uses `ProjectileHitComponent` as `ReadWrite` already.
- **Template hashing** (`spawn-template-registry.md`): `OnHitSpawnRef` is
  embedded in the content-hashed `ProjectileSpawnCommand`/`AoeSpawnCommand`.
  Adding a field is picked up automatically **iff** the hash covers the full
  struct — verify in `CombatRoot.RegisterSpawnTemplate` / `RegisterTimedSpawnTemplate`.
- **Pool reuse** (`ecs-vs-gameobject...`, spawn-apply systems): hot state resets
  on cold-create and reuse. Seed `OnHitSpawnCooldownRemaining = 0` so a reused
  entity can fire immediately.
- **Zero-cooldown = current behavior**: `CooldownSeconds <= 0` makes `ready`
  always true and never sets remaining — byte-for-byte the existing emit path.

## Mechanisms reused vs. introduced

- **Reused**: the single `OnHitSpawnRef` slot; the config/state tier split; the
  `CooldownRemaining` decrement idiom (`ProjectileContactGateJob`,
  `TimedSpawnStateComponent`, `AoeHitGateComponent`); the existing spawn-apply
  reset points.
- **Introduced**: two scalar fields (`CooldownSeconds` on the ref,
  `OnHitSpawnCooldownRemaining` per entity) and one compiled scalar on the host
  runtime defs. **No new component, system, event, or data path.**

## Minimal/additive vs. refactor comparison

- **Additive (chosen)**: extend `OnHitSpawnRef` + the two hit components with a
  scalar each. Data flow: unchanged pipeline, one extra field along the existing
  on-hit-spawn path. New types: none. Copies/translations: none. Long-term cost:
  negligible — the fields live exactly where the concept already lives.
- **Refactor alternative**: split on-hit-spawn config into a dedicated
  `OnHitSpawnState` component with its own tick system. Data flow: a second
  component + system for a concept the collision job already owns inline.
  Removes nothing; adds a system and a query. Long-term benefit: none here.
- **Decision: additive.** The concept (on-hit spawn) already has a single owner
  and a single slot; adding a cooldown scalar to that slot *is* the
  single-source-of-truth choice. A separate component would be the parallel path
  the workflow warns against.

## Design validation

- Per-source-entity across simultaneous hits: ✔ remaining set on first emit,
  gates the rest of the same-frame pass.
- Independent of direct damage: ✔ gate wraps only the spawn-event branches.
- Lingering AOE (repeat-hit) rate-limits correctly: ✔ every-frame decrement in
  `LingeringAoeCollisionJob` before the tick early-return; `EmitHit` gate fires
  at most one impact effect per cooldown window even across many ticks/targets.
- Pulse AOE / single-hit projectile: ✔ fire once then die; cooldown never
  observable, behavior identical to today.
- `OnAoeHitSpawnTrigger` (not an impact trigger) keeps `cooldown = 0`: ✔ its
  compiler branch never sets `OnHitSpawnCooldownSeconds`. (It shares the
  `OnHitAoeSpawnDefinition` field with the AOE-source impact-AOE branch, so make
  sure the cooldown is set in the **impact** branch only.)

## Verification note

Harness cannot build/run Unity (`[[feedback_plan_then_verify_ecs]]`). Each task
lists the PlayMode assertion the user runs. Silent-failure class here: forgetting
the RW query change (writes get dropped) or the template-hash coverage (cooldown
ignored). Both are called out.

## Tasks

- `001-authoring-and-compile-flow.md` — trigger `cooldownSeconds` fields,
  `OnHitSpawnRef.CooldownSeconds`, host-def scalar, compiler + `BuildOnHitSpawnRef`.
- `002-per-entity-state-and-reset.md` — `OnHitSpawnCooldownRemaining` on both hit
  components + reset-on-reuse in the spawn-apply systems; make
  `AoeHitSpawnComponent` RW in the AOE queries.
- `003-emission-gate.md` — gate the on-hit-spawn emission in
  `ProjectileCollisionJob` and `AoeCollisionCore.EmitHit`.
- `004-per-frame-tick.md` — decrement in `ProjectileContactGateJob` and
  `LingeringAoeCollisionJob`.
- `005-docs-and-tests.md` — update `skill-system.md`; PlayMode test coverage.

## Open questions

- **Template-hash coverage** must be confirmed (task 002 acceptance) — if
  `RegisterSpawnTemplate` hashes a field subset rather than the whole struct,
  `OnHitSpawnRef.CooldownSeconds` needs adding to that hash explicitly.
