# Mob Spawn Rework — Plan Index

## Summary

Build a new mob spawning system to replace the deleted `Assets/Scripts/Spawn/` code. Mobs
stay GameObject `MobRoot` actors but are **pooled** so hundreds can live/die without
`Instantiate`/`Destroy` churn. A single **`SpawnController`** MonoBehaviour owns a set of
authored **spawn points**, a weighted **mob table**, and two **pluggable ScriptableObject
strategies**: a *spawn behaviour* (when/how many) and a *placement* (where). The first
concrete strategies are **continuous stream** (hold live count near a cap via a rate) and
**fixed points**. Waves/director behaviours and ring/arena placements slot in later as
sibling SO subclasses with no controller change.

## Rationale for major decisions

- **Pool, don't re-instantiate** — the user's target is hundreds of concurrent mobs; the
  dominant cost of the naive path is GC + structural churn from per-death `Destroy` and
  per-spawn `Instantiate`. Pooling is the core perf lever.
- **Behaviour/placement as SO strategies** — the requirement is "configurable spawn
  behaviour" and "configurable spawn points on the controller." Strategy SOs keep the
  *rules* in the Game-Logic layer (where the docs say mob spawn rules belong) and the
  *controller/points* in Scene-and-Authoring, matching the layer boundary exactly.
- **Refactor MobRoot's teardown rather than add a parallel one** — see comparison below.

## Constraints & invariants (with sources)

1. **Mobs are Physics2D GameObjects; only target *proxies* enter ECS.** Jobs/systems must
   not read GameObjects/Transforms/Colliders. Source: `Docs/architecture/layer-rules.md`
   (Managed/Unmanaged split), `Docs/flows/mob-spawn-and-behaviour.md`. → The spawner is
   pure MonoBehaviour + SO; nothing here touches ECS directly.
