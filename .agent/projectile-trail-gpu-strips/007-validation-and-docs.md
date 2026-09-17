# 007 — Validate and document final pipeline

Scope: regression coverage, profiling, contracts, and handoff. Complexity:
medium. Dependencies: 002–006.

Work:

1. Update `Docs/reference/simulation/vfx-system.md`,
   `Docs/contracts/vfx-requests.md`, `Docs/flows/vfx-dispatch.md`, projectile
   simulation docs, and authoring documentation to name actual new shape and
   graph contract. Explain logical key, GPU slot ownership, fixed lifetime
   bound, invalid-index behavior, and one shared graph batch. Remove stale
   claim that projectile trails produce `LineSegment`; keep targeted links.
2. Update `CombatVfxRootRegistrationTests`, projectile authoring/validation
   tests, spawn/reuse tests, both collision-lane tests, and VFX integration
   tests. Tests should observe visible trail behavior and command semantics,
   not mirror allocator implementation. No production-only test hooks.
3. Profile target stress scenes with mixed projectile types, at least two
   trail graphs, many concurrent projectiles, different widths, and long
   tails. Record CPU queue/sort/upload time, GPU resolver/strip time,
   staging bytes, active slots, dropped Begins, full strips, and probe
   lengths. Define visual budget and fallback from measurements; gameplay
   output must remain unchanged when trails drop.
4. Check reset/loadout change, root teardown, zero-command cleanup frames,
   long sessions with high key churn, paused/culling behavior, and all target
   graphics APIs. Verify all native/GPU buffers release through resource
   owner. Review any async diagnostic readback for bounded frequency.
5. User runs named Unity Test Runner classes and exports XML under `Logs/`
   using `TestResults-EditMode.xml` and `TestResults-PlayMode.xml` (suffix as
   needed). Agent reviews XML before reporting any test result. Agent does
   not invoke Unity tests.

Acceptance:

- Documentation matches code and edited graphs; `LineSegment` remains the
  stateless targeted-link shape.
- GPU/CPU profiler counters show expected cost under agreed target stress
  workload, with explicit strip budget and observed drop behavior.
- Mixed-value shared-graph, pool reuse, delayed tails, command reordering,
  and registration conflicts covered.
- User-provided XML results reviewed before claiming tests pass.
