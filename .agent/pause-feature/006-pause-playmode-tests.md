# 006 — Pause Tests

## Goal

Prove the pause behaviour that matters, through public runtime APIs and real
gameplay effects. No production-only flags or counters added for tests
(`Docs/testing.md:32-41`).

## EditMode

`Assets/Tests/EditMode/ContinuousStreamBehaviourEditModeTests.cs` (owned by task
002, listed here for the full picture):

- A saturated accumulator plus `Tick(sink, 0f)` spawns nothing.
- Normal rate behaviour at positive dt is unchanged.

The runtime is a plain class behind `ISpawnSink`, so a test-owned fake sink is
enough — no scene, no pooling.

## PlayMode

New `Assets/Tests/PlayMode/PauseFeaturePlayModeTests.cs`. Follow the setup shape
already used by `MobSpawnControllerPlayModeTests.cs` and
`ProjectileSpawnPipelineTests.cs`.

1. **Clock stops.** Pause, run frames, assert `Time.timeScale == 0`,
   `AudioListener.pause == true`, and that the ECS world's `Time.ElapsedTime`
   does not advance across those frames. This is the invariant the whole feature
   rests on (I2) and it is the one that will break silently if the world is ever
   moved off the default player loop.

2. **No casts leak.** With a ready `SkillDriver` and `fireHeld` true, pause and
   run many frames; assert the active projectile count is unchanged. Run the
   same setup unpaused for one frame to prove the fixture would have fired —
   otherwise the test passes for the wrong reason.

3. **Projectiles hold position.** Spawn projectiles, record positions, pause,
   run frames, assert positions are identical. Resume and assert they move
   again.

4. **Loadout edits still resolve while paused.** Queue an edit via
   `TryQueueEdit` at `timeScale = 0`, run frames, assert `EditResolved` fired
   and the revision advanced. This is the guard-placement regression test for
   I4 — it fails if the dt guard in task 002 is ever moved above
   `ProcessPendingEdit`.

5. **Input block reasons compose.** Block for `SkillPicker`, then for `Paused`,
   clear `SkillPicker`, assert input is still blocked. Clear `Paused`, assert
   input is live.

6. **Resume restores.** After a pause/resume cycle, movement, casting, and mob
   spawning all work, and `Time.timeScale` is back to `1`.

Every test must restore `Time.timeScale = 1f` and `AudioListener.pause = false`
in teardown, including on failure. A leaked `timeScale = 0` will hang every
later PlayMode test in the run with no useful error.

## Manual Check

Not automatable, worth one pass during task 005's playtest: pause on a frame
with heavy AOE impacts, hold several seconds, resume, and watch whether a batch
of VFX appears late. Queued `SendEvent`s drain on the effect's next update,
which never comes at dt 0 (`CombatAoeVfxDispatcher.cs:231-236`).

## Test Commands

Agents must not run these. Export XML and review the result file before any
claim about test outcomes.

EditMode:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Unity.exe' -automated -runTests -batchmode -nographics -projectPath . -testPlatform EditMode -testResults "e:/UnityHub/projects/play-ground/TestResults/pause-feature-editmode-results.xml" -logFile Logs\PauseFeatureEditMode.log
```

PlayMode:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Unity.exe' -automated -runTests -batchmode -nographics -projectPath . -testPlatform PlayMode -testResults "e:/UnityHub/projects/play-ground/TestResults/pause-feature-playmode-results.xml" -logFile Logs\PauseFeaturePlayMode.log
```

## Acceptance Criteria

- All six PlayMode tests and both EditMode tests pass, verified from the
  exported XML.
- The existing EditMode and PlayMode suites still pass — in particular
  `SkillSlotStateEditModeTests` and `MobSpawnControllerPlayModeTests`, which
  cover the two classes task 002 modifies.
- No test leaves `Time.timeScale` or `AudioListener.pause` modified.

## Dependencies

001-005.

## Scope Estimate

Medium. One new PlayMode file, one new EditMode file.
