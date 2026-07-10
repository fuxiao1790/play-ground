# 001 — MobRoot pooling refactor

**Scope:** small, self-contained. **Depends on:** none. **Blocks:** 002, 004.

## Goal
Make `MobRoot` reusable from a pool: one teardown path (deactivate + clean, never
`Destroy`), and one re-entry path (`InitializeForSpawn`) shared by first spawn and reuse.

## Changes to `Assets/Scripts/Mob/MobRoot.cs`

1. **Remove self-`Destroy`.** In `DeleteCombatTargetProxy()`
   ([MobRoot.cs:282-290](../../Assets/Scripts/Mob/MobRoot.cs#L282-L290)) delete the tail:
   ```csharp
   if (softDeathNotified && this != null) { Destroy(gameObject); }
   ```
   The pool now owns destruction. Proxy deletion + `deleteProxyInLateUpdate = false` stay.

2. **Split `Awake` init.** `Awake()` ([MobRoot.cs:73-83](../../Assets/Scripts/Mob/MobRoot.cs#L73-L83))
   keeps one-time work only: `ValidateReferences`, `skillDriver = GetComponent<SkillDriver>()`,
   `targetId = ++nextTargetId`, `random` seed. Move `CurrentHealth = ...` (line 79) and
   `PickNewWanderVelocity()` (line 82) into `InitializeForSpawn()`, then call
   `InitializeForSpawn()` at the end of `Awake` so the first life is initialized identically.

3. **Add `public void InitializeForSpawn()`** — the single per-life reset, must run while the
   GameObject is active (so a subsequent `Register` sees `IsCombatTargetActive`):
   - `isAlive = true; softDeathNotified = false; deleteProxyInLateUpdate = false;`
   - `CurrentHealth = Mathf.Max(1f, statSheet.MaxHealth);`
   - `statusSnapshots.Clear();`
   - re-enable `bodyCollider`, `hurtbox`, `spriteRenderer`; `body.simulated = true;`
   - `body.linearVelocity = Vector2.zero; wanderVelocity = Vector2.zero;`
   - `PickNewWanderVelocity();`

4. **Add `public bool IsAlive => isAlive;`** for pool/controller checks.

## Notes / risks
- `SoftDie()` ([230-253](../../Assets/Scripts/Mob/MobRoot.cs#L230-L253)) and `OnDisable()`
  ([108-112](../../Assets/Scripts/Mob/MobRoot.cs#L108-L112)) are unchanged — they already
  disable colliders/sprite, unregister (clearing `registries`), and delete the proxy. That is
  the desired pool-return teardown; the controller just needs to re-`Register` on reuse.
- Do **not** auto-register in `OnEnable`: MobRoot holds no `CombatRoot` reference; the
  controller supplies it per spawn (`Register` already dedupes, lines 153-162).
- **Follow-up (out of scope):** a reused mob's `SkillDriver` may retain cooldown/aim state.
  Track a future `SkillDriver.ResetRuntime()` called from `InitializeForSpawn`.

## Acceptance criteria
- A mob that reaches `SoftDie` is **not** destroyed; its GameObject can be `SetActive(false)`
  then `SetActive(true)` + `InitializeForSpawn()` and behave as a fresh mob.
- After reuse, `IsCombatTargetActive` is true and a fresh `Register` creates a new proxy.
- First-spawn behaviour is byte-for-byte the same as before (Awake still fully initializes).
