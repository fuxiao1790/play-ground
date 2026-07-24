# 008 — Invert stats display coupling (break the sim↔Debugging cycle)

**Status: DONE in code (user, 2026-07-24) — pending Unity compile/play verification.**
Implemented as a direct ECS-world read from the overlay (not a system accessor); the
simulation dropped the push entirely. The split is no longer gated on this.

**Depends on:** none (code fix inside the single assembly; do alongside 001–004).
**Prerequisite for:** 009 (Debugging leaf package) and 004 (sim package must be
Debugging-free). **Scope:** small, behavior-preserving.

## Problem
The sim references the Debugging overlay, creating a cycle:
- `CombatStatsGatherSystem` (sim) holds `CombatStatsBinding.Display` typed
  `global::PerformanceText` and pushes each frame via `binding.Display?.Apply(...)`.
- `PerformanceText` (Debugging) reads sim types (`CombatStatsGatherSystem`,
  `CombatVfxRoot`).

Two assemblies cannot mutually reference, so Debugging cannot be split out until the
sim→Debugging edge is gone.

## Insight
The snapshot is **already** published to the `CombatStatsSingleton` ECS component
every frame (`CombatStatsGatherSystem.OnUpdate` lines 70–73). The push to
`PerformanceText` is redundant — the overlay can pull the singleton instead.

## Changes (as implemented)
- `CombatStatsGatherSystem.cs`: deleted `Bind`/`Unbind`, the
  `AddComponentObject(_statsEntity, new CombatStatsBinding())` in `OnCreate`, and the
  `binding.Display?.Apply(...)` push in `OnUpdate`. The system still writes the
  snapshot to `CombatStatsSingleton` — that publish is the whole contract now. No
  accessor added.
- `CombatStatsComponents.cs`: deleted the `CombatStatsBinding` managed component;
  refreshed the data-flow comment (publish, not push).
- `PerformanceText.cs`: added `ReadCombatStats()` that reads the world directly —
  `World.DefaultGameObjectInjectionWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CombatStatsSingleton>()).TryGetSingleton(out …)`
  — and `Update()` uses it. Removed `OnEnable`/`OnDisable` `Bind`/`Unbind`, `Apply`,
  and the cached push state; the overlay holds no system reference.

## Acceptance criteria
- `grep -rn "PerformanceText" Assets/Scripts/System` returns nothing.
- `grep -rn "CombatStatsBinding" Assets Packages` returns nothing.
- Sim still compiles; the overlay still shows live, updating stats.

## Verification (user, Unity)
- `BenchmarkLarge` overlay counters update exactly as before.
