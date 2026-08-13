# 006 — Tests and docs

## Change: Tests

### Rewritten

`Assets/Tests/PlayMode/MobSpawnControllerPlayModeTests.cs` needs two changes, and
both come from the protocol rather than from churn.

**1. Killing goes through ECS.** `MobRoot.SoftDie()` is called at `:26`, `:61`,
`:91` and looped at `:123-126`; task 005 deletes it. Replace with:

```csharp
Entity proxy = mob.CombatTargetProxy;
EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;
Health health = em.GetComponentData<Health>(proxy);
health.Current = 0f;
em.SetComponentData(proxy, health);
yield return null;    // frame N: sim detects, presentation pushes
yield return null;    // frame N+1: MobRoot hides, SoftDied fires
```

**2. Spawning takes an extra frame.** Every `controller.Spawn()` followed by an
immediate `SingleActiveMob()` assertion now needs a `yield return null` first —
the instance is disabled during the request frame, so
`FindObjectsByType<MobRoot>(FindObjectsInactive.Exclude)` (`:240`) will not see
it. `PoolReuseReturnsSameMobInstance` (`:20`),
`ReusedMobHasFreshPerLifeState` (`:85`), and
`ReclaimAccountingReturnsKilledMobsToPool` (`:113`) all hit this.

`ContinuousStreamHoldsCap` (`:39`) already ticks two frames and asserts
`ActiveCount == 3` with `cap: 3`. It should still pass, and it is now also the
regression test for the in-flight cap accounting — if `CanSpawn` forgets
`pendingSpawns.Count` (task 005), the first frame submits more than three and
this test catches it.

`SpawnPointSamplesInsideDisabledPolygonArea` (`:147`) is untouched.

### New

Six tests. The first four guard orderings — which is what this plan is made of —
and the last two guard the protocol's frame boundaries.

1. **Regen cannot revive.** Already exists as
   `ResourceRegenDoesNotReviveDepletedHealth`
   (`ProjectileCollisionSimulationTests.cs:150`) and is currently **failing**;
   task 001 makes it pass. No new test — just confirm it goes green.
2. **The player is not despawned.** Drop a proxy without `DespawnOnDeathTag` to
   zero health; assert the entity still exists and no `CombatDespawnEvent` was
   emitted. Guards `WithAll<DespawnOnDeathTag>()` in task 004.
3. **Death feedback reaches the actor.** Assert the killing blow's
   `ReceiveCombatTick` ran before `OnCombatDespawned`, via a test `ICombatTarget`
   recording call order. Guards `[UpdateAfter(CombatApplyBridge)]` in task 004.
4. **No double despawn.** Kill a mob, step three frames; assert
   `OnCombatDespawned` fired once and the instance is in `MobPool` exactly once.
   Guards Decision 4 (teardown deletes stay off the despawn lane) and Decision 6
   (same-frame destruction removes the need for a dedupe flag). **Highest value
   test here** — a double pool return hands the same mob to two rents, and the
   symptom appears far from the cause.
5. **Actor is never live without a proxy.** After `controller.Spawn()` and before
   the next frame, assert the instance is inactive and `CombatTargetProxy` is
   `Entity.Null`; after one frame, assert active with a valid proxy. This is the
   spawn protocol, asserted directly.
6. **Cancelled create destroys its orphan.** Submit a create, cancel it via
   `CombatTargetProxy.Delete` in the same frame, step one frame, assert no
   leaked entity carrying `TargetProxyTag` without a `TargetCompanion`. Guards
   the behaviour change task 002 introduces and task 003 handles.

Per `coding-standards.md` §*Test Hooks*, none may add production counters or
flags; a test-owned `ICombatTarget` recording call order is explicitly allowed.

## Change: Docs

### Close the two open TODOs

`Docs/architecture/phase-order.md:28` and `Docs/flows/runtime-frame.md:80` both
ask for the same thing: *"verify exact ordering between actor `LateUpdate()`
proxy deletion and all `PresentationSystemGroup` systems in the current Unity
player loop."*

The answer, from stock `ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop`
(`CombatEcsWorld.cs:32`) with no `ICustomBootstrap`: `SimulationSystemGroup` is
appended to the `Update` phase and `PresentationSystemGroup` to `PreLateUpdate`,
both after that phase's `ScriptRun…` subsystem. So one frame runs:

