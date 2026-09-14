# Task Execution Packet

## Task
007-aim-oriented-nova.md

## Goal
Replace the shot-index-0-only velocity replacement (implemented by the now-superseded
task 004) with a full aim-oriented radial nova. On successful nearest-hostile
acquisition, the ENTIRE wave's velocities are computed from the acquired direction:
`direction(i) = Rotate(aimDirection, 360 degrees * i / count)`, `velocity(i) =
direction(i) * Speed`. Shot `i = 0` points exactly at the target; shots `1..count-1`
occupy the remaining equally-spaced nova slots. The existing stored
forward/side-spray/radial pattern (with its spread/jitter/RNG) is used ONLY when
acquisition fails or is disabled — it is no longer touched at all on success (no
partial computation, no RNG draws for the discarded pattern).

This is an edit to the same file task 004 already modified:
`Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs`. You are
replacing task 004's approach, not layering on top of it.

## Files Allowed To Modify
- Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs

## Files Allowed To Create
- None.

## Files Likely Needed For Reading
- Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs (full file —
  read it first; this packet's Step-By-Step section gives you the exact target
  state for every method that changes)
- Assets/Scripts/System/Combat/CombatTargetAcquisition.cs (unchanged since task 001;
  `TrySelectNthNearest` signature is unchanged)

## Behavior To Preserve
- `TargetSpatialHashSingleton.BuildHandle`/`ConsumerHandle` dependency wiring added
  by task 004 (the `hasTargetHash`/`targetSnapshot`/`expansionScheduleDependency`
  logic in `OnUpdate`, and the `HasTargetHash`/`TargetSnapshot` job fields) stays
  exactly as-is — this task only changes what happens once acquisition succeeds or
  fails inside `Execute()`, not how the hash singleton is read/synchronized.
- `[UpdateAfter(typeof(TargetSpatialHashSystem))]` class attribute stays.
- Disabled/no-target/missing-hash/zero-range/`CombatFaction.None`/coincident-target
  path must still fall through to the byte-for-byte original (pre-task-004,
  pre-task-007) pattern output — this is now the ONLY path that runs the stored
  forward/side-spray/radial pattern code.
- Wave count, projectile IDs (`ProjectileIdFor`), render Z striping, timed-spawn
  re-stamping, bounds computation, and discrete/continuous routing in `WriteCommand`
  are unaffected — only which `velocity` value is computed per shot changes.
- `Stamp`, `WriteCommand`, `SpreadAngle`, `IntervalWaveSeed`,
  `IntervalSideSprayVelocity`, `RadialDirection`, `ProjectileIdFor`, and `Rotate`
  keep their exact current bodies — reuse `Rotate` for the new aimed-nova math
  rather than writing new rotation code.

## Behavior To Change
1. Remove the `hasAim`/`aimedVelocity` parameters task 004 added to
   `CreateForwardPattern`, `CreateSideSprayPattern`, `CreateRadialPattern`, and
   `WriteForwardWave` — restore these four methods to taking no aim-related
   parameters at all (their bodies also lose the `if (i == 0 && hasAim) velocity =
   aimedVelocity;` / ternary override lines). These methods now run ONLY on the
   fallback path (acquisition failed/disabled) and must be pattern-pure again.
2. Rename `TryResolveAimedVelocity` to `TryResolveAimedDirection`: it now resolves
   and returns the normalized aim **direction** (not a final velocity), since the
   caller needs the direction to build the whole rotated nova, not just one shot's
   velocity. Same preconditions and fallback-triggering failure cases as before
   (`HasTargetHash`, `LaunchAimMode == NearestHostile`, `LaunchAimRange > 0f`,
   `Faction != None`, a hostile found, non-coincident target) — only the `out`
   value and its computation (`math.normalize(diff)` instead of `math.normalize(diff)
   * command.Speed`) change.
3. Add a new method `CreateAimedNovaPattern(in ProjectileSpawnCommand command, int
   count, float2 aimedDirection)` that writes every shot `i` in `[0, count)` with
   velocity `Rotate(aimedDirection, 360f * i / count) * command.Speed`, using the
   existing `Rotate(float2, float)` helper (degrees) unchanged. No RNG, no spread,
   no jitter — this path is unconditional per shot.