2. **Spawn rules live in Game-Logic; controllers/points in Scene-and-Authoring.** Source:
   `Docs/layers/game-logic.md` ("Mob spawn rules once the replacement spawning system is
   authored"). → Behaviour/placement/table = SOs; controller/point = MonoBehaviours.
3. **Proxy lifecycle**: `CombatTargetRegistry.Register` → `CombatTargetProxy.Create` only
   creates a proxy when `IsCombatTargetActive` (active && alive && health>0); `Push` needs
   a non-null proxy; `OnDisable` deletes the proxy. Source: `Assets/Scripts/System/Targets/CombatTargetRegistry.cs`,
   `CombatTargetProxy.cs`, `MobRoot.cs`. → A pooled mob must be **active + freshly
   initialized before `Register`**, and re-`Register`ed every reuse (OnDisable clears
   `registries`). No `Entity` handle survives a disable, so reuse is clean.
4. **Prefer serialized/Inspector wiring over Awake-time injection** when values are known
   at edit time. Source: memory `feedback_inspector_over_awake_injection`. → Controller
   fields (table, behaviour, placement, points, cap, combatRoot, target) are serialized;
   `Bind()` is only a fallback.
5. **Keep the "no scene mobs is valid" state.** Source: existing test
   `GameRootAcceptsNoSceneMobsAfterSpawnerReset`. → `GameRoot.mobs[]` loop and
   `Configure(...)` signature stay intact.

## Mechanisms reused vs. introduced

**Reused**
- `MobRoot` combat APIs: `Register(CombatTargetRegistry)`, `BindCombatRoot`, `BindVfxRoot`,
  `SetTarget`, `SoftDied` event — the controller drives these exactly as `GameRoot` does
  today ([GameRoot.cs:52-67](../../Assets/Scripts/Game/GameRoot.cs#L52-L67)).
- Weighted prefab selection — ported from the deleted `MobSpawnPool.ChoosePrefab`
  (git `801e7423~1:Assets/Scripts/Spawn/MobSpawnPool.cs`).
- Existing `UnitStatSheet` for per-prefab health/speed; existing runtime asmdef covers
  `Assets/Scripts/Spawn/`.

**Introduced** (no existing equivalent — additive is correct here)
- `SpawnBehaviour`/`SpawnPlacement` strategy SOs + a per-controller runtime accumulator.
  There is no current spawn-timing mechanism to conform to, so this adds no duplicate path.

## Design validation vs. invariants

- (1) Nothing in the new code enters ECS; mobs still self-push proxies. ✓
- (2) Rules are SOs, controller/points are MonoBehaviours. ✓
- (3) Task 004 orders `Rent`→`SetActive`→`InitializeForSpawn`→`Register`; reclaim is
  deferred to end-of-frame to avoid re-entrancy inside `SoftDied`. ✓
- (4) All controller refs serialized; `Bind` is fallback-only. ✓
- (5) GameRoot changes are additive; existing loop/signature untouched. ✓

## Coding-standards conformance (`Docs/coding-standards.md`)

- **Root Component Rule** — `SpawnController` and `SpawnPoint` are root components: they own
  serialized refs, setup validation, event wiring, update order, and the public API. Actual
  spawn *decisions* (when/how many/where) live in SO strategies + focused runtime helpers, not
  in the controller. ✓
- **Awake vs OnEnable/Start boundary** — `Awake` does only *self-owned* construction
  (create inactive pool-root, `MobPool`, `rng`, `behaviourRuntime`, prewarm) and fail-fast
  validation. **All cross-MonoBehaviour work** (receiving `combatRoot`/`target` via `Bind`,
  beginning to spawn, `WireMob`→`Register`/`BindCombatRoot`) happens in `OnEnable`/`Start` or
  later at runtime — never in `Awake`. `MobRoot.InitializeForSpawn` is self-owned reset, so
  calling it from `Awake` for the first life is allowed. (Tasks 004, 005.)
- **Fail-fast validation** — required serialized fields (`table`, `behaviour`, `placement`,
  and `combatRoot` unless bound) validated once in `Awake`, throwing a clear setup error;
  `OnValidate` gives editor feedback. Gameplay methods then assume valid deps. (Task 004.)
- **Update timing / no expensive setup in runtime calls** — the behaviour tick is cooldown-
  class work (Update is fine). **Pool creation is a setup/preload step**: prewarm to the cap in
  `Awake` so steady-state gameplay does no `Instantiate`. (Tasks 002, 004.)
- **ScriptableObject Rule** — SO assets (`MobSpawnTable`, `SpawnBehaviour`, `SpawnPlacement`)
  hold only authored data; **all per-instance mutable state** (stream accumulator, active
  count) lives on runtime objects (`SpawnBehaviourRuntime`, the controller). ✓
- **Allocation Rule** — "mob spawn" is a named hot path: pooling removes Instantiate/Destroy
  churn; reclaim uses a reused `HashSet` + a method-group handler (no per-frame LINQ, no
  closure captures, no broad scene lookups during gameplay). (Tasks 002, 004.)
- **Test Hooks** — tests observe through public runtime APIs and real effects (reference
  equality of recycled `MobRoot`, `ActiveCount`, damage taken) — no production-only counters
  or flags added for tests. (Task 006.)
- **Unity Object Access** — the only scene scans (`FindAnyObjectByType<SpawnController>` in
  GameRoot, `GetComponentsInChildren<SpawnPoint>` in the controller) are one-time setup, not
  hot-path. ✓

## Minimal/additive vs. refactor comparison

The one place additive-vs-refactor bites is **`MobRoot` end-of-life**.

- **Minimal/additive** (keep `Destroy` path, add a separate "pooled mob" flag/branch):
  - resulting data flow: two teardown paths (self-`Destroy` vs pool-return) gated by a flag.
  - new concepts/types: a "pooled?" mode on MobRoot; duplicated reset logic.
  - copies/translations: none, but duplicate lifecycle state to keep in sync.
  - long-term cost: two mob lifetimes that must not diverge — a classic sync-bug surface;
    unclear owner of mob lifetime.
- **Refactor** (MobRoot has one teardown = "deactivate & clean"; the pool owns lifetime):
  - resulting data flow: `SoftDie`/`OnDisable` always disable + unregister + delete proxy;
    the controller/pool decides reuse vs. keep-parked. `InitializeForSpawn` is the single
    re-entry point for every life (including the first, called from `Awake`).
  - existing concepts changed: remove the `Destroy(gameObject)` tail in
    `DeleteCombatTargetProxy`; split `Awake` one-time init from per-life init.
  - copies/translations removed: no duplicate lifecycle branch.
  - long-term benefit: **one** mob lifetime, owned by the pool; first spawn and reuse share
    the exact same init path (no drift).
- **Decision: refactor.** The target design is clear and the additive path creates a
  duplicate-lifetime structural warning. One source of truth for mob lifetime = the pool.

**Default decision rule applied**: mob "end of life" was describable two ways (destroy vs.
recycle); collapse to one (`InitializeForSpawn` + disable), no compatibility reason to keep both.

## Task list

- [001-mobroot-pooling-refactor.md](001-mobroot-pooling-refactor.md) — split Awake vs
  per-life init, add `InitializeForSpawn()`/`IsAlive`, remove self-`Destroy`.
- [002-mob-spawn-table-and-pool.md](002-mob-spawn-table-and-pool.md) — `MobSpawnTable` SO
  (weighted pick) + `MobPool` (per-prefab recycle under inactive root).
- [003-spawn-behaviour-and-placement.md](003-spawn-behaviour-and-placement.md) —
  `SpawnBehaviour`/`ContinuousStreamBehaviour`, `SpawnPlacement`/`FixedPointPlacement`,
  `SpawnPoint`, `SpawnContext`, `ISpawnSink`.
- [004-spawn-controller.md](004-spawn-controller.md) — `SpawnController`: owns everything,
  ticks behaviour, wires each mob into combat, reclaims deaths.
- [005-gameroot-integration.md](005-gameroot-integration.md) — reference + fallback-bind
  the controller; keep static path intact.
- [006-playmode-tests-and-scene-wiring.md](006-playmode-tests-and-scene-wiring.md) —
  PlayMode tests + manual scene wiring/verification.

## Dependencies

001 is independent and unblocks the pool. 002 depends on 001 (`InitializeForSpawn`). 003 is
independent (interfaces + SOs). 004 depends on 002+003. 005 depends on 004. 006 depends on all.

## Open questions / considerations

- **Scale target** — The "player plus mobs below roughly 50" figure in
  `Docs/coding-standards.md` / `Docs/reference/design/gameplay.md` is **out of date**; the
  project already handles far more, so hundreds is the working bar and not a risk. (Those doc
  lines should be refreshed separately.) Pooling + Physics2D actors keeps hundreds viable
  without moving mobs into ECS; if a much higher bar ever walls on per-mob `Update`/Physics2D,
  a data-oriented mob update is the next lever (out of scope here).
- **MobRoot movement in `Update`** — existing `MobRoot` sets `body.linearVelocity` in `Update`,
  but the standard says Rigidbody2D movement belongs in `FixedUpdate`. Pre-existing and out of
  scope for the spawn rework; flag only.
- `SkillDriver` on a reused mob may carry cooldown/aim state across lives. Out of scope for
  this pass; flagged as a follow-up `SkillDriver.ResetRuntime()` hook (see 001).
- Cap magnitude for "hundreds" — pick a serialized default (~300) but leave it Inspector-set.
