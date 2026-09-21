# Task Execution Packet

## Task
001-target-faction-contract.md

## Goal
Expand `TargetFaction` into a full faction-eligibility snapshot (own faction +
filter mode + allowed attacker faction) carried by every target proxy, stamped
at creation, with zero new components/native arrays and zero behavior change
for existing hostile-only usage.

## Files Allowed To Modify
- `Assets/Scripts/System/Targets/CombatTargetProxy.cs`
- `Assets/Scripts/System/Targets/TargetProxyEvents.cs`
- `Assets/Scripts/System/Targets/TargetProxyCreateApplySystem.cs`
- `Assets/Scripts/System/Targets/CombatTargetRegistry.cs`

## Files Allowed To Create
- None required. (If a truly minimal new static helper/factory file feels
  necessary, stop instead — task 001 does not authorize new files; put
  factories as static members on `TargetFaction` itself.)

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading (do not modify)
- `Assets/Scripts/System/Core/CombatScope.cs` (defines `CombatFaction` enum:
  `None = 0, Player = 1, Mob = 2`)
- `Assets/Scripts/System/Targets/ICombatTarget.cs`
- `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs`
  (confirm it only copies `TargetFaction` via existing
  `NativeList<TargetFaction>` gather — do not modify this file in task 001)
- `Assets/Scripts/Player/PlayerRoot.cs`, `Assets/Scripts/Mob/MobRoot.cs` (confirm
  they only expose `CombatFaction` property; must NOT be modified)

## Current Shapes (verified by orchestrator before packet creation)

`TargetFaction` today (`CombatTargetProxy.cs`):
```csharp
public struct TargetFaction : IComponentData
{
    public CombatFaction Value;
}
```

`TargetProxyCreateEvent` today (`TargetProxyEvents.cs`) carries
`public CombatFaction Faction;` among other fields.

`TargetProxyCreateApplySystem.OnUpdate` currently does:
```csharp
EntityManager.SetComponentData(proxy, new TargetFaction { Value = createEvent.Faction });
```

`CombatTargetProxy.Create(EntityManager, ICombatTarget, CombatFaction faction)`
is the only production entry point (called once, from
`CombatTargetRegistry.TryCreateProxy`:
`CombatTargetProxy.Create(entityManager, target, target.CombatFaction);`).
Several PlayMode test files also call this exact 3-arg overload directly
(`AoeSimulationTests.cs`, `ProjectileCollisionSimulationTests.cs`) — task 001
must NOT break these call sites; test-file changes are task 004's job, not
this task's.

## Behavior To Preserve
- `new TargetFaction { Value = X }` (default `FilterMode == 0`) and any hostile
  factory must both mean "attacker must differ from target's own faction" —
  i.e. `FilterMode = 0` must be named/valued `HostileOnly`.
- `CombatTargetProxy.Create(EntityManager, ICombatTarget, CombatFaction faction)`
  (the existing 3-arg overload) must keep compiling and keep producing
  hostile-default proxies, since it is called from production code
  (`CombatTargetRegistry`) and several existing tests outside this task's
  scope.
- `TargetSpatialHashSystem`'s existing `NativeList<TargetFaction>` gather,
  resize, clear, disposal path must need zero changes — expanded fields ride
  along automatically since it's the same component type.
- `TargetProxyCreateApplySystem` must remain the only writer of `TargetFaction`
  at creation; do not add a runtime-update path.
- Do not touch `PlayerRoot.cs`, `MobRoot.cs`, or any summon/firing-proxy code.
- `TargetProxyCreateEvent`/`TargetProxyUpdateEvent`/`TargetProxyDeleteEvent`
  must remain unmanaged, blittable, creation/update-intent-only buffer elements.

