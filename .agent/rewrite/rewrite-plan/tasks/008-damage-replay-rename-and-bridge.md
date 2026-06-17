# Task 008: Reshape damage replay to the contract (DamageReplayEvent + DamageDispatchBridge); single damage-clear owner

## Execution Role
This task is intended for a lower-cost implementation agent. Do not make architecture decisions. Follow this file and the linked context files. Behavior-preserving rename + one redundancy fix.

## Goal
Align the damage path with design §8 vocabulary without changing behavior: rename `CombatPendingDamage` → `DamageReplayEvent`, rename `CombatHitDispatchSystem` → `DamageDispatchBridge` (narrow its role doc to "the only approved reader that crosses to managed target callbacks"), and consolidate the duplicate `CombatDamageElement` clear to a single owner.

## Required Reading
- `../context/002-target-architecture.md` (§1.4, §2 Damage dispatch)
- `../context/003-data-flow.md` (§5)
- `../context/004-system-ordering.md` (PresentationSystemGroup, OrderFirst clear)
- `../context/005-decision-log.md` (D1, D-DAMAGE-CLEAR)

## Design Decisions Already Made
- `DamageReplayEvent` keeps the `TargetId` + `CombatFaction` identity model — NO `Entity TargetProxy` (D1, proxy entities out of scope).
- The scope `CombatDamageElement` buffer and `CombatHitFlushJob` are PRESERVED (they already give frozen-once-per-frame, atomic-per-hit semantics). Only names + the clear-owner change.
- `ProjectileSimulationSystem` is the sole owner of the `CombatDamageElement` clear; `AoeSimulationSystem` stops clearing it (D-DAMAGE-CLEAR).

## Why This Task Exists
Design §11 maps `CombatHitElement`/`CombatHitDispatchSystem`/`CombatHitFlushJob` → "typed consequence streams + DamageDispatchBridge". The typed-stream split is already done (collision emits damage / spawn / vfx separately, and spawns are now typed events). This task completes the naming + bridge-role part and fixes the double-clear.

## Current Code References
```
Assets/Scripts/System/Common/CombatHitElement.cs
- struct CombatPendingDamage  → rename to DamageReplayEvent (keep all fields: Faction, SourceId,
  TypeId, TargetId, Position, Kind, DamageAmount, CritChance, CritMultiplier, DirectDamageEnabled,
  SourceNodeId, StackEffect).
- struct CombatDamageElement  → KEEP as the scope buffer (the finalized "array").

Assets/Scripts/System/Common/CombatHitFlushJob.cs
- Reads the damage NativeStream, appends CombatDamageElement. Update the read type to DamageReplayEvent.

Assets/Scripts/System/Common/CombatHitDispatchSystem.cs
- PresentationSystemGroup; groups by (TargetId, Faction); RollDamage (UnityEngine.Random); resolves
  ICombatTarget via CombatRoot.TryGetByFaction; ReceiveHits; Clear(). RENAME class to
  DamageDispatchBridge; keep behavior identical; narrow the doc comment per §8.4.

Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs
Assets/Scripts/System/Aoe/AoeCollisionSystem.cs
- Both write CombatPendingDamage to the damage stream. Update to DamageReplayEvent.

Assets/Scripts/System/Projectile/ProjectileSimulationSystem.cs
- OnUpdate clears CombatDamageElement. KEEP — this is the sole clear owner.

Assets/Scripts/System/Aoe/AoeSimulationSystem.cs
- OnUpdate ALSO clears CombatDamageElement. REMOVE that clear (D-DAMAGE-CLEAR). If the system then
  has no remaining work, delete it (verify it has no other responsibility first — currently it only
  clears the damage buffer).
```

## Files To Modify
- `System/Common/CombatHitElement.cs` — rename `CombatPendingDamage` → `DamageReplayEvent`.
- `System/Common/CombatHitFlushJob.cs` — read `DamageReplayEvent`.
- `System/Common/CombatHitDispatchSystem.cs` — rename file + class to `DamageDispatchBridge`; narrow doc; behavior unchanged. (Rename the `.meta`-paired file by renaming the `.cs`; Unity regenerates references via the class — but a `SystemBase` is referenced by type, so update any explicit `GetOrCreateSystemManaged<CombatHitDispatchSystem>()` in tests.)
- `System/Projectile/ProjectileCollisionSystem.cs`, `System/Aoe/AoeCollisionSystem.cs` — write `DamageReplayEvent`.
- `System/Aoe/AoeSimulationSystem.cs` — remove the `CombatDamageElement` clear; delete the system if now empty.
- Tests naming `CombatHitDispatchSystem` / `CombatPendingDamage` / `AoeSimulationSystem` — update.

