# Context 006 — Validation Strategy

How to validate each task and the whole rewrite. The project is a Unity Entities (DOTS) project; "compile" means the Unity Editor recompiles the `PlayGround.Runtime` assembly with no errors, and Burst compiles the jobs.

Scope note: this is **Increment 2** (direction-fidelity). The spawn/event/lifetime spine already compiles and is tested (Increment 1). Each Increment-2 task changes a working baseline, so the bar is **keep the suite green after every task** while the architecture moves to the design.

---

## Compile validation
- After every task, the Unity Editor must recompile with **zero errors**.
- Burst: `ProjectileSpawnEvent`/`ProjectileSpawnCommand`/`AoeSpawnEvent`/`AoeSpawnCommand`/`DamageReplayEvent` are plain blittable structs (no managed refs). `DamageReplayEvent` now carries `Entity TargetProxy` (blittable) — still Burst-safe.
- The `TargetCompanion` managed companion is a **class `IComponentData`** (managed component) — it is NOT touched by any `[BurstCompile]` job; only `DamageDispatchBridge` (main thread) reads it.
- A struct used in both `NativeQueue<T>` and `DynamicBuffer<T>` (the `...SpawnEvent` types) must implement `IBufferElementData` and contain no managed fields.

## Unit / integration tests (Unity Test Runner)
Canonical pattern: `AoeSimulationTests.cs` — bare `World`, systems added to a `SimulationSystemGroup`, manual scope entity, `Tick(dt)`, assert.

Existing tests that MUST keep passing (update system lists / type names / occupancy flag as each task lands):
- `AoeSimulationTests.cs` — switch occupancy assertions from `AoeActiveTag` → `Active` (Task 011); `AoeSpawnCommandData` → `AoeSpawnCommand` (Task 012); damage assertions move from the `CombatDamageElement` buffer to the finalized `NativeArray<DamageReplayEvent>` / a test bridge hook (Task 015); targets become **proxy entities** rather than `CombatTargetElement` rows (Task 014) — `AddTarget` creates a proxy.
- `ProjectileTrackingSimulationTests.cs`, `ProjectileSpawnPipelineTests.cs` — occupancy flag + command-name updates; spawn via `ProjectileSpawnEvent`.
- `AoePlayModeTests.cs`, `BareMinimumPrototypePlayModeTests.cs`.
- EditMode: `CritEditModeTests.cs` (crit roll stays in the bridge), `ProjectileAuthoringEditModeTests.cs`, `SkillValidationEditModeTests.cs` — update to the renamed authoring Event types (Task 012).

New tests to add (Task 017):
1. **Generic `Active` reuse:** spawn → expire (disable `Active`) → spawn reuses the same entity via `WithDisabled<Active>`; both domains.
2. **Event/Command naming + split:** a `Count==1` event yields one entity; the command type has no `Count` field (structural); per-shape container routing puts child-spawner projectiles through the child-spawner apply system only.
3. **Per-shape apply:** a basic projectile never lands in the child-spawner archetype and vice-versa; each apply system reuses only its own shape's disabled slots.
4. **Proxy lifecycle:** creating a target makes a proxy entity; pushing position updates `TargetPosition`; deleting the target removes the proxy before the next collision; collision reads the proxy.
5. **Entity-keyed damage:** a hit produces a `DamageReplayEvent` whose `TargetProxy` equals the hit proxy entity; dispatch resolves it via `TargetCompanion` to the right `ICombatTarget`; atomic per-hit and group-by-proxy preserved; crit roll unchanged (`CritEditModeTests` still green).
6. **Proxy deletion safety (R5/§8.3):** a target that dies during dispatch does not delete its proxy until after dispatch; no event references a missing proxy.
7. **Convert-elimination parity** (carried from Increment 1): impact-AoE-on-hit golden values unchanged.

## Manual gameplay / editor scenarios
- `Main.unity`, Play mode: fire player attacks; confirm projectiles move/hit/damage; multi-shot counts; child spawns; impact AOEs and impact-projectile bursts; mob projectiles damage the player; **targets take damage resolved through their proxy/companion** (no NRE on death, death animation still plays after proxy deletion).
- `DebugOverlay` counters: active projectiles, sim ms, active AOEs, spawn/despawn rate, managed allocations per frame (must stay flat — companion is read only at dispatch).

## Profiling checks (Unity Profiler)
- Existing markers: `Projectile.Spawn`, `.ReuseJob`, `Aoe.Spawn`, Cold/Reuse counters — keep equivalents on the per-shape apply systems.
- **Structural Changes** module: cold-create stays the exceptional path (one ECB playback per apply system per frame); proxy create/delete is one structural change per target enable/disable, not per frame.
- Allocation: no new per-frame managed allocations. The `TargetCompanion` is read once per damaged-target group at presentation — no per-hit managed allocation.
- Performance target (Docs/architecture.md): ~50k projectiles, 20 targets, 120 fps. Don't regress spawn/sim ms; the proxy spatial query must match or beat the old `CombatTargetElement` hash. Profile a stress scene before/after Phases C–E.

## Expected failure modes & debugging hints
- **Same-frame recursion:** newly spawned entity acts the same tick → ordering invariant 1 broken.
- **Lost spawns:** expansion finalize not draining BOTH the queue and the scope buffer, or producers write a different queue instance than expansion drains.
- **No damage / NRE on dispatch:** `TargetProxy` resolves to a deleted proxy (proxy deleted before dispatch — violates §8.3 ordering), or the `TargetCompanion` lookup misses; confirm proxies are deleted in `LateUpdate`, after dispatch.
- **Double / missing damage:** the damage `NativeQueue` not cleared after the bridge reads it, or read while collisions still write (missing finalize sync).
- **Wrong target hit:** proxy spatial query stale because the GameObject didn't push position before simulation (Task 016 ordering).
- **Container leaks:** every `NativeQueue`/`NativeArray`/`NativeStream` (spawn queues, per-shape command containers, damage queue) disposed in `OnDestroy`; per-frame temporaries disposed each frame. Cross-check `Docs/coding-standards.md` "Native And ECS Handle Ownership".
- **Burst error on companion:** a job tried to touch `TargetCompanion` — only the main-thread bridge may (§8.4).