## Behavior To Change
- `TargetFaction` gains:
  - `public TargetFactionFilterMode FilterMode;` (new enum, byte-backed:
    `HostileOnly = 0`, `AllowedFactionOnly = 1`)
  - `public CombatFaction AllowedAttackerFaction;`
  - Keep `public CombatFaction Value;` (target's own faction) unchanged in
    meaning.
  - Add named static factories on `TargetFaction`, e.g.
    `public static TargetFaction Hostile(CombatFaction ownFaction)` (returns
    `FilterMode = HostileOnly`, `AllowedAttackerFaction = CombatFaction.None`)
    and
    `public static TargetFaction AllowedFrom(CombatFaction ownFaction, CombatFaction allowedAttacker)`
    (returns `FilterMode = AllowedFactionOnly`).
  - Add/refresh the `// ECS Lifecycle:` comment above `TargetFaction`: present
    on every target proxy, stamped once at creation via
    `TargetProxyCreateApplySystem`, immutable for proxy lifetime in this scope.
- `TargetProxyCreateEvent.Faction : CombatFaction` becomes a full
  `TargetFaction` snapshot field (e.g. rename to `FactionPolicy : TargetFaction`,
  or keep the field name if that reads cleanly — orchestrator defers exact
  naming to you, but it must carry the full struct, not just the enum).
- `CombatTargetProxy`:
  - Add a new overload `Create(EntityManager entityManager, ICombatTarget target, TargetFaction policy)`
    that does everything the current 3-arg overload does but forwards the full
    policy into the create event.
  - Keep the existing `Create(EntityManager, ICombatTarget, CombatFaction faction)`
    overload compiling; reimplement it as a thin wrapper that calls the new
    overload with `TargetFaction.Hostile(faction)`, so `CombatTargetRegistry`
    and existing tests keep hostile-default behavior with zero further edits
    required.
- `TargetProxyCreateApplySystem.OnUpdate`:
  - Replace `new TargetFaction { Value = createEvent.Faction }` with
    `EntityManager.SetComponentData(proxy, createEvent.<renamed policy field>);`
    (stamp the full struct directly).
- `CombatTargetRegistry.TryCreateProxy`: only touch if the compat overload
  approach above does not fully preserve its call
  (`CombatTargetProxy.Create(entityManager, target, target.CombatFaction);`).
  Prefer leaving this file's call site untouched if the compat overload makes
  it unnecessary — but the task explicitly lists this file as in-scope, so if
  you judge it clearer to have the registry call
  `TargetFaction.Hostile(target.CombatFaction)` explicitly through the new
  overload, that is also acceptable. Pick one and note the choice in the log.

## Relevant Global Context
- One eligibility rule will be added in task 002 (not this task) — do not add
  a `CanHit`/eligibility helper now, only the data shape.
- `CombatFaction.None` must remain representable as `AllowedAttackerFaction`
  (meaning: unhittable) — no validation/throw needed in this task, task 002
  owns the rejection rule.
- ECS jobs read unmanaged proxy data only; keep everything blittable, no
  managed types added to `TargetFaction` or `TargetProxyCreateEvent`.
- Target snapshot list order/alignment in `TargetSpatialHashSystem` must not
  need any change — verify by reading, not by editing.
- No new native container, component, or archetype field count change (the
  archetype's `TargetFaction` component grows in size, not in components).

## Dependencies Confirmed
- None (task 001 has no dependencies). Verified by reading current source at
  `Assets/Scripts/System/Targets/CombatTargetProxy.cs`,
  `TargetProxyEvents.cs`, `TargetProxyCreateApplySystem.cs`,
  `CombatTargetRegistry.cs`.

## Step-By-Step Instructions
1. In `CombatTargetProxy.cs`, add `TargetFactionFilterMode` enum and expand
   `TargetFaction` struct with `FilterMode` + `AllowedAttackerFaction` fields
   plus `Hostile(...)` / `AllowedFrom(...)` static factories and the lifecycle
   comment, as described above.
2. In `TargetProxyEvents.cs`, change `TargetProxyCreateEvent`'s faction payload
   from a bare `CombatFaction` to a full `TargetFaction` snapshot field.
3. In `CombatTargetProxy.cs`, add the new `TargetFaction`-accepting `Create`
   overload; make the existing `CombatFaction`-accepting overload delegate to
   it via `TargetFaction.Hostile(faction)`. Update the internal event
   construction (currently building `TargetProxyCreateEvent { ..., Faction =
   faction, ... }`) to populate the renamed policy field.
4. In `TargetProxyCreateApplySystem.cs`, stamp the full policy struct onto the
   proxy instead of constructing `new TargetFaction { Value = ... }`.
5. Decide whether `CombatTargetRegistry.cs` needs any edit given the compat
   overload; make the minimal edit (or none) and record the decision.
6. Re-check every call site touched by the rename (search for
   `createEvent.Faction`, `.Faction =` on `TargetProxyCreateEvent`, and any
   other direct construction of `TargetProxyCreateEvent`) so nothing is left
   referencing a removed field name.

## Acceptance Criteria
- Every proxy has one `TargetFaction` containing allegiance and hit policy.
- `new TargetFaction { Value = X }` and the hostile factory both preserve
  current different-faction behavior (`FilterMode` default/zero is
  `HostileOnly`).
- Selected-faction policy can represent a target accepting its own faction
  (`AllowedFactionOnly` with `AllowedAttackerFaction == Value`).
- Selected faction `None` is representable but matches no valid attacker
  (data-shape only — rejection logic is task 002).
- Proxy creation transfers policy without any managed reads inside simulation
  jobs.
- Existing target archetype, hash ownership, build handles, and cleanup shape
  gain no new components or native containers.
- No concrete GameObject/summon/firing-proxy behavior or content authoring is
  added.
- Solution compiles: every reference to the old `TargetProxyCreateEvent.Faction`
  field name is updated consistently.

## Validation Required
- Static/code-level check only (agent does not run Unity or tests): grep for
  any remaining reference to the old `TargetProxyCreateEvent.Faction` field
  name or any other now-invalid member access across `Assets/Scripts` and
  `Assets/Tests` to catch compile breaks caused by the rename.
- Confirm `CombatTargetRegistry.cs`'s existing call
  `CombatTargetProxy.Create(entityManager, target, target.CombatFaction);`
  still resolves against an overload (either unchanged, or updated
  consistently).
- Confirm the two production reads in `TargetProxyCreateApplySystem.cs` no
  longer reference a bare `CombatFaction` field that was removed.
- Do not attempt to run PlayMode/EditMode tests or the Unity compiler.

## Hard Boundaries
- Do not modify files outside the allowed list except for imports/namespaces
  directly required by this task.
- Do not add the `CanHit` eligibility helper (task 002).
- Do not modify collision/acquisition/tracking systems (task 002).
- Do not modify `CombatTickResult`/finalizer/bridge (task 003).
- Do not add or edit tests (task 004).
- Do not edit documentation (task 005).
- Do not change architecture beyond what's specified above.
- Do not introduce new abstractions not described by this task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop and report if `CombatTargetRegistry.cs`'s change requires any behavior
  decision beyond "keep hostile-default equivalent to today."
