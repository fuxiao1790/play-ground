# 008 — Tests + docs

## Goal
Update tests for the flipped `TargetFaction` semantics and single-root wiring, and bring
the docs in line.

## Background — the semantic flip breaks "same-faction hits"
Collision tests currently spawn a `Player` projectile **and** create a `Player` target
([ProjectileCollisionSimulationTests.cs:281,323](../../Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs#L281),
[ProjectileTrackingSimulationTests.cs:222,280](../../Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs#L222)).
Under the old firing-faction model that matched; after 001/002/004 a `Player` projectile
**skips** a `Player` target. Tests that assert a hit/acquire must give the target the
**opposing** faction.

## Changes
1. **Collision/tracking/AOE simulation tests**
   ([ProjectileCollisionSimulationTests.cs](../../Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs),
   [ProjectileTrackingSimulationTests.cs](../../Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs),
   [AoeSimulationTests.cs](../../Assets/Tests/PlayMode/AoeSimulationTests.cs)):
   - Where a projectile/AOE under test is `Player`, create the target with `Mob`
     (`TargetFaction { Value = Mob }` and/or `CombatTargetProxy.Create(em, target, Mob)`).
   - Add at least one assertion that a **same-faction** target is **not** hit/acquired
     (new coverage for the skip).
2. **Target-mask tests — delete or rewrite as faction-skip.**
   `ProjectileTargetMaskFiltersHits`
   ([BareMinimumPrototypePlayModeTests.cs:174](../../Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs#L174))
   and the AoE mask cases test a filter that is being deleted (006). Do not try to make
   them pass — delete them, or replace with same-faction-skip coverage. Drop any other
   test references to `targetMask`/`ConfigureTargetBinding`.
3. **Pipeline/prototype tests**
   ([ProjectileSpawnPipelineTests.cs](../../Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs),
   [SpawnCommandUnificationTests.cs](../../Assets/Tests/PlayMode/SpawnCommandUnificationTests.cs),
   [BareMinimumPrototypePlayModeTests.cs](../../Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs),
   [AoePlayModeTests.cs](../../Assets/Tests/PlayMode/AoePlayModeTests.cs)):
   - Update root construction to a single `CombatRoot` and the new `Spawn(..., faction)`
     signatures; any `CombatRenderBatchId`/batch-id assertions use `renderId` (no
     `faction<<16`).
   - Add/keep a **cross-faction reuse** assertion: a `Mob` spawn reuses a disabled
     `Player` slot of the same render id (validates 005).
4. **Docs:**
   - [combat-bridge.md](../../Docs/layers/combat-bridge.md): "owns `CombatRoot` per
     firing faction" → one faction-agnostic root; faction is a per-spawn argument /
     per-entity field; "Must not treat `CombatScope` membership as faction" stays.
   - [combat-root-api.md](../../Docs/contracts/combat-root-api.md): spawn API takes a
     `CombatFaction`; drop "combat faction … binding" language implying a per-root faction.
   - [target-proxy.md](../../Docs/contracts/target-proxy.md): `TargetFaction` is the
     target's own allegiance; collision ignores same-faction.
   - [spawn-events-and-commands.md](../../Docs/contracts/spawn-events-and-commands.md):
     note reuse is faction-agnostic (batch id = render id).
   - Update [faction-overhaul/info.md](info.md) "documentation gaps" as resolved.

## Acceptance Criteria
- All PlayMode tests compile and pass.
- New coverage: same-faction target not hit; cross-faction reuse occurs.
- Docs no longer describe a per-faction root or firing-faction `TargetFaction`.

## Dependencies
001–007.

## Scope
Medium (test edits dominate).
