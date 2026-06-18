# Task 011: Generic `Active` occupancy flag

## Goal
Adopt design §3's generic occupancy flag and remove the per-domain active tags:
```csharp
public struct Active : IComponentData, IEnableableComponent {}
```
Replace `ProjectileActiveTag` and `AoeActiveTag` with `Active` everywhere. Slot **kind** stays defined by the domain/shape marker components (`ProjectileTag`/`AoeTag`); slot **alive** is `Active` enabled. Reuse queries become `WithDisabled<Active>()` + the markers.

## Required Reading
- `../context/002-target-architecture.md` §1.4
- `../context/005-decision-log.md` → D-ACTIVE-GENERIC + Governing principle
- `../context/004-system-ordering.md` (read/write table: `Active` row)

## Scope / what stays
- Only the **occupancy** flag is unified. `CombatRenderActiveTag` and the collision-active opt-in tags (`ProjectileCollisionActiveTag`/`AoeCollisionActiveTag`) are a **different concept** (per-feature participation) and **stay**.
- Domain gating is unchanged: every projectile/AoE system still queries `ProjectileTag`/`AoeTag`; `Active` alone must never opt an entity into a domain system (Global Invariant 6).

## Current Code References (Increment-1 end-state)
- `System/Projectile/ProjectileEcsComponents.cs` — `ProjectileActiveTag : IComponentData, IEnableableComponent`.
- `System/Aoe/AoeEcsComponents.cs` — `AoeActiveTag`.
- Writers (enable/disable): `*SpawnApplySystem` (enable on spawn/reuse), `CombatLifetimeSystem` (disable on expiry), `ProjectileCollisionSystem`/`AoeCollisionSystem` (`Deactivate` disables active + render).
- Reuse queries: the apply systems' `WithDisabled<...ActiveTag>` + shared-component filter.
- All readers via `grep ProjectileActiveTag|AoeActiveTag` (~30 files incl. tests).

## Files To Create / Modify for the new type
- Add `public struct Active : IComponentData, IEnableableComponent {}` to `System/Common/CombatEcsComponents.cs` (the existing shared-component file — do not create a new file).

## Files To Modify
- `System/Projectile/ProjectileEcsComponents.cs`, `System/Aoe/AoeEcsComponents.cs` — delete the two active tags.
- Every system that referenced them — apply, lifetime, both collisions, tracking, child-spawn, render-prepare, `CombatRoot`, archetype builders in the apply systems (`ComponentType.ReadWrite<Active>()` in the archetype + reuse query).
- All tests referencing the old tags.

## Required Changes
1. Add `Active`.
2. Mechanically replace `ProjectileActiveTag` → `Active` and `AoeActiveTag` → `Active` across `Assets/Scripts` and `Assets/Tests`. Keep `WithDisabled<Active>()` shapes identical; keep the same enable/disable call sites.
3. In each apply archetype, replace the per-domain active tag with `Active`; the domain marker (`ProjectileTag`/`AoeTag`) stays in the archetype so the slot kind is still distinguishable.
4. Recompile; run the suite.

## Behavior Preservation Requirements
- Reuse picks the same slots (now `WithDisabled<Active>` + markers); spawn enables, lifetime/collision disable — byte-for-byte equivalent behavior, only the flag name/type changes.
- No entity is opted into a domain system by `Active` alone (markers still gate).

## Dependencies
None. Do first — Tasks 013/014/015 query `Active`.

## Acceptance Criteria
- [ ] `Active` exists; `ProjectileActiveTag`/`AoeActiveTag` are gone.
- [ ] All reuse queries use `WithDisabled<Active>()` + domain/shape markers.
- [ ] `CombatRenderActiveTag` and collision-active tags unchanged.
- [ ] Repo compiles; full suite green (with test references updated).

## Risk
Medium — wide but mechanical (~30 files). The only real trap is accidentally collapsing a render/collision opt-in tag into `Active`; keep those separate.
