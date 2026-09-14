# Task Execution Packet

## Task
004-expand-aimed-projectile-waves.md

## Goal
In `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs`, make the
expansion job acquire a nearest-hostile target once per event/wave (when the
command's `LaunchAimMode == NearestHostile` and preconditions hold), and redirect
**only deterministic shot index 0**'s velocity to point exactly at that target
(`normalize(targetPosition - command.Position) * command.Speed`), leaving every
other shot's velocity, RNG consumption, IDs, and routing completely unchanged. This
is the core behavioral task; tasks 001-003 (already complete) only prepared the data
(renamed acquisition helper, added authoring fields, threaded policy into
`ProjectileSpawnCommand.LaunchAimMode`/`LaunchAimRange`).

This packet already investigated the exact job structure, the exact
producer/consumer handle pattern used by an existing consumer of the same singleton
(`TargetedResolveSystem`), and the exact `CombatTargetAcquisition` API. Follow the
step-by-step instructions closely rather than re-deriving the design.

## Files Allowed To Modify
- Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs

## Files Allowed To Create
- None.

## Files Likely Needed For Reading
- Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs (full file — the file you will edit; read it first)
- Assets/Scripts/System/Combat/CombatTargetAcquisition.cs (task 001 result: `Snapshot` struct, `TrySelectNthNearest(in Snapshot, float2 from, float radius, int rank, CombatFaction faction, int excludeKey, out Entity, out float2 position)`, `TargetKey`)
- Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs (the `TargetSpatialHashSingleton` struct: `AoeOccupiedCells`, `TargetEntities`, `TargetPositions`, `TargetShapes`, `TargetFactions`, `BuildHandle`, `ConsumerHandle`)
- Assets/Scripts/System/Targeted/TargetedResolveSystem.cs (READ-ONLY precedent — do not modify. Shows the exact pattern: `TargetSpatialHashSingleton hash = SystemAPI.GetSingleton<...>(); state.Dependency = JobHandle.CombineDependencies(state.Dependency, hash.BuildHandle); ... TargetSnapshot = new CombatTargetAcquisition.Snapshot(hash.TargetEntities.AsArray(), hash.TargetPositions.AsArray(), hash.TargetShapes.AsArray(), hash.TargetFactions.AsArray(), hash.AoeOccupiedCells) ... hashRw.ValueRW.ConsumerHandle = JobHandle.CombineDependencies(hashRw.ValueRW.ConsumerHandle, handle);` — this is `ISystem`/`SystemAPI` style; you are editing a `SystemBase`, so adapt using the `Dependency` property instead of `state.Dependency`, but the handle-combination logic is identical.)
- Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs (confirms exact `ProjectileSpawnCommand` field names: `Faction`, `Position`, `Speed`, `SeedContactGateTargetId`, `LaunchAimMode`, `LaunchAimRange` — the last two added by task 003, already present).

## Behavior To Preserve
- Wave count unchanged; no new entity, no new component, no new system, no allocation beyond what's already `Allocator.Temp`/`TempJob` in this job.
- Every shot other than index 0 keeps its exact existing velocity, and the RNG sequence consumed for shots after index 0 must be bit-identical to launch-aim-disabled behavior — meaning any RNG draw that the existing code performs for shot index 0 (e.g. `IntervalSideSprayVelocity`'s `rng.NextFloat`, or forward-wave jitter's `rng.NextFloat`) must still execute even when shot 0's velocity is going to be overwritten.
- `RadialDirection`/pattern math for shot 0 still computed and then discarded on override (or simply not used for shot 0 when aim succeeds — radial has no RNG, so either approach preserves determinism; prefer overriding after unconditional computation for consistency/readability across all three pattern methods, but a ternary is acceptable for the RNG-free radial case specifically).
- `ProjectileIdFor`, render Z striping, timed-spawn re-stamping, bounds computation, and discrete/continuous routing in `WriteCommand` are completely untouched — only the `velocity` value passed into `WriteCommand` for shot index 0 changes.
- Disabled policy (`LaunchAimMode != NearestHostile`), non-positive `LaunchAimRange`, `Faction == CombatFaction.None`, missing `TargetSpatialHashSingleton`, no hostile found, or target position coincident with `command.Position` (near-zero direction) must all fall through to the existing unmodified pattern-generation code path — same output as before this task existed.
- Root casts (`command.DeterministicIdTickIndex <= 0`) are unaffected in practice because `LaunchAimMode` is always `None` for root-compiled templates (guaranteed by task 003) — no special-case root skip is needed beyond the existing mode gate.

## Behavior To Change
- Once per event (not per shot), after `Stamp(ref command, in evt)` and before pattern dispatch, attempt acquisition via `CombatTargetAcquisition.TrySelectNthNearest` using: `from = command.Position`, `radius = command.LaunchAimRange`, `rank = 0`, `faction = command.Faction`, `excludeKey = command.SeedContactGateTargetId` (this is the trigger's `ContactGateSeedTargetId`, already in the same key-space as `CombatTargetAcquisition.TargetKey` — confirmed by reading `ProjectileDiscreteCollisionSystem.TargetKey`, which uses the identical algorithm).
- On success, compute `aimedVelocity = math.normalize(targetPosition - command.Position) * command.Speed` (guarded by a minimum length-squared check to avoid zero-vector normalization), and thread a `(bool hasAim, float2 aimedVelocity)` pair into the three pattern-writing methods so shot index 0's velocity is replaced with `aimedVelocity`.
- Add a job dependency on `TargetSpatialHashSingleton.BuildHandle` before scheduling, and publish the job's handle into `TargetSpatialHashSingleton.ConsumerHandle` after scheduling — mirroring `TargetedResolveSystem`'s pattern exactly, adapted to `SystemBase`'s `Dependency` property. Guard all of this behind `SystemAPI.TryGetSingleton` succeeding (missing singleton = stripped test world = acquisition simply never succeeds, no exception).

## Relevant Global Context
- `ProjectileSpawnExpansionSystem` currently has no explicit ordering relationship with `TargetSpatialHashSystem`, but it is already implicitly ordered after it because `TargetedResolveSystem` declares both `[UpdateAfter(typeof(TargetSpatialHashSystem))]` and `[UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]`. For explicitness and robustness (this plan's constraint: "Jobs reading persistent hash containers must depend on BuildHandle and publish their read handle into ConsumerHandle"), add an explicit `[UpdateAfter(typeof(PlayGround.System.Combat.Collision.Broadphase.TargetSpatialHashSystem))]` attribute to the `ProjectileSpawnExpansionSystem` class alongside its existing `[UpdateAfter]`/`[UpdateBefore]` attributes. This is a minor, local ordering-attribute addition consistent with the file's existing style — not a new system or architectural change.
- `CombatFaction` and `TargetSpatialHashSingleton`/`CombatTargetAcquisition` types live in namespaces not currently imported by this file (`PlayGround.System.Combat` for `CombatTargetAcquisition`; `PlayGround.System.Combat.Collision.Broadphase` for `TargetSpatialHashSingleton`/`TargetSpatialHashSystem`). `CombatFaction` is already available via the existing `PlayGround.System.Combat.Core` using.
- The job is `[BurstCompile] private struct ProjectileExpansionJob : IJob` — everything added must stay Burst/blittable-compatible. `CombatTargetAcquisition.Snapshot` is already Burst-compatible (used inside another `[BurstCompile]` job in `TargetedResolveSystem`).
- This system already completes `Dependency` synchronously earlier in `OnUpdate` (for unrelated main-thread event gathering via `EntityManager`) — that earlier `Dependency.Complete()` call is NOT the same as the hash's `BuildHandle`; you must still separately combine `Dependency` with `hash.BuildHandle` right before scheduling `ProjectileExpansionJob`, exactly as `TargetedResolveSystem` does with `state.Dependency`.

## Dependencies Confirmed
- Task 001 complete: `Assets/Scripts/System/Combat/CombatTargetAcquisition.cs` exists with `TrySelectNthNearest`/`Snapshot`/`TargetKey`.
- Task 003 complete: `ProjectileSpawnCommand` (in `ProjectileSpawnPipeline.cs`) has `LaunchAimMode` (`ProjectileLaunchAimMode`) and `LaunchAimRange` (`float`) fields, populated from the compiled `RuntimeProjectileDefinition` for trigger-reached projectile templates only; root templates carry `None`/`0f`.

## Step-By-Step Instructions

1. Open `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs`. Add two
   using directives near the top:
   ```csharp
   using PlayGround.System.Combat;
   using PlayGround.System.Combat.Collision.Broadphase;
   ```

2. Add an explicit ordering attribute to the class (alongside the existing
   `[UpdateAfter]`/`[UpdateBefore]` attributes on `ProjectileSpawnExpansionSystem`):
   ```csharp
   [UpdateAfter(typeof(TargetSpatialHashSystem))]
   ```

3. In `OnUpdate`, right before the line
   `Dependency = new ProjectileExpansionJob { ... }.Schedule(Dependency);`,
   resolve the target hash singleton and build the snapshot + scheduling dependency:
   ```csharp
   bool hasTargetHash = SystemAPI.TryGetSingleton(out TargetSpatialHashSingleton targetHash);
   CombatTargetAcquisition.Snapshot targetSnapshot = hasTargetHash
       ? new CombatTargetAcquisition.Snapshot(
           targetHash.TargetEntities.AsArray(),
           targetHash.TargetPositions.AsArray(),
           targetHash.TargetShapes.AsArray(),
           targetHash.TargetFactions.AsArray(),
           targetHash.AoeOccupiedCells)
       : default;
   JobHandle expansionScheduleDependency = hasTargetHash
       ? JobHandle.CombineDependencies(Dependency, targetHash.BuildHandle)
       : Dependency;
   ```
   Then change the job construction to schedule against `expansionScheduleDependency`
   and pass the new fields:
   ```csharp
   Dependency = new ProjectileExpansionJob
   {
       Events = events,
       Templates = templates.Map,
       DiscreteCommands = discreteCommands,
       ContinuousCommands = continuousCommands,
       HasTargetHash = hasTargetHash,
       TargetSnapshot = targetSnapshot
   }.Schedule(expansionScheduleDependency);
   ```
   Immediately after the existing `Dependency = events.Dispose(Dependency);` line (keep
   that line as-is), publish the consumer handle:
   ```csharp
   if (hasTargetHash)
   {
       RefRW<TargetSpatialHashSingleton> targetHashRw =
           SystemAPI.GetSingletonRW<TargetSpatialHashSingleton>();
       targetHashRw.ValueRW.ConsumerHandle =
           JobHandle.CombineDependencies(targetHashRw.ValueRW.ConsumerHandle, Dependency);
   }
   ```
   (The existing lines after this — `singleton.DiscreteCommands = discreteCommands;`,
   `singleton.ContinuousCommands = continuousCommands;`, `singleton.PendingHandle =
   Dependency;` — stay exactly as they are, after this new block.)

4. Add two fields to `ProjectileExpansionJob`:
   ```csharp
   public bool HasTargetHash;
   public CombatTargetAcquisition.Snapshot TargetSnapshot;
   ```
   (No `[ReadOnly]` attribute needed on these outer fields — `Snapshot`'s own fields
   are already individually marked `[ReadOnly]`, matching the precedent in
   `TargetedResolveSystem`'s `TargetedResolveJob.TargetSnapshot` field.)

5. Add a new private method to `ProjectileExpansionJob` (place it near `Stamp`):
   ```csharp
   private bool TryResolveAimedVelocity(in ProjectileSpawnCommand command, out float2 velocity)
   {
       velocity = default;
       if (!HasTargetHash
           || command.LaunchAimMode != ProjectileLaunchAimMode.NearestHostile
           || command.LaunchAimRange <= 0f
           || command.Faction == CombatFaction.None)
       {
           return false;
       }

       if (!CombatTargetAcquisition.TrySelectNthNearest(
               TargetSnapshot,
               command.Position,
               command.LaunchAimRange,
               0,
               command.Faction,
               command.SeedContactGateTargetId,
               out Entity _,
               out float2 targetPosition))
       {
           return false;
       }

       float2 diff = targetPosition - command.Position;
       if (math.lengthsq(diff) <= 0.0001f)
       {
           return false;
       }

       velocity = math.normalize(diff) * command.Speed;
       return true;
   }
   ```

6. In `Execute()`, right after `Stamp(ref command, in evt);` and before
   `int count = math.max(1, command.Count);` (order relative to `count` doesn't
   matter, but keep it before the `if (command.DeterministicIdTickIndex <= 0)`
   branch and before the `switch`), add:
   ```csharp
   bool hasAim = TryResolveAimedVelocity(in command, out float2 aimedVelocity);
   ```
   Then update the two call sites that currently call `CreateForwardPattern(in command, count);` (the early-return root-cast branch, and the `default`/`Forward` case in the switch) to
   `CreateForwardPattern(in command, count, hasAim, aimedVelocity);`, the
   `CreateSideSprayPattern(in command, count, ref waveRng);` call to
   `CreateSideSprayPattern(in command, count, ref waveRng, hasAim, aimedVelocity);`,
   and the `CreateRadialPattern(in command, count);` call to
   `CreateRadialPattern(in command, count, hasAim, aimedVelocity);`.

7. Update the four pattern-writing method signatures and bodies:

   ```csharp
   private void CreateSideSprayPattern(
       in ProjectileSpawnCommand command,
       int count,
       ref Random waveRng,
       bool hasAim,
       float2 aimedVelocity)
   {
       for (int i = 0; i < count; i++)
       {
           int id = ProjectileIdFor(in command, i);
           float2 velocity = IntervalSideSprayVelocity(in command, i, ref waveRng);
           if (i == 0 && hasAim)
           {
               velocity = aimedVelocity;
           }
           WriteCommand(in command, id, velocity);
       }
   }

   private void CreateRadialPattern(
       in ProjectileSpawnCommand command,
       int count,
       bool hasAim,
       float2 aimedVelocity)
   {
       for (int i = 0; i < count; i++)
       {
           int id = ProjectileIdFor(in command, i);
           float2 velocity = (i == 0 && hasAim)
               ? aimedVelocity
               : RadialDirection(i, count) * command.Speed;
           WriteCommand(in command, id, velocity);
       }
   }

   private void CreateForwardPattern(
       in ProjectileSpawnCommand command,
       int count,
       bool hasAim,
       float2 aimedVelocity)
   {
       if (count <= 1)
       {
           float2 velocity = hasAim ? aimedVelocity : command.BaseDirection * command.Speed;
           WriteCommand(in command, ProjectileIdFor(in command, 0), velocity);
           return;
       }

       var spreadRng = new Random(command.JitterSeed != 0 ? command.JitterSeed : 1u);
       WriteForwardWave(in command, count, ref spreadRng, hasAim, aimedVelocity);
   }
   ```

8. Update `WriteForwardWave`:
   ```csharp
   private void WriteForwardWave(
       in ProjectileSpawnCommand command,
       int count,
       ref Random rng,
       bool hasAim,
       float2 aimedVelocity)
   {
       for (int i = 0; i < count; i++)
       {
           float angle = SpreadAngle(command.SpreadDegrees, i, count);
           if (command.JitterDegrees > 0f)
           {
               angle += rng.NextFloat(-command.JitterDegrees, command.JitterDegrees);
           }

           int id = ProjectileIdFor(in command, i);
           float2 velocity = Rotate(command.BaseDirection, angle) * command.Speed;
           if (i == 0 && hasAim)
           {
               velocity = aimedVelocity;
           }
           WriteCommand(in command, id, velocity);
       }
   }
   ```
   Note the RNG draw (`rng.NextFloat`) still executes unconditionally for `i == 0`
   even though its result is discarded when `hasAim` is true — this is required to
   keep the RNG sequence for shots `1..count-1` identical to the disabled-policy case.

9. Do not touch `Stamp`, `WriteCommand`, `SpreadAngle`, `IntervalWaveSeed`,
   `IntervalSideSprayVelocity`, `RadialDirection`, `ProjectileIdFor`, or `Rotate` —
   only their call sites/signatures gain the two new trailing parameters where
   listed above; `IntervalSideSprayVelocity`, `RadialDirection`, `ProjectileIdFor`,
   and `Rotate` keep their existing signatures unchanged (they are called the same
   way as before, just the caller now optionally overrides the result for `i == 0`).

## Acceptance Criteria
- Enabled trigger wave redirects exactly deterministic shot index `0` at nearest hostile collision shape within range.
- Aimed shot uses exact normalized target-center direction times authored speed, without spread/jitter.
- Remaining shots retain normal forward/side-spray/radial nova velocities.
- Normal pattern math and random draws still execute for the aimed slot before velocity replacement, so later shots retain the same deterministic RNG sequence as the disabled policy.
- Wave count unchanged; aimed shot replaces one existing pattern slot.
- Disabled/no-target/missing-hash/zero-range/coincident-target path preserves existing direction and pattern exactly.
- Same-faction targets skipped (enforced inside `CombatTargetAcquisition.TrySelectNthNearest`, unchanged).
- Contact-gated seed target skipped; next nearest valid hostile selected (via `excludeKey = command.SeedContactGateTargetId`).
- Discrete and continuous waves receive same one-shot replacement before lane split (acquisition happens once per event, before any `WriteCommand`/routing call).
- Continuous projectile remains without tracking component and flies straight (untouched).
- Discrete homing behavior unchanged (untouched — `Tracking` fields not read/written by this change).
- No new entity state, system, structural change, managed read, or allocation.
- Hash build/consumer handles correctly protect persistent containers.

## Validation Required
- Static/search-based: grep the modified file for every call site of the four pattern methods to confirm all were updated with the new parameters and no call site was missed.
- Code review: trace through a hand example — e.g., count=3 side-spray with aim enabled — confirming shot 0's `rng.NextFloat` call still happens (so shots 1 and 2 consume the RNG in the same state as before) and only shot 0's final velocity differs.
- Confirm the file still only reads (never writes) `TargetSpatialHashSingleton`'s target arrays/`AoeOccupiedCells`, and only writes `ConsumerHandle` (never `BuildHandle` or the data arrays).
- Report whether a Unity/Burst compile check is possible; if not runnable, state so explicitly. Do not run Unity tests — task 005 (separate, later) adds the actual EditMode/PlayMode coverage for this behavior; user runs and exports XML per project policy.

## Hard Boundaries
- Do not modify files outside the allowed list except for imports/namespaces directly required by this task.
- Do not change architecture.
- Do not introduce new abstractions (no new struct/class to carry `hasAim`/`aimedVelocity` — plain parameters, matching this file's existing style of passing plain values through private methods).
- Do not combine this task with task 005 (no test files) or task 006 (no docs).
- Do not reopen index-level decisions (acquisition once per event before pattern expansion; only shot index 0 redirected; full pattern math still runs for shot 0 — all settled).
- Stop on architectural ambiguity.
- Do not hand-edit Unity `.meta` files.
