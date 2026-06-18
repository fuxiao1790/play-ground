# Task 017: Tests + validation (Increment 2)

## Goal
Migrate the existing suite to the Increment-2 architecture (`Active`, Event/Command names, proxy entities, Entity-keyed damage) and add new coverage for the reversed decisions. Run the full validation pass.

## Required Reading
- `../context/006-validation-strategy.md` (the full strategy + failure modes)
- `../context/002`/`003`/`004` for the shapes/flows under test

## Migrate existing tests
- `AoeSimulationTests.cs` — occupancy `AoeActiveTag` → `Active`; `AoeSpawnCommandData` → `AoeSpawnCommand`; targets become **proxy entities** (`AddTarget` creates a proxy with `TargetPosition`/`TargetCollisionShape`/`TargetFaction`/`TargetCompanion`); damage assertions move off `CombatDamageElement` to the finalized `NativeArray<DamageReplayEvent>` (expose a test hook or read the bridge's input array). No `CombatTargetElement`/`CombatHitFlushJob` setup.
- `ProjectileTrackingSimulationTests.cs`, `ProjectileSpawnPipelineTests.cs` — `Active`, command names, per-shape apply system list.
- `AoePlayModeTests.cs`, `BareMinimumPrototypePlayModeTests.cs`.
- EditMode `CritEditModeTests.cs` (crit roll unchanged in the bridge), `ProjectileAuthoringEditModeTests.cs`, `SkillValidationEditModeTests.cs` — renamed authoring Event types.

## New tests
1. **Generic `Active` reuse** (both domains): spawn → expire → respawn reuses the same entity via `WithDisabled<Active>`.
2. **Event/Command split + naming:** `Count==1` yields one entity; `ProjectileSpawnCommand` has no `Count` field (structural/compile assertion); no type named `...CommandData`.
3. **Per-shape apply:** basic projectiles never enter the child-spawner archetype and vice-versa; each apply reuses only its own shape's disabled slots; no apply system branches on a shape key.
4. **Proxy lifecycle:** target enable creates a proxy; `Update` push updates `TargetPosition`; leaving sim deletes the proxy before the next collision; collision reads the proxy.
5. **Entity-keyed damage:** a hit yields `DamageReplayEvent.TargetProxy == hit proxy`; dispatch resolves via `TargetCompanion` to the right `ICombatTarget`; atomic per-hit + group-by-proxy preserved.
6. **Proxy-deletion safety (§8.3):** a target dying during dispatch keeps its proxy until after dispatch; no event references a missing proxy.
7. **Next-tick spawn (R5):** impact-spawned entity does not act the same tick.
8. **Convert-elimination parity** (carry from Increment 1): impact-AoE golden values unchanged.

## Validation pass
- Compile clean after every prior task; full Test Runner green.
- Manual `Main.unity`: projectiles move/hit/damage; multi-shot counts; child spawns; impact AOEs/bursts; mob↔player damage **through proxy/companion**; death animation plays after proxy deletion; no NRE.
- Profiler: spawn markers on per-shape apply; cold-create exceptional; proxy create/delete one structural change per target enable/disable; no new per-frame managed allocations (companion read only at dispatch); re-check ~50k-projectile stress.

## Dependencies
011–016.

## Acceptance Criteria
- [ ] Existing suite migrated and green.
- [ ] All eight new tests added and green.
- [ ] Manual + profiler checks pass; stress target not regressed.

## Risk
Low–Medium — mostly test migration; the proxy/damage tests need the bare-world harness to create proxy entities (extend the `AoeSimulationTests` pattern).
