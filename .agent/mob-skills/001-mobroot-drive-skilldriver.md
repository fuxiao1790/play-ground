# 001 — MobRoot drives a SkillDriver

## Goal
Make `MobRoot` drive an optional `SkillDriver` each frame, firing at any valid
enemy-faction target. Mirrors `PlayerRoot.Update`'s `skillDriver.Tick(...)` call.

## File
`Assets/Scripts/Mob/MobRoot.cs`

## Changes

1. **Field + Awake resolve.** Add a `SkillDriver` reference (private). Resolve in `Awake`
   via `GetComponent<SkillDriver>()` (matches how `PlayerRoot` obtains its driver). It may
   be `null` — a mob without a `SkillDriver` component keeps wandering with no offense, so
   every call site must be null-guarded. `using PlayGround.Skills;`.

2. **Fill in `BindCombatRoot`** (currently the empty stub at `MobRoot.cs:138-140`):
   forward to `skillDriver?.BindCombatRoot(root)`. No need to store the `CombatRoot` on
   `MobRoot` — target acquisition uses the already-populated `registries` list.

3. **Drive skills in `Update`.** After the existing wander tick (inside the `isAlive`
   path, `MobRoot.cs:89-92`), call a new private `DriveSkills()`:
   - `if (skillDriver == null) return;`
   - `if (TryAcquireEnemyTarget(out ICombatTarget enemy))`
     - `Vector2 targetPos = enemy.CombatTargetPosition;`
     - `Vector2 toTarget = targetPos - (Vector2)transform.position;`
     - `Vector2 aimDir = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.right;`
     - `skillDriver.Tick(true, aimDir, targetPos);`
   - `else` → `skillDriver.Tick(false, Vector2.zero, (Vector2)transform.position);`
     (still advances cooldowns).

4. **`TryAcquireEnemyTarget(out ICombatTarget enemy)`** — simplest valid-target logic:
   iterate the existing `registries` list; for each registry iterate `registry.Targets`;
   return the **first** `t` where `t.IsCombatTargetActive && t.CombatFaction != CombatFaction`
   (this mob's faction is `CombatFaction.Mob`, so this matches the player and any future
   `Player`-faction summon). Set `enemy` and return `true`; else `enemy = null; return false`.
   No distance math. `CombatTargetRegistry.Targets` is `IReadOnlyList<ICombatTarget>`
   (`CombatTargetRegistry.cs:27`).

5. **Optional cosmetic (may skip):** `spriteRenderer.flipX = aimDir.x < 0f;` so the mob
   faces its target, consistent with `PlayerFacing.AimAt`.

## Notes / gotchas
- Do **not** read `TargetSpatialHashSingleton` from the main thread (job-guarded).
- `SkillDriver.Tick` internally ticks all slot cooldowns before the `fireHeld` check, so
  passing `false` when no target is present is correct and cheap.
- Dead mobs already `return` before `DriveSkills` via the `!isAlive` guard
  (`MobRoot.cs:83-87`), so no post-death firing; in-flight projectiles are unaffected.

## Acceptance criteria
- A mob with a wired `SkillDriver` (faction `Mob`) fires its loadout toward an active
  enemy-faction target every cooldown period; aim tracks the target's live position.
- A mob with **no** `SkillDriver` component wanders normally with no errors.
- No new spawn/faction plumbing added; only `MobRoot.cs` changes.

## Scope
Small — one MonoBehaviour, ~30 lines, one `using`.

## Dependencies
None (compiles against existing `SkillDriver`, `CombatFaction`, `ICombatTarget`,
`CombatTargetRegistry`). Prefab wiring (003) is what makes it fire at runtime.
