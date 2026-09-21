# Task Execution Packet

## Task
002-unify-hit-eligibility.md

## Goal
Add one static Burst-safe `CanHit(CombatFaction attacker, in TargetFaction target)`
helper and replace every direct same-faction comparison in collision,
acquisition, and tracking with a call to it, so selection and collision use
identical eligibility semantics everywhere.

## Files Allowed To Modify
- `Assets/Scripts/System/Targets/CombatTargetProxy.cs` (add the `CanHit` helper
  next to `TargetFaction`/`TargetFactionFilterMode`, which task 001 already put
  here)
- `Assets/Scripts/System/Combat/CombatTargetAcquisition.cs`
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileContinuousCollisionSystem.cs`
- `Assets/Scripts/System/Aoes/AoeCollisionCore.cs`
- `Assets/Scripts/System/Projectiles/ProjectileTrackingSystem.cs`
- `Assets/Scripts/System/Spawning/ExternalSpawnGateSystem.cs` (only if you
  rename `CombatTargetAcquisition.TryNearestHostile`; update its one call site)
- `Assets/Scripts/System/Targeted/TargetedResolveSystem.cs` (read for
  confirmation; only edit if a rename cascades here — it currently calls the
  already-generic `TrySelectNthNearest`, not `TryNearestHostile`)
- `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs` (same
  caveat — it also calls `TrySelectNthNearest`, not `TryNearestHostile`)

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading (do not modify)
- `Assets/Scripts/System/Core/CombatScope.cs` (`CombatFaction` enum)
- `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs`
  (confirms `TargetFactions` snapshot array is unchanged by task 001 — do not
  touch)

## Exact Current Comparison Sites (verified by orchestrator; use these line
anchors, but re-locate by content since line numbers may drift slightly)

1. `CombatTargetAcquisition.cs` (`TrySelectNthNearest`, inner loop):
   ```csharp
   if (targetIndex < 0
       || targetIndex >= snapshot.TargetEntities.Length
       || snapshot.TargetFactions[targetIndex].Value == faction
       || TargetKey(snapshot.TargetEntities[targetIndex]) == excludeKey)
   {
       continue;
   }
   ```
   Also public API `TryNearestHostile(...)` (thin wrapper calling
   `TrySelectNthNearest(..., rank: 0, ...)`) — rename to `TryNearestEligible`
   per task item 4; its only caller is
   `ExternalSpawnGateSystem.cs:169` (`CombatTargetAcquisition.TryNearestHostile(...)`).

2. `ProjectileDiscreteCollisionSystem.cs` (`ProjectileCollisionJob.Execute`,
   inner loop):
   ```csharp
   if (TargetFactions[targetIdx].Value == identity.Faction)
   {
       continue;
   }
   ```

3. `ProjectileContinuousCollisionSystem.cs` (`ProjectileContinuousCollisionJob.Execute`,
   inner loop): identical pattern —
   `if (TargetFactions[targetIndex].Value == identity.Faction) { continue; }`

4. `AoeCollisionCore.cs` (`RunCollision`, inner loop):
   ```csharp
   if (targetFactions[i].Value == identity.Faction)
       continue;
   ```

5. `ProjectileTrackingSystem.cs` (`ProjectileTrackingJob`):
   - `TryRefreshTrackedTarget`, cached-index branch:
     `&& TargetFactions[cachedIndex].Value != identity.Faction)` (part of a
     larger `&&` chain — this is the ELIGIBLE case, i.e. currently means
     "keep tracking if different faction").
   - `TryRefreshTrackedTarget`, mapped-index branch: identical
     `&& TargetFactions[mappedIndex].Value != identity.Faction)` pattern.
   - `TrySelectRandomTargetInCell`:
     ```csharp
     if (TargetFactions[targetIndex].Value == projectileFaction)
     {
         continue;
     }
     ```

## Behavior To Preserve
- Hostile-default targets (`FilterMode == HostileOnly`, the task-001 default)
  must behave in every one of the 5 call sites above exactly as the current
  `== identity.Faction` / `!= identity.Faction` comparisons behave today —
  this is a drop-in replacement, not a semantic change for existing content.
- Preserve every other gate/ordering exactly: invalid/expired-source
  early-outs, contact/repeat-hit gates, broadphase/narrowphase geometry,
  pierce/candidate caps, deterministic tie-breaking, and "spawn/VFX only after
  an eligible contact" — you are only swapping the faction predicate itself,
  never reordering or removing any surrounding check.
- Do not touch `CombatHitEvent` construction, VFX emission, pierce/lifetime
  logic, contact-gate logic, or any candidate-selection/sorting logic beyond
  the one predicate.
- `ProjectileLaunchAimMode.NearestHostile` (the serialized enum value in
  `ProjectileSpawnExpansionSystem.cs`) must NOT be renamed — task explicitly
  preserves it to avoid an authored-content migration. You may update the
  comment near it to say "nearest eligible target under target policy" without
  renaming the enum member itself.
- `TrySelectNthNearest`/`TryNearestHostile` in `CombatTargetAcquisition.cs` are
  Burst-compiled (`[BurstCompile]` on the class) and called from job code in
  `TargetedResolveSystem.cs` and `ProjectileSpawnExpansionSystem.cs` — keep the
  helper allocation-free and blittable.

## Behavior To Change
- Add, in `CombatTargetProxy.cs`, next to `TargetFaction`/
  `TargetFactionFilterMode`:
  ```csharp
  public static bool CanHit(CombatFaction attacker, in TargetFaction target)
  {
      if (attacker == CombatFaction.None)
      {
          return false;
      }

      switch (target.FilterMode)
      {
          case TargetFactionFilterMode.HostileOnly:
              return attacker != target.Value;
          case TargetFactionFilterMode.AllowedFactionOnly:
              return attacker == target.AllowedAttackerFaction;
          default:
              return false;
      }
  }
  ```
  (Static free function on `TargetFaction` struct, matching the plan's
  conceptual signature. Place it as a static member of the `TargetFaction`
  struct itself, consistent with the `Hostile(...)`/`AllowedFrom(...)`
  factories task 001 already added there.)
- Replace each of the 5 comparison sites above with a call to
  `TargetFaction.CanHit(attackerFactionVariable, in targetFactionsArray[index])`,
  negating where the original condition was an equality "skip" check (i.e.
  `== faction` "continue" becomes `!CanHit(...)` "continue"; `!= faction` "is
  eligible" becomes `CanHit(...)` "is eligible"). Match each site's existing
  polarity exactly — do not flip accept/reject logic.
- Rename `CombatTargetAcquisition.TryNearestHostile` to
  `TryNearestEligible` (keep its exact signature and behavior — same delegation
  to `TrySelectNthNearest` with `rank: 0`); update its XML/inline comment if
  any references "hostile"; update the sole call site in
  `ExternalSpawnGateSystem.cs:169`.
- Update any inline comments in the 5 files above that describe the removed
  comparison as "hostile"/"different faction" to describe it as "eligible
  under target policy" — comment-only, do not change unrelated comments.

## Relevant Global Context
- Task 001 (already complete) expanded `TargetFaction` with `FilterMode` and
  `AllowedAttackerFaction`; `CombatFaction.None` is never a valid attacker.
- ECS jobs read unmanaged proxy data only; `CanHit` must stay a pure static
  value-comparison function — no managed dispatch, allocation, or component
  lookup.
- No new native container, target array, or structural change is introduced
  anywhere in this task.
- Domain identity (projectile/AOE/targeted tags, pooling/archetypes) is
  unaffected — this task only changes a boolean predicate's implementation and
  its two call-site names.

## Dependencies Confirmed
- Task 001 complete: `TargetFaction.FilterMode`, `TargetFaction.AllowedAttackerFaction`,
  `TargetFactionFilterMode.HostileOnly/AllowedFactionOnly` all exist in
  `Assets/Scripts/System/Targets/CombatTargetProxy.cs` (verified by orchestrator
  read after task 001 subagent finished — `TargetFaction` struct now has
  `Value`, `FilterMode`, `AllowedAttackerFaction`, plus `Hostile(...)` and
  `AllowedFrom(...)` static factories).

## Step-By-Step Instructions
1. Add the `CanHit` static helper to `TargetFaction` in `CombatTargetProxy.cs`
   as specified above.
2. In `CombatTargetAcquisition.cs`: replace the `TrySelectNthNearest` faction
   check with `!TargetFaction.CanHit(faction, in snapshot.TargetFactions[targetIndex])`;
   rename `TryNearestHostile` to `TryNearestEligible`.
3. In `ExternalSpawnGateSystem.cs`: update the one call from
   `CombatTargetAcquisition.TryNearestHostile(...)` to
   `CombatTargetAcquisition.TryNearestEligible(...)`.
4. In `ProjectileDiscreteCollisionSystem.cs` and
   `ProjectileContinuousCollisionSystem.cs`: replace
   `TargetFactions[targetIdx/targetIndex].Value == identity.Faction` with
   `!TargetFaction.CanHit(identity.Faction, in TargetFactions[targetIdx/targetIndex])`.
5. In `AoeCollisionCore.cs`: replace `targetFactions[i].Value == identity.Faction`
   with `!TargetFaction.CanHit(identity.Faction, in targetFactions[i])`.
6. In `ProjectileTrackingSystem.cs`: replace both
   `TargetFactions[...].Value != identity.Faction` occurrences with
   `TargetFaction.CanHit(identity.Faction, in TargetFactions[...])`, and
   replace `TargetFactions[targetIndex].Value == projectileFaction` with
   `!TargetFaction.CanHit(projectileFaction, in TargetFactions[targetIndex])`.
7. Grep the touched files afterward for any remaining `.Value ==`/`.Value !=`
   comparison against a faction variable to confirm none were missed.
8. Confirm `TargetedResolveSystem.cs` and `ProjectileSpawnExpansionSystem.cs`
   need no code change (they call `TrySelectNthNearest` directly, which is
   already fixed by step 2) — only touch them if you find an additional direct
   `.Value ==`/`.Value !=` faction comparison in their own code (none were
   found by the orchestrator's read, but verify).

## Acceptance Criteria
- Hostile-default targets behave exactly as before in all domains.
- `AllowedFactionOnly` target accepts selected faction even when same as
  target's own faction (true by construction of `CanHit`, not directly
  testable without task 004's tests — just ensure the logic is correct).
- Same target rejects every other faction under `AllowedFactionOnly`.
- `CombatFaction.None` cannot hit or acquire any target (enforced centrally in
  `CanHit`).
- Discrete projectile, continuous projectile, impact AOE, lingering AOE,
  targeted root acquisition, targeted chain resolution, launch aiming, and
  homing acquisition/refresh all call `TargetFaction.CanHit` (directly, or
  transitively through `CombatTargetAcquisition.TrySelectNthNearest`/
  `TryNearestEligible`).
- No hot-loop allocation, managed access, component lookup, new structural
  change, or extra target array introduced.
- Contact gates, caps, hit ordering, and consequence behavior remain
  unchanged.

## Validation Required
- Static/grep only (agent does not run Unity or tests): grep every touched
  file for leftover `.Value ==`/`.Value !=` faction comparisons to confirm all
  5 sites were converted; grep project-wide for `TryNearestHostile` to confirm
  zero remaining references after the rename.
- Confirm `CanHit`'s `switch` covers both current `TargetFactionFilterMode`
  values plus a `default: return false;` for defensive rejection of unknown
  values.
- Do not attempt to run PlayMode/EditMode tests or the Unity compiler.

## Hard Boundaries
- Do not modify files outside the allowed list except for imports/namespaces
  directly required by this task.
- Do not add tests (task 004) or documentation (task 005).
- Do not touch `CombatTickResult`/finalizer/bridge (task 003).
- Do not rename `ProjectileLaunchAimMode.NearestHostile`.
- Do not change architecture beyond adding the one helper and swapping the
  predicate at each of the 5 identified sites.
- Do not introduce new abstractions (no new component, no new snapshot type).
- Do not combine this task with task 001 or task 003.
- Do not reopen index-level decisions.
- Stop and report if any of the 5 sites' surrounding logic doesn't match what
  this packet describes (i.e. line numbers drifted enough that the intended
  edit is ambiguous) — re-locate by the code shown above rather than guessing.