4. In `Execute()`, call `TryResolveAimedDirection` once per event, immediately
   after `Stamp` (same position task 004 had `TryResolveAimedVelocity`). If it
   succeeds, call `CreateAimedNovaPattern` and `continue` to the next event —
   skipping the root-cast check and the pattern `switch` entirely (acquisition
   success now overrides ALL pattern selection, including the root-cast
   forward-volley special case, per the plan's decision that successful acquisition
   "intentionally replaces normal forward/side-spray/radial pattern calculation for
   whole wave"; in practice root templates never carry `NearestHostile` per task
   003, so this ordering is a formality, not an observable behavior change for
   root). If it fails, fall through to the existing (now-restored, parameterless)
   root-cast check and pattern `switch` exactly as before task 004 ever existed.

## Relevant Global Context
- Superseding requirement (see `implementation-context.md`'s "Superseding
  Requirement" section): the whole nova is now oriented from the aimed direction,
  not just shot 0. RNG preservation for non-aimed shots (a task-004/005 concept) no
  longer applies, because on success NO shot uses the stored
  pattern/spread/jitter/RNG code at all — task 008 will remove the tests that
  asserted RNG-sequence preservation, since that guarantee no longer exists by
  design.
- `count = 1` naturally produces a single direct shot: `Rotate(aimedDirection, 360 *
  0 / 1) = Rotate(aimedDirection, 0) = aimedDirection` unchanged, so no special-case
  branch is needed in `CreateAimedNovaPattern` for `count <= 1`.
- Verified by hand: for `aimedDirection = (0, 1)` (up) and `count = 4`, `Rotate`'s
  existing rotation-matrix convention (`c*v.x - s*v.y, s*v.x + c*v.y`) produces
  `i=0` → up, `i=1` (90°) → left, `i=2` (180°) → down, `i=3` (270°) → right —
  matching task 008's acceptance criterion ("deterministic shot order is up, left,
  down, right") exactly. This confirms `Rotate` is the correct existing helper to
  reuse with no sign/winding adjustment.

## Dependencies Confirmed
- Tasks 001-006 complete (see `implementation-log.md`). Task 004's dependency-handle
  wiring in `OnUpdate` (confirmed present by direct file read at the start of this
  task) is being kept; only `Execute()`'s pattern-selection body and four
  pattern-writing method signatures change.

## Step-By-Step Instructions
1. Read the current file in full to confirm it still matches the state described
   above (task 004's output).
2. In `Execute()`, replace:
   ```csharp
   Stamp(ref command, in evt);
   bool hasAim = TryResolveAimedVelocity(in command, out float2 aimedVelocity);
   int count = math.max(1, command.Count);

   // Root casts always use forward-volley behavior. Spawn patterns describe child waves only.
   if (command.DeterministicIdTickIndex <= 0)
   {
       CreateForwardPattern(in command, count, hasAim, aimedVelocity);
       continue;
   }

   switch (command.SpawnPatternType)
   {
       case ProjectileChildSpawnPatternType.SideSpray:
           var waveRng = new Random(IntervalWaveSeed(in command));
           CreateSideSprayPattern(in command, count, ref waveRng, hasAim, aimedVelocity);
           break;

       case ProjectileChildSpawnPatternType.Radial:
           CreateRadialPattern(in command, count, hasAim, aimedVelocity);
           break;

       case ProjectileChildSpawnPatternType.Forward:
       default:
           CreateForwardPattern(in command, count, hasAim, aimedVelocity);
           break;
   }
   ```
   with:
   ```csharp
   Stamp(ref command, in evt);
   int count = math.max(1, command.Count);

   if (TryResolveAimedDirection(in command, out float2 aimedDirection))
   {
       CreateAimedNovaPattern(in command, count, aimedDirection);
       continue;
   }

   // Root casts always use forward-volley behavior. Spawn patterns describe child waves only.
   if (command.DeterministicIdTickIndex <= 0)
   {
       CreateForwardPattern(in command, count);
       continue;
   }

   switch (command.SpawnPatternType)
   {
       case ProjectileChildSpawnPatternType.SideSpray:
           var waveRng = new Random(IntervalWaveSeed(in command));
           CreateSideSprayPattern(in command, count, ref waveRng);
           break;

       case ProjectileChildSpawnPatternType.Radial:
           CreateRadialPattern(in command, count);
           break;

       case ProjectileChildSpawnPatternType.Forward:
       default:
           CreateForwardPattern(in command, count);
           break;
   }
   ```
3. Replace the `TryResolveAimedVelocity` method with:
   ```csharp
   private bool TryResolveAimedDirection(in ProjectileSpawnCommand command, out float2 aimedDirection)
   {
       aimedDirection = default;
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

       aimedDirection = math.normalize(diff);
       return true;
   }
   ```
4. Add a new method (place it near `TryResolveAimedDirection`):
   ```csharp
   private void CreateAimedNovaPattern(in ProjectileSpawnCommand command, int count, float2 aimedDirection)
   {
       for (int i = 0; i < count; i++)
       {
           int id = ProjectileIdFor(in command, i);
           float2 direction = Rotate(aimedDirection, 360f * i / count);
           WriteCommand(in command, id, direction * command.Speed);
       }
   }
   ```
5. Restore `CreateSideSprayPattern`, `CreateRadialPattern`, `CreateForwardPattern`,
   and `WriteForwardWave` to their pre-task-004 parameterless-aim signatures/bodies:
   ```csharp
   private void CreateSideSprayPattern(
       in ProjectileSpawnCommand command,
       int count,
       ref Random waveRng)
   {
       for (int i = 0; i < count; i++)
       {
           int id = ProjectileIdFor(in command, i);
           float2 velocity = IntervalSideSprayVelocity(in command, i, ref waveRng);
           WriteCommand(in command, id, velocity);
       }
   }

   private void CreateRadialPattern(in ProjectileSpawnCommand command, int count)
   {
       for (int i = 0; i < count; i++)
       {
           int id = ProjectileIdFor(in command, i);
           float2 velocity = RadialDirection(i, count) * command.Speed;
           WriteCommand(in command, id, velocity);
       }
   }

   private void CreateForwardPattern(
       in ProjectileSpawnCommand command,
       int count)
   {
       if (count <= 1)
       {
           WriteCommand(in command, ProjectileIdFor(in command, 0), command.BaseDirection * command.Speed);
           return;
       }

       var spreadRng = new Random(command.JitterSeed != 0 ? command.JitterSeed : 1u);
       WriteForwardWave(in command, count, ref spreadRng);
   }
   ```
   and:
   ```csharp
   private void WriteForwardWave(in ProjectileSpawnCommand command, int count, ref Random rng)
   {
       for (int i = 0; i < count; i++)
       {
           float angle = SpreadAngle(command.SpreadDegrees, i, count);
           if (command.JitterDegrees > 0f)
           {
               angle += rng.NextFloat(-command.JitterDegrees, command.JitterDegrees);
           }

           int id = ProjectileIdFor(in command, i);
           WriteCommand(in command, id, Rotate(command.BaseDirection, angle) * command.Speed);
       }
   }
   ```
6. Do not touch `Stamp`, `WriteCommand`, `SpreadAngle`, `IntervalWaveSeed`,
   `IntervalSideSprayVelocity`, `RadialDirection`, `ProjectileIdFor`, `Rotate`, the
   `OnCreate`/`OnDestroy`/`OnUpdate` methods, the class attributes, or
   `ProjectileSpawnEventSingleton`.

## Acceptance Criteria
- Exactly radial slot `0` has zero offset from acquired direction.
- Other slots are spaced by exactly `360/count` around acquired direction.
- Whole nova rotates when target direction rotates.
- Projectile count, IDs, render Z, payload, timed child state, and lane routing
  stay unchanged.
- Discrete and continuous lanes receive identical aimed nova behavior.
- No target/disabled/missing hash/invalid faction/non-positive range/coincident
  target retains existing normal pattern byte-for-byte.
- No homing state, target entity state, component, archetype, pool, structural
  change, or allocation added.

## Validation Required
- Static/search-based: grep the file for `hasAim`/`aimedVelocity` to confirm zero
  remaining references (fully removed, not just renamed).
- Code review: hand-trace `count = 4`, `aimedDirection = (0, 1)` through
  `CreateAimedNovaPattern`/`Rotate` and confirm the four resulting directions are
  up, left, down, right (matching task 008's test expectation) — see this packet's
  "Relevant Global Context" section for the worked example.
- Confirm the fallback path (`TryResolveAimedDirection` returns `false`) reaches
  code identical to the file's state before task 004 ever ran (compare against the
  method bodies given in this packet's Step 5).
- Report whether a Unity/Burst compile check is possible; if not runnable, state so
  explicitly. Do not run Unity tests — task 008 (separate, later) replaces the
  now-obsolete PlayMode test coverage.

## Hard Boundaries
- Do not modify files outside the allowed list.
- Do not change architecture (no new system, no new component, no new event field).
- Do not introduce new abstractions beyond the one new `CreateAimedNovaPattern`
  method explicitly described above.
- Do not combine this task with task 008 (no test changes) or task 009 (no doc
  changes).
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
- Do not hand-edit Unity `.meta` files.
