# 001 - Expand Target Faction Contract

## Goal

Make faction-based hit eligibility generic data carried by every target proxy,
without adding optional components or parallel target snapshot arrays.

## Changes

1. In `Assets/Scripts/System/Targets/CombatTargetProxy.cs`:
   - add `TargetFactionFilterMode : byte` with `HostileOnly = 0` and
     `AllowedFactionOnly = 1`;
   - expand `TargetFaction` with `FilterMode` and
     `AllowedAttackerFaction` fields while retaining `Value` as target own
     allegiance;
   - add/update `ECS Lifecycle:` comment: component is present on every target
     proxy, stamped at creation, immutable until proxy deletion in this scope;
   - add named value constructors/factories for hostile-default and selected-
     faction policies so call sites do not assemble contradictory fields.

2. In `Assets/Scripts/System/Targets/TargetProxyEvents.cs`:
   - replace scalar faction creation payload with full `TargetFaction` snapshot,
     or add equivalent fields if layout clarity demands it;
   - keep event unmanaged and creation-only.

3. In `Assets/Scripts/System/Targets/CombatTargetProxy.cs` and
   `TargetProxyCreateApplySystem.cs`:
   - make proxy creation accept/stamp full target faction policy;
   - update `CombatTargetRegistry` and existing call sites to request
     hostile-default policy, preserving player/mob behavior;
   - do not modify `PlayerRoot`, `MobRoot`, or add summon/firing-proxy code;
   - keep `TargetFaction` in existing target archetype, so component count and
     structural lifecycle remain unchanged.

4. Keep `TargetSpatialHashSystem` on existing `NativeList<TargetFaction>` gather,
   resize, clear, and disposal path. Expanded fields must copy automatically with
   same aligned array; do not add another native container.

## Acceptance Criteria

- Every proxy has one `TargetFaction` containing allegiance and hit policy.
- `new TargetFaction { Value = X }` and hostile factory both preserve current
  different-faction behavior because zero mode is `HostileOnly`.
- Selected-faction policy can represent target accepting its own faction.
- Selected faction `None` is representable but will match no valid attacker.
- Proxy creation transfers policy without managed reads in simulation jobs.
- Existing target archetype, hash ownership, build handles, and cleanup shape do
  not gain new components or native containers.
- No concrete GameObject behavior or content authoring is added.

## Dependencies

None.

## Estimated Scope / Complexity

Medium. Small data-shape change with broad construction/test fixture updates.