## Files To Create
None.

## Files To Delete
- Possibly `System/Aoe/AoeSimulationSystem.cs` if it becomes empty after removing the clear (verify first; `AoeSimulationTests` currently adds it — update the test).

## Required Changes
1. Rename `CombatPendingDamage` → `DamageReplayEvent` (field-for-field). Update the two collision writers and `CombatHitFlushJob` reader.
2. Rename `CombatHitDispatchSystem` → `DamageDispatchBridge`; keep grouping/crit/replay/clear logic byte-for-byte; update the doc comment to state it is the only approved reader crossing to managed `ICombatTarget` callbacks (design §8.4).
3. Remove the duplicate clear in `AoeSimulationSystem`; if empty, delete it and update `AoeSimulationTests` (it adds `AoeSimulationSystem` — replace with nothing, the projectile sim system owns the clear; but note the AoE tests build a world WITHOUT `ProjectileSimulationSystem`, so they must clear the damage buffer themselves or add a clear — see Validation).
4. Recompile; run.

## Behavior Preservation Requirements
- Damage grouping by `(TargetId, Faction)`, crit rolling on the main thread, one `ReceiveHits` per target group, and buffer clearing after replay are all unchanged.
- Exactly one clear of `CombatDamageElement` per frame in the live game (via `ProjectileSimulationSystem`).

## Intentional Behavior Changes
None functionally. The only change is removing a redundant second clear (the buffer was cleared twice at `OrderFirst`; clearing once is equivalent because nothing writes it between the two `OrderFirst` systems).

## Out of Scope
- Proxy entities / Entity-keyed damage (D1, deferred).
- Damage condensation (design §12, deferred).

## Dependencies
- Tasks 004 + 006 (collision systems already restructured) — do this after the cutovers to avoid editing collision twice.

## Follow-Up Tasks
- Task 009 (ordering audit confirms single clear owner + bridge placement), Task 010 (tests).

## Implementation Constraints
- `DamageReplayEvent` stays blittable (Burst). `DamageDispatchBridge` stays in `PresentationSystemGroup` (crit RNG on main thread — do NOT move crit into a Burst job; design/ecs-notes warns against `UnityEngine.Random` in jobs).
- If `AoeSimulationSystem` is deleted, ensure the AoE-only test world still clears the damage buffer each tick (the test can clear it in `Tick`, or keep a tiny clear system — see Validation).

## Step-by-Step Implementation Plan
```
1. Rename CombatPendingDamage → DamageReplayEvent; update writers + flush reader.
2. Rename CombatHitDispatchSystem → DamageDispatchBridge (file + class); narrow doc.
3. Remove AoeSimulationSystem's damage clear; delete the system if empty.
4. Update tests (system names, and AoE test damage-clear ownership).
5. Recompile; run full suite + CritEditModeTests.
```

## Acceptance Criteria
```
- [ ] DamageReplayEvent replaces CombatPendingDamage (TargetId+Faction identity; no Entity field).
- [ ] DamageDispatchBridge replaces CombatHitDispatchSystem; behavior byte-for-byte identical.
- [ ] CombatDamageElement is cleared by exactly one system (ProjectileSimulationSystem).
- [ ] CritEditModeTests and damage-replay paths still pass.
- [ ] Repo compiles.
```

## Validation
- Compile + full suite. `CritEditModeTests` must pass (crit roll unchanged).
- AoE test note: `AoeSimulationTests` builds a world without `ProjectileSimulationSystem`. If you delete `AoeSimulationSystem`, the test must still clear `CombatDamageElement` between ticks (otherwise hit counts accumulate). Simplest: have the test clear the buffer at the start of `Tick`, OR keep a minimal `CombatDamageClearSystem` added in the test. Pick one and document it in the test.
- Manual: Play `Main.unity`; confirm mobs/players take damage once per hit, crits occur, no double damage.

## Risk Level
Low–Medium — mechanical rename; the only real risk is the AoE-test clear ownership when `AoeSimulationSystem` is removed.

## Failure Modes
- **Damage accumulates across frames in tests:** the damage buffer isn't cleared in the AoE test world after removing `AoeSimulationSystem`. Detect via `AoeSimulationTests` hit-count assertions.
- **No damage / NRE on dispatch:** a renamed type still referenced by old name — fix references.

## Rollback Strategy
Revert the rename commit; restore the second clear. Names-only change, trivially reversible.

## Notes for Future Tasks
- `DamageReplayEvent` is the contract type a future proxy-entity migration (out of scope here) would extend with an `Entity` field.
