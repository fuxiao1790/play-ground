# Profiling Note

## Change Expected

Projectile, impact AOE, and lingering AOE spawn apply no longer use a serial
dead-slot reuse job with one shared claim cursor. Each domain now captures
disabled-slot chunks, builds worker lanes of command indices, and schedules an
`IJobParallelFor` over worker-owned chunk ranges. Reuse work can run on multiple
workers; only overflow cold-create remains on the main thread through ECB.

## Counters To Read

- `ProjectileSpawnApplySystem.Reuse`
- `ProjectileSpawnApplySystem.Cold`
- `ImpactAoeSpawnApplySystem.Reuse`
- `ImpactAoeSpawnApplySystem.Cold`
- `LingeringAoeSpawnApplySystem.Reuse`
- `LingeringAoeSpawnApplySystem.Cold`

The reuse and cold values should sum to the command count for the domain. A
non-zero cold count under forced imbalance is expected; repeat frames should
converge toward more reuse after the pool grows.

## Capture Status

Busy-scene profiler capture was not run in this pass because Unity batchmode
could not open the project while another Unity editor instance already had it
open. The focused PlayMode run failed before tests started with Unity's project
lock message. Compile validation did pass for runtime and playmode test
assemblies.

## Follow-Up Capture

After closing the existing editor instance, run the focused PlayMode tests and a
busy-scene profiler capture. Confirm the apply marker work appears under
parallel job execution instead of one single-worker reuse job, then compare
`*.Reuse` and `*.Cold` counters against the `.agent/idle-combat-system-cost/`
baseline if available.
