# Task Execution Packet

## Task

002-pool-trim-job-popcount.md

## Goal

Use disabled-Active query masks to popcount pool chunks and enumerate only disabled entities for deletion.

## Files Allowed To Modify

- Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs

## Files Allowed To Create

- None

## Files Allowed To Delete

- None; delete the obsolete Active type-handle field and uses.

## Behavior To Preserve

- Calm-down gate stays unchanged.
- Threshold remains `activeCount >= (int)(count * ActiveThresholdPercent / 100f)`.
- LastDeletedCount and stats deletion accounting remain exact.
- Pool trim deletes disabled slots only.

## Behavior To Change

- Queries use WithDisabled<Active>() instead of unfiltered Active matching.
- Job derives active count by disabled-mask popcount and uses ChunkEntityEnumerator for deletion.

## Relevant Global Context

- Active is only enableable component in either query.
- Never read chunkEnabledMask when `useEnabledMask` is false; all chunk slots match in that case.
- No unsafe or new abstraction. Existing Unity.Mathematics and Unity.Burst.Intrinsics imports cover required APIs.
- Do not run Unity tests; static validation only.

## Dependencies Confirmed

- None; task 002 has no dependency. Existing cleanup system contains every targeted query/handle/job site.

## Step-By-Step Instructions

1. Shape both queries with WithDisabled<Active>(); remove IgnoreComponentEnabledState.
2. Remove _activeHandle, setup/update, and PoolTrimJob field/wiring.
3. Set disabledCount to popcount ULong0+ULong1 if masked, otherwise chunk.Count; derive activeCount.
4. Preserve threshold. Enumerate matching disabled slots using ChunkEntityEnumerator and destroy them.
5. Reword before/after comment to say disabled-slot count.

## Acceptance Criteria

- No chunk.Count loop or EnabledMask indexing in PoolTrimJob.
- useEnabledMask false branch present; math.countbits used.
- Destroy pass uses ChunkEntityEnumerator.
- No _activeHandle or job ActiveHandle; Burst remains.
- No calm-gate change or unsafe code.

## Validation Required

- Source review, static searches, `git diff --check`; user later runs supplied Unity XML commands.

## Hard Boundaries

- Only cleanup system edits.
- Do not add tests or modify other files; optional coverage is not requested.
