# 007 — Docs and tests

**Scope:** medium. **Depends on:** 001-006.

## Doc updates

`Docs/reference/simulation/spawn-template-registry.md`:

- "Registry Concurrency Contract" (`:31-52`) — the claim "entries are never removed
  in v1" and "all writes happen before the tick" is now wrong. Rewrite to: template
  maps are written by managed pre-tick code and by `SpawnTemplateRefCountSystem` in
  `LateSimulationSystemGroup`, after every simulation job for the tick has completed.
  The maps remain immutable for the duration of the tick, which is what makes the
  `[ReadOnly]` concurrent reads safe — state that explicitly, since it is the reason
  the sweep sits where it does.
- "Registry rules" (`:230-239`) — replace "entries are never removed in v1" with the
  two-counter reclaim rule.
- Add a "Registry Lifetime" section: id is `(kind, Hash128)`; `OwnerCount` and
  `InstanceCount`; `Unregister` drops a managed claim and never erases; pinned entries
  from the ad-hoc `CombatRoot.Spawn` paths.
- Testing checklist (`:538-574`) — "Confirm the registry count is unchanged after a
  simulation tick" still holds. Add the reclaim cases below.

`Docs/contracts/combat-root-api.md:23` — document `UnregisterSpawnTemplate`.

`Docs/reference/game-logic/skill-system.md:820`, `:844`, `:881` — note that
`SkillDriver` releases the previous compile's keys after registering the new set.

## Test-world impact

003, 004 and 005 read the `SpawnTemplateRegistryState` singleton directly and throw
if it is missing. Any test world that runs the spawn-apply systems, the pool cleanup
system or the sweep system without a `CombatRoot`/`CombatScopeOwner` scope will now
fail. Audit and fix:

- `Assets/Tests/EditMode/CombatPoolCleanupSystemTests.cs`
- `Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs`
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileContinuousSimulationTests.cs`
- `Assets/Tests/PlayMode/TargetedSkillPlayModeTests.cs`
- `Assets/Tests/EditMode/TargetedSpawnPipelineEditModeTests.cs`

Prefer adding the scope singleton to the shared test fixture setup over weakening the
fail-loud read.

## New tests

Add to `Assets/Tests/PlayMode/AoePlayModeTests.cs` or a new
`SpawnTemplateRefCountTests.cs`:

1. Register the same template twice, unregister once → entry survives.
2. Register once, unregister once, no entities → entry gone after one frame.
3. Register, fire a projectile whose `OnHitSpawn` points at a child template,
   unregister the child → child entry survives while the projectile is in flight;
   the projectile's impact spawn still materializes.
4. Same as 3, then let the projectile expire and the pool trim run → child entry is
   reclaimed.
5. Reuse a pooled slot with a different template → old key count drops, new key rises,
   neither erased while owned.
6. Repeated `CombatRoot.Spawn(ProjectileSpawnRequest)` with identical content → one
   pinned entry, never erased by the sweep.
7. `SkillDriver` recompile with an unchanged loadout → registry entry count unchanged
   and no erase/re-add churn.
8. `SkillDriver` `OnDestroy` with in-flight projectiles → entries survive until the
   entities are gone.
9. Assert every `InstanceCount` returns to 0 after a full spawn/expire/trim cycle —
   the regression guard against the acquire derivation in 005 and the release sites in
   003/004 drifting apart.
10. Spawn a source with `HasTimedSpawner == 0` but a non-default
    `TimedSpawn.TemplateKey` on the command → no acquire for that key, because
    `WriteCommon` zeroes the component. Guards the one branch 005 has to mirror by
    hand.

## Test command

Agents do not run tests. Run and export XML:

```
Unity.exe -batchmode -projectPath "E:/UnityHub/projects/play-ground" -runTests -testPlatform PlayMode -testResults "E:/UnityHub/projects/play-ground/TestResults/spawn-template-refcount-results.xml" -quit
```

and for EditMode:

```
Unity.exe -batchmode -projectPath "E:/UnityHub/projects/play-ground" -runTests -testPlatform EditMode -testResults "E:/UnityHub/projects/play-ground/TestResults/spawn-template-refcount-editmode-results.xml" -quit
```

## Acceptance criteria

- The concurrency contract section no longer claims entries are never removed, and
  states why the LateSimulation sweep preserves the `[ReadOnly]` guarantee.
- All existing tests pass with no weakening of the fail-loud singleton reads.
- Test 9 passes — no count drift across a full lifecycle.
