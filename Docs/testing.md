# Testing

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Summary

Use Unity Test Framework.

Test types:

- EditMode tests for pure runtime logic and data structures
- PlayMode tests for scene, prefab, Physics2D, input, and integration behavior

Rule:

- if behavior is wrong, test fails
- if prefab or scene wiring is invalid, setup fails immediately with a clear error

Tests should prove gameplay or runtime behavior, not just prove that something ran.

## Test Folders

Target folders:

- `Assets/Tests/EditMode/`
- `Assets/Tests/PlayMode/`
- `Assets/Tests/Fixtures/`

Test-only scenes and prefabs belong under `Assets/Tests/`, not normal runtime folders.

## Keep Tests Out Of Runtime Logic

Do not add production flags, metadata, hidden counters, or debug breadcrumbs only for tests.

Preferred observability:

- public runtime methods
- real gameplay effects such as damage, despawn, state, or hit callback payload
- test-owned listener components
- diagnostic APIs that are useful outside tests too

## First Tests To Port

EditMode:

- `StateMachineCore` transitions and callbacks
- projectile volley angle builder
- projectile movement and lifetime systems
- projectile collision shape math
- projectile tracking/reacquire behavior
- projectile pierce/contact gate behavior
- projectile child entity creation and payload forwarding behavior
- AOE pulse hit rules
- AOE lingering tick and re-entry rules
- mob state transition rules
- damage snapshot forwarding

PlayMode:

- player movement and dash basics
- camera follows and zooms
- play area wall blocks player and mobs
- spawn point instantiates mobs and respects cap
- mob detects player and chases
- mob damage, hurt, recovery, and soft death
- player projectile hits mob
- mob projectile hits player
- projectile target masks filter hits
- piercing projectile can repeat-hit after cooldown
- projectile children carry child-specific hit payloads
- player AOE hits mobs only
- mob AOE hits player only
- stack-triggered projectile explosion

## Running Tests

Use Unity Test Runner in editor for normal local work.

For command line, use Unity in headless batch mode with project path and test
platform. This should be the default for agent-driven runs so the visible editor
does not need to be opened or interacted with.

Do not pass `-quit` for `-runTests`. In this Unity/Test Framework version, the
test runner exits the editor process itself after writing results; adding `-quit`
can shut Unity down before result XML is produced.

EditMode example:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Unity.exe' -automated -runTests -batchmode -nographics -projectPath . -testPlatform EditMode -testResults Logs\TestResults-EditMode.xml -logFile Logs\EditModeTests.log
```

PlayMode example:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Unity.exe' -automated -runTests -batchmode -nographics -projectPath . -testPlatform PlayMode -testResults Logs\TestResults-PlayMode.xml -logFile Logs\PlayModeTests.log
```

For non-test editor automation, such as scene or prefab builders, use headless
batch mode and include `-quit` because the called editor method owns the work:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Unity.exe' -batchmode -nographics -quit -projectPath . -executeMethod PlayGround.Editor.BareMinimumPrototypeBuilder.BuildBareMinimumPrototype -logFile Logs\BareMinimumBuilder.log
```

Use the installed Unity path on this machine if it differs.

## Writing Smoke Tests

Do not replicate low-level implementation details in tests.

Write assertions:

- setup the same gameplay condition
- run enough frames
- assert the real expected result
- fail clearly when result is wrong
