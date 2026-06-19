# 001 — Collision handles both archetypes (gate-strategy split)

**Depends on:** none. **Scope:** `AoeCollisionSystem`. **Complexity:** medium.

## Goal

Make `AoeCollisionSystem` collide AOEs whether or not they carry
`AoeContactGateElement`, so the later archetype split (Task 002) does not drop impact
AOEs from collision. Lingering AOEs keep the retained buffer (cross-frame cooldown);
impact AOEs de-dup intra-tick with a `stackalloc` scratch.

This task is **non-breaking on its own**: until Task 002 exists, the impact variant's
`WithNone<CombatLifetimeComponent>` query matches nothing.

## Approach

Factor the cell-walk / narrow-phase into a shared generic so the ~80-line body is not
duplicated. Two thin `IJobEntity` wrappers supply different de-dup strategies:

```csharp
interface IContactGate {
    int  IndexOf(int targetKey);            // -1 if absent
    void Add(int targetKey, float cooldown);
}

// Lingering: wraps DynamicBuffer<AoeContactGateElement> (retained, cooldown-bearing).
struct BufferGate  : IContactGate { ... }

// Impact: wraps int* scratch + count (stackalloc'd in the wrapper's Execute).
struct ScratchGate : IContactGate { ... }   // Add ignores cooldown (always 0)
```

- `static void RunCollision<TGate>(ref TGate gate, ...) where TGate : struct, IContactGate`
  holds the existing logic from [AoeCollisionSystem.cs:171-273](../../Assets/Scripts/System/Aoe/AoeCollisionSystem.cs#L171-L273),
  replacing `IndexOfGate(contactGates, key)` with `gate.IndexOf(key)` and
  `contactGates.Add(...)` with `gate.Add(...)`. Burst specializes per concrete `TGate`.
- **Lingering job:** `[WithAll(AoeTag, Active, AoeCollisionActiveTag)]`,
  `[WithAll(CombatLifetimeComponent)]`, takes `DynamicBuffer<AoeContactGateElement>`,
  builds `BufferGate`, uses `cooldown = hitGate.RepeatHitCooldownSeconds`, **retains**
  (no deactivate at end — lifetime system handles it).
- **Impact job:** `[WithAll(AoeTag, Active, AoeCollisionActiveTag)]`,
  `[WithNone(CombatLifetimeComponent)]`, **no** buffer param;
  `int* seen = stackalloc int[CollisionConstants.MaxAoeTargetsPerTick]`, builds
  `ScratchGate`, `cooldown = 0`, and **unconditionally deactivates** after the pass
  (constraint 2 — no lifetime system for impact AOEs).
- The `lifetimeEnabled` branches at [:187](../../Assets/Scripts/System/Aoe/AoeCollisionSystem.cs#L187)
  and [:266](../../Assets/Scripts/System/Aoe/AoeCollisionSystem.cs#L266) become
  compile-time constants per variant and collapse.

## System wiring

- `activeAoeQuery` ([:24-38](../../Assets/Scripts/System/Aoe/AoeCollisionSystem.cs#L24-L38)):
  drop `AoeContactGateElement` from the required set; keep it only on the lingering job
  via `[WithAll(CombatLifetimeComponent)]` + the buffer param.
- **Per-entity VFX stream indices.** Each variant runs over its own query indexed from 0,
  so they cannot share one `NativeStream` index space. Give each variant its **own**
  `NativeStream` sized to its own query count and flush both (mirror the existing
  `VfxStreamFlushJob` for each). Combine both collision handles into the existing
  `expansion.ProducerHandle` / `damageBridge.ProducerHandle` and dispose chains.
- Schedule both jobs; `CombineDependencies` their handles everywhere the single handle
  is used today.

## Acceptance

- Builds; Burst compiles both specializations.
- With no impact archetype yet (pre-Task-002), every AOE matches the lingering job and
  behavior is identical to today; all collision tests still pass.
- No `DynamicBuffer` parameter on the impact path; no heap allocation introduced (scratch
  is `stackalloc`).
- `OverlappingTargetRegisteredInMultipleCellsHitsOnce` passes against the impact path once
  Task 002 routes entities to it (re-checked in 003).
