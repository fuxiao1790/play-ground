# Context 006 — Validation Strategy

How to validate each task and the whole rewrite. The project is a Unity Entities (DOTS) project; "compile" means the Unity Editor recompiles the `PlayGround.Runtime` assembly with no errors, and Burst compiles the jobs.

---

## Compile validation
- After every task, the Unity Editor must recompile with **zero errors**. There is no headless build step in the task instructions; the implementer (or the reviewing agent) confirms via the Editor console or by ensuring the changed `.cs` files reference only existing symbols.
- Burst: jobs marked `[BurstCompile]` must use only unmanaged types. `ProjectileSpawnEvent`/`ProjectileSpawnCommandData`/`AoeSpawnEvent`/`AoeSpawnCommandData`/`DamageReplayEvent` are plain structs of blittable fields + existing blittable snapshot structs — keep them Burst-safe (no managed refs).
- Watch for: a struct used in both `NativeQueue<T>` and `DynamicBuffer<T>` must implement `IBufferElementData` and contain no managed fields.

## Unit / integration tests (Unity Test Runner)
Canonical pattern: `Assets/Tests/PlayMode/AoeSimulationTests.cs` — construct a bare `World`, add the relevant systems to a `SimulationSystemGroup`, manually create a scope entity with the needed buffers, drive `Tick(dt)`, assert on buffer contents / entity counts.

Existing tests that MUST keep passing (update their system lists / buffer names as types change):
- `AoeSimulationTests.cs` — AoE spawn/lifetime/collision; adds `AoeSimulationSystem`, `AoeSpawnSystem`, `AoeContactGateSystem`, `AoeLifetimeSystem`, `AoeCollisionSystem`; creates scope buffers `CombatTargetElement`, `AoeSpawnRequestElement`, `CombatDamageElement`, `VfxSpawnRequestElement`. After the rewrite these become `AoeSpawnExpansionSystem` + `AoeSpawnApplySystem`, `CombatLifetimeSystem`, and the scope buffer `AoeSpawnRequestElement` → `AoeSpawnEvent`.
- `ProjectileTrackingSimulationTests.cs` — projectile movement/tracking (largely unchanged, but if it spawns via the buffer it must use `ProjectileSpawnEvent`).
- `AoePlayModeTests.cs`, `BareMinimumPrototypePlayModeTests.cs`.
- EditMode: `CritEditModeTests.cs` (crit roll lives in the dispatch bridge — unchanged), `ProjectileAuthoringEditModeTests.cs`, `SkillValidationEditModeTests.cs`.

New tests to add (Task 010):
1. **Expansion fan-out:** enqueue one `ProjectileSpawnEvent` with `Count=3, SpreadDegrees>0` → after expansion+apply, exactly 3 active projectiles with distinct ids and spread velocities; assert no command carries `Count` (structural — the type has no such field).
2. **Event/command split:** a `Count==1` event yields exactly one entity; a timed-child event yields `HasChildSpawner==0` children.
3. **Next-tick spawn (R5):** a projectile that spawns an impact projectile on hit — the impact projectile does NOT collide/move on the same tick (assert it exists but its position/hit-count is unchanged until the next `Tick`).
4. **Unified lifetime — pulse:** a pulse AoE (`Lifetime<=0`, `CombatLifetimeComponent` disabled) is hit once then deactivated the same tick (port `PulseHitsOverlappingTargetOnce`); a lingering AoE counts down and expires (port `LingeringExpiresAndDeactivates`).
5. **Reuse:** spawn → expire → spawn reuses the same entity (port `PulseEntityIsReusedOnRespawn`).
6. **Convert-elimination parity:** an impact-AoE-on-projectile-hit produces the same AoE (type, position, lifetime, damage) it produced before the refactor (golden-value assertion).

## Manual gameplay / editor scenarios
- Open `Assets/Scenes/Main.unity`, enter Play mode. Fire the player's projectile attacks (held fire performs all ready attacks). Confirm: projectiles appear, move, hit mobs, deal damage; multi-shot/fan attacks produce the right shot count; child-spawning projectiles still spawn children over time; impact AOEs and impact-projectile bursts still appear on hit; mob projectiles damage the player.
- Watch the `DebugOverlay` counters: active projectiles, projectile sim ms, active AOEs, spawned/despawned per second, managed allocations per frame.

## Profiling checks (Unity Profiler)
- Markers to watch (existing): `Projectile.Spawn`, `Projectile.Spawn.ReuseJob`, `Aoe.Spawn`, `Aoe.Spawn.ReuseJob`, and counters `Projectile.Spawn.Cold` / `Projectile.Spawn.Reuse`. Keep equivalents on the new apply systems so regressions are visible.
- **Entities → Structural Changes** module: cold-create should remain the exceptional path (one ECB playback per apply system per frame). No new per-entity add/remove during reuse.
- Allocation: no new per-frame managed allocations in the spawn path (no LINQ, no closures in `OnUpdate`); reuse the bucket/list pools as the current systems do.
- Performance target context (Docs/architecture.md): ~50k projectiles, 20 targets, 120 fps. The rewrite must not regress spawn/sim ms at scale; profile a stress scene before and after Phase 2/3.

## Expected failure modes & debugging hints
- **Same-frame recursion / spawn explosion:** if a newly spawned entity moves/collides the same tick, ordering invariant 1 (next-tick) is broken — check that apply runs after all collision/lifetime/movement.
- **Lost spawns:** if attacks fire but nothing appears, the expansion finalize is probably not draining BOTH the queue and the scope buffer, or the producers are writing a different queue instance than the one expansion drains (confirm `World.GetExistingSystemManaged<ProjectileSpawnExpansionSystem>()` returns the same instance).
- **Double damage / no damage:** confirm exactly one system clears `CombatDamageElement`, and the bridge clears after replay.
- **Pulse AoE never hits or hits forever:** confirm pulse AOEs spawn with `CombatLifetimeComponent` disabled and are deactivated by collision; confirm the unified lifetime query excludes disabled-lifetime entities.
- **Container leaks (Unity logs "N persistent allocations"):** every `NativeQueue`/`NativeArray`/`NativeStream` created by a system must be disposed in `OnDestroy` (and per-frame temporaries disposed each frame), matching the current `ProjectileMultiExpandSystem`/`ProjectileSpawnSystem` disposal discipline. Cross-check `Docs/coding-standards.md` "Native And ECS Handle Ownership".