```text
MonoBehaviour Update()  ->  SimulationSystemGroup
  ->  MonoBehaviour LateUpdate()  ->  PresentationSystemGroup  ->  render
```

Record it and delete both TODOs — but **confirm it once with a frame-marker log
before deleting**, since the claim rests on documented Entities behaviour rather
than an observed trace in this project.

`phase-order.md` §*Inferred Frame Order* also needs rewriting: step 13 ("Actor
roots delete queued dead target proxies in `LateUpdate()`") describes behaviour
task 005 removes for mobs. After this plan it applies to the player only.

### Updated by this plan

- `Docs/coding-standards.md` §*Hybrid ECS/Scene Rule* — the managed-target
  paragraph names exactly two bridges permitted to read `TargetCompanion`. Add
  `CombatActorSpawnBridge` and `CombatDespawnBridge`, or a reader following that
  rule alone will correctly conclude both are illegal.
- `Docs/coding-standards.md` §*Update Timing* — "Use `LateUpdate()` for queued
  target proxy deletion on actors" now describes the player only.
- `Docs/contracts/target-proxy.md` — the proxy may carry `DespawnOnDeathTag`;
  registration resolves in `PresentationSystemGroup`, not in simulation; the
  create handshake now returns through `TargetProxySpawnResult`.
- `Docs/contracts/spawn-events-and-commands.md` §*Sibling Event Pipeline* —
  `TargetProxySpawnResult` and `CombatDespawnEvent` join the proxy event family.
- `Docs/flows/target-proxy-lifecycle.md` — the full two-frame handshake, both
  directions. This is the doc that most needs the new sequence diagram.
- `Docs/flows/runtime-frame.md` — step 2 says a registration handle "resolves on
  a later simulation tick", which becomes "resolves in the same frame's
  presentation push, and the actor goes live on the next `Update()`".
- `Docs/flows/mob-spawn-and-behaviour.md` — currently claims mob spawning was
  removed and step 1 describes "a future mob spawning system". Both false;
  rewrite against the real path.
- `Docs/layers/ecs-simulation.md` §*Owns* — the despawn-on-death decision.
- `Docs/layers/presentation-and-feedback.md` §*Owns* — the two new bridges and
  the actor spawn/despawn push. §*Does Not Own* keeps "ECS entity lifetime",
  which stays true: bridges notify, `TargetProxyDeleteApplySystem` destroys.
- `Docs/folder-structure.md` — the three new files.

### Also wrong, fix while here

`Docs/reference/game-logic/spawn-system.md` claims "The previous mob spawning
implementation has been removed. There is currently no runtime
`Assets/Scripts/Spawn/` implementation, no spawn-point scene objects, and no
spawn-pool ScriptableObject contract." All three clauses are false:
`SpawnController`, `SpawnPoint`, and `MobSpawnTable` exist and run.

### New

**ADR-007 — deferred spawn/despawn handshake.** Should record:

- the three-phase protocol and why the frame boundary is the point: an actor
  never runs a frame without a confirmed proxy, and never hides in the middle of
  a frame it was simulating in;
- that the push phase is `PresentationSystemGroup` and why that satisfies
  "LateUpdate";
- what deliberately did not move: the spawn decision, prefab knowledge, pooling,
  and actor presentation;
- its relationship to ADR-001, which is **not** overturned — movement,
  animation, and scene composition stay on GameObjects exactly as it says;
- why proxies are not pooled the way ADR-005 pools projectiles, so the
  divergence reads as a choice;
- the two accepted one-frame windows from [index.md](./index.md) §*Accepted
  consequences*, including the three-line `TargetProxyPendingTag` fix that was
  deliberately not built.

## Acceptance Criteria

- No test calls `MobRoot.SoftDie`.
- `ResourceRegenDoesNotReviveDepletedHealth` passes.
- The five new tests exist and each fails if its guarded attribute, `WithAll`, or
  deferral is removed.
- Both bridges are named in the `TargetCompanion` allowance.
- The two phase-order TODOs are answered and removed, after one confirming trace.
- ADR-007 exists and bounds itself against ADR-001 and ADR-005.

## Dependencies

All prior tasks.

## Scope

Medium — five new tests and one real doc rewrite (`target-proxy-lifecycle.md`);
the rest is mechanical.
