# Task 016: Codify the phase order (proxy + damage queue + Active)

## Goal
Re-assert the design §9 order after the Increment-2 changes: GameObject proxy push (Update) before simulation, proxy delete (LateUpdate) after dispatch, the damage `NativeQueue → NativeArray` finalize between collision and dispatch, and all reuse over `Active`. Audit the §6 container discipline for the new damage queue and per-shape command containers.

## Required Reading
- `.agent/rewrite/design.md` §9; `../context/004-system-ordering.md` (the contract)
- `../context/002-target-architecture.md` §3 (container table)
- index Global Invariants 1–8

## Required Changes
1. **Ordering attributes / group placement** to match `../context/004`:
   - 9.1 `CombatLifetimeSystem` before all collision.
   - 9.4/9.5 collisions read proxies; before expansions and before damage finalize.
   - 9.7 damage finalize (`NativeQueue → NativeArray`) after both collisions, before the presentation bridge.
   - 9.8 expansions after all collisions; 9.9 per-shape apply systems last (R5).
   - `DamageDispatchBridge` in `PresentationSystemGroup`, after finalize.
2. **Frame boundary (MonoBehaviour):** confirm targets push proxy `TargetPosition` in `Update()` before the ECS world ticks, and delete proxies in `LateUpdate()` (or end of Update, after dispatch). Document the driver order (see design §9). No proxy is deleted while a damage event still references it (Global Invariant 7).
3. **Container discipline audit (§6):** for the damage queue and each per-shape projectile command container and each spawn event queue — owner creates/disposes; reader clears/disposes; assert-empty-before-write; no read while writing; dispose in `OnDestroy`. No leftover `CombatDamageElement` clear.
4. **Domain gating audit:** every projectile/AoE system queries `ProjectileTag`/`AoeTag`; `Active` alone never opts in (Global Invariant 6).
5. Recompile; run.

## Behavior Preservation Requirements
- Pure ordering/ownership codification; no logic change. Next-tick spawn (R5) and proxy-deletion safety (§8.3) hold by order alone.

## Dependencies
011, 013, 014, 015.

## Acceptance Criteria
- [ ] System order matches `../context/004` exactly; attributes don't contradict it.
- [ ] Proxy push (Update) precedes simulation; proxy delete (LateUpdate) follows dispatch.
- [ ] Damage finalize sits between collisions and the bridge.
- [ ] §6 discipline holds for the damage queue + per-shape containers + event queues (no leaks, no read-while-write).
- [ ] Repo compiles; suite green.

## Risk
Low–Medium — ordering regressions can reintroduce same-tick recursion or stale-proxy reads. Covered by the next-tick and proxy-deletion tests (Task 017).
