# 002 - Unify Faction Hit Eligibility

## Goal

Ensure every simulation path selects, tracks, and collides with exactly targets
allowed by target faction policy.

## Changes

1. Add one Burst-compatible helper near faction/target contracts, conceptually:

   ```csharp
   CanHit(CombatFaction attacker, in TargetFaction target)
   ```

   Rules:
   - reject `CombatFaction.None` first;
   - `HostileOnly`: accept `attacker != target.Value`;
   - `AllowedFactionOnly`: accept
     `attacker == target.AllowedAttackerFaction`;
   - reject unknown enum values defensively.

2. Replace direct same-faction comparisons in:
   - `ProjectileDiscreteCollisionSystem`;
   - `ProjectileContinuousCollisionSystem`;
   - `AoeCollisionCore` (therefore both impact and lingering AOE systems);
   - `CombatTargetAcquisition`;
   - `ProjectileTrackingSystem`, including cached-target refresh, mapped-target
     refresh, and new acquisition.

3. Update acquisition call sites to reflect eligibility semantics:
   - `ExternalSpawnGateSystem` root targeted acquisition;
   - `TargetedResolveSystem` chain links;
   - `ProjectileSpawnExpansionSystem` nearest-target launch aim.
   Snapshot construction keeps passing existing `TargetFactions` array.

4. Rename internal helper APIs/comments from "hostile" to "eligible" where they
   now describe generic policy (for example `TryNearestHostile`). Preserve
   serialized `ProjectileLaunchAimMode.NearestHostile` value/name in this scope
   to avoid an unrelated authored-content migration; document it as nearest
   eligible target under target policy.

5. Preserve ordering of other gates:
   - invalid/expired source early-outs;
   - contact/repeat-hit gates;
   - broadphase and narrowphase geometry;
   - pierce/candidate caps and deterministic tie-breaking;
   - spawn/VFX consequence emission only after an eligible contact.

6. Treat selected-faction targets as ordinary acquisition candidates. In
   particular, interval-trigger projectile launch aim may select one and orient
   its full aimed nova toward it; targeted chains and homing projectiles may
   select it under their normal distance/rank rules. Do not introduce a separate
   acquisition opt-out.

## Acceptance Criteria

- Hostile-default targets behave exactly as before in all domains.
- `AllowedFactionOnly` target accepts selected faction even when same as target
  own faction.
- Same target rejects every other faction.
- `CombatFaction.None` cannot hit or acquire any target.
- Discrete projectile, continuous projectile, impact AOE, lingering AOE,
  targeted root acquisition, targeted chain resolution, launch aiming, and
  homing acquisition/refresh all call same eligibility helper.
- No hot-loop allocation, managed access, component lookup, new structural
  change, or extra target array is introduced.
- Contact gates, caps, hit ordering, and consequence behavior remain unchanged.
- An eligible selected-faction target can redirect interval launch aim and is
  otherwise ranked exactly like any other eligible target.

## Dependencies

- Task 001.

## Estimated Scope / Complexity

High. Simple predicate, but many independent selection and collision paths must
be updated consistently.
