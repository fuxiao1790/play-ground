# AOE Spawn-State Unification (Problem #1)

## Summary

An AOE's spawn state — which enable-gates (`Active`, `AoeCollisionActiveTag`,
`CombatRenderActiveTag`, `TimedSpawnComponent`) are on — is **decided and applied in four
independent code paths**. This plan collapses the *decision* into one function,
`AoeSpawnApplyUtility.SpawnStateFor(cmd, isLingering)`, that all four paths apply. Purely
behavior-preserving; no new runtime behavior. It exists to remove the silent-desync class of
bug that has repeatedly walled the windup feature (every windup failure was "one enable bit set
wrong in one of the four sites"). It is prerequisite groundwork, not the windup itself.

This is problem #1 of three the user chose to fix one at a time (next: reduce the number of
activation gates; then a user-named problem). Each gets its own plan.

## The problem (grounded in current code)

Four materialization sites, two per archetype, each re-decides the enable bits via a different
mechanism:

| | impact | lingering |
|---|---|---|
| reuse (`EnabledMask[i] = bool`) | [AoeSpawnApplySystem.cs:210-213](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L210) | [:449-454](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L449) |
| cold-create (`ecb.SetComponentEnabled`) | `RecordImpactReset` [:481-484](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L481) | `RecordLingeringReset` [:502-512](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L502) |

Exact current decision (must be preserved byte-for-byte):
- `collision = NeedsCollision(cmd)` ([:541](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L541))
- **impact:** `Active = collision`, `Collision = collision`, `Render = collision` (no timed component)
- **lingering:** `Active = true`, `Collision = collision`, `Render = true`,
  `Timed = HasTimedSpawner(cmd)` ([:546](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L546))

There is no single source of truth for "given a command, what is the AOE's activation state."
Adding a lifecycle phase means editing all four in lockstep; a missed/incorrect bit is a **silent**
failure (no compile error; caught only if a test asserts that exact bit).

## Rationale

Extract the *decision* (which bits) into one pure function returning a small `SpawnState` value;
leave the *application* mechanism (mask vs ECB) alone, because those are two legitimately different
ECS APIs (chunk `EnabledMask` in a Burst `IJob` vs `EntityCommandBuffer` for cold-create) that
cannot share application code without inventing an awkward abstraction. Unifying the decision (the
error-prone part) while keeping two thin, mechanical adapters is the minimal change that removes
the desync class. Data-component writes are **out of scope** (they are value writes and rarely
desync; `WriteCommon` already shares the common ones — a later pass can revisit).

## Constraints & invariants the change must respect

- **Behavior-preserving.** The applied enable-states for every (archetype × path × command) must be
  identical to today. The decision table above is the contract. (code)
- **Burst compatibility.** `SpawnStateFor` and `SpawnState` are called inside the reuse `IJob`
  (`ImpactAoeSpawnJob` / `LingeringAoeSpawnJob`, `[BurstCompile]`). `SpawnState` must be a plain
  blittable `readonly struct` of `bool`s; `SpawnStateFor` must be a static method over pure
  helpers (`NeedsCollision`/`HasTimedSpawner`) — no managed state. ([:138](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L138), [:368](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L368)) (code)
- **`Active` is the occupancy gate.** The reuse dead-slot query is `WithDisabled<Active>`; a spawned
  entity must end with `Active` in the decided state (impact may be born `Active=false` when
  visual-only — that path must be preserved exactly). ([:50](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L50)) (code)
- **`Timed` only exists on lingering.** Impact archetype has no `TimedSpawnComponent`; `SpawnState.Timed`
  is meaningful only for lingering and is ignored by the impact paths. (code)
- **`HasTimedSpawner` also gates timed data writes**, not just the enable bit
  ([:444-449](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L444)/[:502-505](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L502)).
  `SpawnState.Timed == HasTimedSpawner(cmd)` for lingering, so those data writes may read `state.Timed`
  and stay identical. (code)

## Mechanisms reused vs. introduced

- Reused: `NeedsCollision`/`HasTimedSpawner` (unchanged), the existing reuse/ECB application sites
  (they now read from `SpawnState` instead of recomputing).
- Introduced: `AoeSpawnApplyUtility.SpawnState` (4-bool readonly struct) + `SpawnStateFor(cmd,
  isLingering)`. Justified: it *removes* complexity — one decision replaces four hand-written copies —
  rather than adding a parallel path. No new component, system, or runtime data path.

## Design validation

- *Behavior-preserving:* `SpawnStateFor` returns exactly the impact/lingering tuples listed above;
  each site assigns the same bits it assigns today. ✓ (verify by diffing the tuples)
- *Burst:* `SpawnState` is blittable bools; `SpawnStateFor` is static + pure. ✓
- *Occupancy / visual-only impact:* impact `Active = collision` preserved (visual-only ⇒ `Active=false`). ✓
- *Timed data writes:* gated on `state.Timed == HasTimedSpawner`. ✓

## Minimal/additive vs. refactor comparison

- **Minimal/additive (leave as-is):** four sites keep recomputing bits. New types: none. Long-term
  cost: every new phase edits four sites with silent-desync risk — the exact failure mode already hit.
- **Refactor (this plan):** one decision function, two mechanical adapters. Types changed: adds one
  small value struct, removes three duplicate decisions. Long-term benefit: a new phase edits
  `SpawnStateFor` in one place; the four sites can never disagree.
- **Decision: refactor.** Reason: this is one domain concept ("AOE spawn activation state") currently
  represented four times; collapsing to one source of truth is the default rule, with no migration
  blocker (behavior-preserving).

## Default decision rule

Four copies describe one concept (spawn activation state) → refactor to one source of truth. No
compatibility/migration reason blocks it.

## Task list

- [001](001-spawn-state-single-source.md) — Add `SpawnState` + `SpawnStateFor`; route all four
  materialization sites through it. Behavior-preserving.
- [002](002-spawn-state-regression-tests.md) — Ensure PlayMode tests assert the impact and lingering
  spawn enable-states via **both** paths (reuse and cold-create), so `SpawnStateFor` is guarded before
  any future phase edits it.

Order: 001 → 002 (002 may be written first if preferred, as a characterization test that must stay
green across 001).

## Verification

The harness cannot compile or run PlayMode tests here (Unity holds the project; `dotnet build` fails
in a Unity package). **The user runs the PlayMode AOE suite (`AoeSimulationTests`) after 001** as the
green checkpoint. 002 strengthens that guard. No change proceeds to problem #2 until 001+002 are green.

## Open questions / considerations

- **Data-write duplication** (reuse `WriteCommon`/array writes vs ECB `SetComponent` lists) is left
  as-is; it is lower-risk than enable-state and would need a mask-vs-ECB abstraction. Revisit only if
  problem #2 (gate reduction) makes it natural.
- After this lands, windup/any phase becomes: add a phase input to `SpawnStateFor` + one windup data
  field — one place, not four. That is deferred to the windup plan, not this one.
