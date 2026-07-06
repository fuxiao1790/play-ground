# 002 — Regression coverage for spawn activation state

## Goal
Guarantee `SpawnStateFor` is pinned by PlayMode tests before any future phase (windup, gate
reduction) edits it, so a desync becomes a red test instead of a silent gameplay bug. This is the
safety net that was missing every time windup walled.

## Why
The four materialization sites had no test asserting the full enable-tuple per path, so a wrong bit
passed CI silently. Characterization tests over `SpawnStateFor`'s observable output close that gap.

## Coverage to add / confirm (in `Assets/Tests/PlayMode/AoeSimulationTests.cs`)
For **both** the reuse path (spawn into a pre-existing dead slot) and the cold-create path (empty
pool), assert the spawned entity's enable-states:

1. **Impact, needs collision** (`DirectDamage`/stack/on-hit enabled): `Active` on, `AoeCollisionActiveTag`
   on, `CombatRenderActiveTag` on.
2. **Impact, visual-only** (none of the above): `Active` off, `AoeCollisionActiveTag` off,
   `CombatRenderActiveTag` off (born inert — preserves today's behavior).
3. **Lingering, no timed spawner:** `Active` on, `AoeCollisionActiveTag` = `NeedsCollision`,
   `CombatRenderActiveTag` on, `TimedSpawnComponent` off.
4. **Lingering, with timed spawner:** as above but `TimedSpawnComponent` on.

Prefer asserting through the real apply system (`MaterializeAoesOnly()` + the existing
`CreateDisabled*AoeSlot()` reuse helpers) so both the mask adapter and the ECB adapter are exercised.
Reuse existing helpers/patterns already in `AoeSimulationTests`.

## Acceptance criteria
- Tests exist for the 4 cases × 2 paths (reuse + cold-create) covering all four enable bits.
- They pass on the current base **and** across 001 (they are the behavior-preserving proof).
- **User runs the suite** and confirms green (harness cannot run PlayMode here).

## Scope / complexity
Low–medium. Straightforward assertions using existing spawn/reuse test helpers.

## Dependencies
Independent of 001 in authoring, but its purpose is to stay green across 001. Write/confirm it first
if you want a characterization baseline before refactoring.
