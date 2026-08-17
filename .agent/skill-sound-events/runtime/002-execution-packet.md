# Task Execution Packet

## Task
002-drain-and-selection.md

## Goal
Complete `AudioRoot` frame-batched drain: listener/distance cull, clip validation, per-clip copy grouping/cap, priority-plus-novelty ranking, capacity selection, cross-frame spacing, repeat falloff, pitch/delay playback, and production counters. Add hermetic EditMode selection-policy tests.

## Files Allowed To Modify
- `Assets/Scripts/System/Audio/AudioRoot.cs`
- `.agent/skill-sound-events/implementation-log.md`

## Files Allowed To Create
- `Assets/Tests/EditMode/AudioRootSelectionEditModeTests.cs`
- Direct Unity `.meta` file for new test if repository convention requires it.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Audio/SoundEvent.cs`
- `Assets/Scripts/System/Audio/AudioRoot.cs`
- `Assets/Tests/EditMode/AudioClipRegistryEditModeTests.cs`
- `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`
- `Assets/Scripts/System/Application/CombatApplyBridge.cs`
- `Docs/performance.md`
- `Docs/coding-standards.md`

## Behavior To Preserve
- Stable registry and `Clear` registration persistence from task 001.
- Singleton duplicate rejection, listener binding API, prewarmed/growing capped pool.
- No ECS/native lane and no producer implementation.
- Sim assembly references unchanged.

## Behavior To Change
- `LateUpdate` drains complete pending batch exactly once per frame.
- Whole-frame selection favors priority first and novel clip kinds before repeat copies at equal priority.
- Playback explicitly sets source volume, pitch, position, priority, rolloff distances, and repeat delay.
- Audio diagnostic counters and `StatsText()` become available.

## Relevant Global Context
- Producers enqueue during `Update` or earlier; `LateUpdate` is only consumer.
- Ranking method is internal on `AudioRoot`, takes `now`, `listenerPosition`, and `freeVoices`, and must touch no Unity object/`Time`/`Camera`/`AudioSource`.
- Reuse pending, grouping, selection scratch; no per-event allocations.
- Radius cull uses squared distance and event radius, with root default for `<= 0`.
- Missing/destroyed listener culls whole batch as distance, logs no per-frame errors, and always clears pending.
- `maxCopiesPerClipPerFrame` applies independent of voice count.
- Rank `Priority` descending, then copy index ascending; insertion order has no meaning.
- Same-clip spacing uses drain-start snapshot; same-frame duplicates may survive.
- Repeat volumes: `repeatVolumeFalloff ^ copyIndex`; first copy immediate, later copies randomly delayed.
- Jitter uses root's seeded `Unity.Mathematics.Random`.

## Dependencies Confirmed
- Task 001 complete: `SoundEvent`, `AudioRoot`, stable registry, listener binding, pending list, voice-pool vessel, and registry tests exist.
- `AudioManager`, `PlaySound`, and old namespace are absent.
- `AudioRoot.Enqueue` appends only; no pre-existing drain or producer.
- Sim asmdef hash still matches baseline.

## Step-By-Step Instructions
1. Add reusable grouping and selected-index scratch state on `AudioRoot`.
2. Add public diagnostic counter access needed by production/debug/tests and `StatsText()` with exact counter meanings from task.
3. Implement internal pure-ranking method parameterized by `now`, listener position, and free voices. It must validate/cull/group/cap/rank/select and fill reused selected indices without touching Unity objects.
4. Preserve same-frame duplicates by spacing against a drain-start snapshot only.
5. Implement `LateUpdate`: prune, resolve listener/free capacity, invoke policy, play survivors, update per-clip start times after batch, accumulate counters, and clear pending on every path.
6. Set volume/pitch/position/priority/delay explicitly for every voice. Account for pitch and delay in end time.
7. Add task-specified EditMode selection tests calling pure ranking directly; do not trigger playback.
8. Perform static/search/diff validation only. Do not run Unity tests or Unity runner.

## Acceptance Criteria
- Novel clip never loses to equal-priority repeat at capacity; priority still wins before novelty.
- `culledNovel == 0` when distinct kinds fit.
- Per-clip cap holds even with abundant voices.
- Per-event and default radii behave independently/correctly.
- Same-frame duplicates are not spacing-culled.
- Exact min(survivors, free voices) selected; repeat volume indexed correctly.
- Every early-out clears pending; missing listener counts distance and stays silent.
- Playback always assigns volume; first copy no delay; later copies delayed.
- Pure ranking has no Unity object/time dependencies and no per-event allocation.
- Counters distinguish accepted, distance, redundant, novel, spacing, rejected.

## Validation Required
- Static source inspection and searches for required/forbidden dependencies.
- `git diff --check` and Sim asmdef hash.
- Inspect all task-specified EditMode cases.
- Do not run Unity tests. Log user request: EditMode `AudioRootSelectionEditModeTests`; later combined export remains `Logs/TestResults-EditMode-SkillSoundEvents.xml`.
- Generated-project compilation is currently unavailable because unchanged Unity package code fails CS8168/CS8347 and narrow compile-check references missing `Microsoft.Unity.Analyzers.dll`; report this, do not claim compile passed.

## Hard Boundaries
- Do not modify files outside allowed list except imports directly required by this task.
- Do not change architecture or add separate selection/settings/request/counter types.
- Do not add ECS lane, native container, bridge, producer, or authoring chain.
- Do not implement task 003 or task 004.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
