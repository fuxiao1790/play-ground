# 004 — Split expansion into two systems over a shared core

## Goal
Replace the combined `AoeSpawnExpansionSystem` with two systems, each a structural clone of
`ProjectileSpawnExpansionSystem` — one per event type — sharing the echo/scatter/stamp/VFX
logic through a new static `AoeExpansionCore`.

Depends on: 001, 002, 003.

## Changes
1. **Extract `AoeExpansionCore`** (static, Burst-compatible) from the current `AoeExpansionJob`
   ([AoeSpawnExpansionSystem.cs:149-253](../../Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs#L149)):
   the per-event body — template `Stamp`, `EchoCount`/`ScatterRadius` fan, `ComputeWorldBounds`,
   `AoeIdFor`, timed-spawn stamping, VFX enqueue. It takes the resolved command + output
   `NativeList<AoeSpawnCommand>` + optional VFX writer and appends spawned commands. Mirrors
   `AoeCollisionCore`/`AoeSpawnApplyUtility` (shared core, thin callers).
   - Impact vs lingering differ only in: lingering stamps `TimedSpawn` and keeps `Lifetime`;
     impact ignores timed-spawn. Keep this as a branch inside the core or two thin entry
     points — do not duplicate the fan/scatter math.
2. **`ImpactAoeSpawnExpansionSystem`** — clone of `ProjectileSpawnExpansionSystem`:
   owns `NativeQueue<ImpactAoeSpawnEvent> EventQueue`, `_scopeQuery` over `ImpactAoeSpawnEvent`,
   `NativeList<AoeSpawnCommand> ImpactCommands`, `PendingHandle`, `ProducerHandle`. Its `IJob`
   drains events, looks up `AoeSpawnTemplate.Map`, guards `evt.Kind == ImpactAoe`, calls
   `AoeExpansionCore`, appends to `ImpactCommands`.
3. **`LingeringAoeSpawnExpansionSystem`** — same, for `LingeringAoeSpawnEvent` →
   `LingeringCommands`, guard `LingeringAoe`.
4. **Retire `AoeSpawnExpansionSystem`** (delete; it no longer owns queue/commands).
5. **Repoint consumers of the command lists:**
   - `ImpactAoeSpawnApplySystem` reads `ImpactAoeSpawnExpansionSystem.ImpactCommands` /
     `.PendingHandle` (was `AoeSpawnExpansionSystem.ImpactCommands`) —
     [AoeSpawnApplySystem.cs:59-70](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L59).
   - `LingeringAoeSpawnApplySystem` reads `LingeringAoeSpawnExpansionSystem.LingeringCommands` —
     [AoeSpawnApplySystem.cs:273-284](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L273).
6. **Repoint producers' queue/`ProducerHandle` fetches** (from task 003) to the two new
   systems: `StatusProcessSystem`, `ProjectileCollisionSystem`, `TimedSpawnSystem`,
   `ImpactAoeCollisionSystem`, `LingeringAoeCollisionSystem`.
7. **System ordering.** Update every `[UpdateBefore/After(typeof(AoeSpawnExpansionSystem))]`
   ([StatusProcessSystem.cs:13](../../Assets/Scripts/System/Status/StatusProcessSystem.cs#L13),
   [CombatApplyFinalizeSystem.cs:26](../../Assets/Scripts/System/Common/CombatApplyFinalizeSystem.cs#L26),
   [ProjectileSpawnApplySystem.cs:15](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L15),
   [TimedSpawnSystem.cs:14](../../Assets/Scripts/System/Common/TimedSpawnSystem.cs#L14),
   the two apply systems, and `AoeSpawnExpansionSystem`'s own `UpdateAfter` list) to reference
   the two new systems. Preserve the current relative order (producers before expansion,
   expansion before apply). The two expansion systems have no ordering constraint between
   themselves.

## Acceptance criteria
- No reference to `AoeSpawnExpansionSystem` or `AoeSpawnEvent` remains in `Scripts/`.
- Each lane owns its queue + scope buffer + command list; apply reads its matching list.
- `AoeExpansionCore` is the single home of the fan/scatter/stamp/VFX logic (no duplication
  between the two expansion jobs).
- Existing AOE simulation behavior unchanged (task 006 tests green).

## Scope: large (two new systems + core extraction + wide re-pointing/ordering).
