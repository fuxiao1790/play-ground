# Deferred Spawn/Despawn Handshake: ECS Decides, GameObject Follows Next Frame

## Scope

Invert lifetime control between the GameObject and its proxy entity, using a
fixed three-phase frame protocol:

```text
Update()               actors submit intent; ECS consumes it and decides
LateUpdate()           ECS pushes results back to actors
next Update()          actors act on what was pushed
```

Nothing about prefabs, pools, sprites, or instance identity enters ECS. The
entity gains **one tag**. Everything else is event plumbing and timing.

## Frame positions

The world uses stock `ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop`
(`Assets/Scripts/System/Platform/CombatEcsWorld.cs:32`) with no `ICustomBootstrap`,
so the Entities default placement applies: `SimulationSystemGroup` appends to the
`Update` phase and `PresentationSystemGroup` appends to `PreLateUpdate`. Both are
*appended*, so each lands after that phase's `ScriptRun…` subsystem:

| # | Position | Runs |
|---|---|---|
| 1 | MonoBehaviour `Update()` | actors submit intent |
| 2 | `SimulationSystemGroup` | ECS consumes, decides, writes result buffers |
| 3 | MonoBehaviour `LateUpdate()` | — |
| 4 | `PresentationSystemGroup` | ECS pushes results to actors |
| 5 | next frame `Update()` | actors act |

**The push phase is 4, `PresentationSystemGroup`.** It satisfies "LateUpdate" in
the protocol — after simulation, before the next `Update()` — and it is the only
phase permitted to resolve managed companions (`coding-standards.md` §*Hybrid
ECS/Scene Rule*). That it runs *after* MonoBehaviour `LateUpdate()` is a bonus:
an actor cannot consume a pushed result in the same frame even by accident, so
"handled next `Update()`" is structural rather than a convention.

`Docs/architecture/phase-order.md:28` and `Docs/flows/runtime-frame.md:80` both
carry an unresolved TODO asking exactly this ordering. This plan answers it from
the bootstrap; task 006 records the answer and removes the TODOs. Worth
confirming once with a frame-marker log before deleting them, since the claim
rests on stock Entities behaviour rather than on an observed trace.

## Spawn protocol

```text
frame N  Update()        1. SpawnController rents a DISABLED GameObject
                         2. MobRoot requests a proxy -> TargetProxyCreateEvent
frame N  Simulation      3. TargetProxyCreateApplySystem creates the entity
                            -> TargetProxySpawnResult { Token, Proxy }
frame N  Presentation    4. CombatActorSpawnBridge pushes the result:
                            binds TargetCompanion, sets CombatTargetProxy,
                            calls ICombatTarget.OnCombatSpawned(proxy)
frame N+1 Update()       5. SpawnController enables the confirmed instance;
                            MobRoot runs its first frame
```

The actor never runs a frame without a confirmed proxy. Today it runs first and
asks after.

## Despawn protocol

```text
frame N  Update()        (nothing; the actor is alive and normal)
frame N  Simulation      1. CombatDespawnOnDeathSystem sees Health <= 0 on a
                            tagged proxy -> CombatDespawnEvent + TargetProxyDeleteEvent
frame N  Presentation    2. CombatDespawnBridge pushes: clears CombatTargetProxy,
                            calls ICombatTarget.OnCombatDespawned()  [records only]
                         3. TargetProxyDeleteApplySystem destroys the entity
frame N+1 Update()       4. MobRoot acts on the recorded event: hide, stop
                            physics, unregister, raise SoftDied
                            -> SpawnController reclaims to the pool
```

`OnCombatDespawned` **records**; it does not hide. Hiding is the actor's own work
in its own `Update()`, which is what "game obj handled these events in next
Update()" means.

## What changes

| File | Change |
|---|---|
| `ResourceRegenSystem.cs` | restore the depleted-health guard (pre-existing bug, task 001) |
| `ICombatTarget.cs` | three default members |
| `TargetProxyEvents.cs` | one byte on the create event; two new event structs |
| `CombatTargetProxy.cs` | `DespawnOnDeathTag`; copy the flag into the create event |
| `TargetProxyCreateApplySystem.cs` | stop touching managed state; emit a result instead |
| `CombatScopeOwner.cs` | two `AddBuffer` calls |
| `CombatActorSpawnBridge.cs` | new — spawn result push |
| `CombatDespawnOnDeathSystem.cs` | new — death detection |
| `CombatDespawnBridge.cs` | new — despawn push |
| `MobPool.cs` | `Rent` returns the instance disabled |
| `SpawnController.cs` | pending-confirmation list; enables on confirm |
| `MobRoot.cs` | deferred spawn/despawn handling; managed death decision deleted |

**Unchanged:** `MobSpawnTable`, `SpawnBehaviour`, `ContinuousStreamBehaviour`,
`SpawnPlacement`, `SpawnPoint`, `GameRoot`, `PlayerRoot`, every asmdef.

## Rationale

### Decision 1 — one tag, no unit identity in ECS

ECS needs one fact: *when this proxy's health hits zero, the actor should
despawn*. That must exist because the player is also a proxy with health and must
**not** be auto-despawned.

`DespawnOnDeathTag` is named for what it does, not for a domain. It comes from
the actor via `ICombatTarget.CombatDespawnOnDeath` (default `false`), riding the
existing `TargetProxyCreateEvent` as a byte. `MobRoot` returns `true`;
`PlayerRoot` inherits the default.

It cannot be inferred from `TargetFaction` — a player-faction summon would want
despawning while the player would not.

### Decision 2 — the create-apply system stops touching managed state

`TargetProxyCreateApplySystem` currently runs in `SimulationSystemGroup` and does
three managed things (`TargetProxyCreateApplySystem.cs:37-59`):
`CombatTargetProxy.TryTakePendingCreate` (dictionary of `ICombatTarget`),
`AddComponentObject(proxy, new TargetCompanion { Target = target })`, and
`target.CombatTargetProxy = proxy`.

`Docs/architecture/phase-order.md` §*Phase Ownership* already states plainly:
*"ECS simulation may not read managed target companions or live Unity objects."*
It does. The protocol requires moving that work to the push phase anyway, so the
existing violation is fixed as a side effect rather than as a separate errand.

After the change the simulation system only reads unmanaged event data, creates
the entity, and emits `TargetProxySpawnResult { Token, Proxy }`. The token
handshake and `pendingCreates` dictionary stay exactly as they are — they just
resolve one phase later, in `CombatActorSpawnBridge`.

### Decision 3 — the spawner owns the pending list, not the actor

The confirmation lands on `MobRoot` (`OnCombatSpawned`), but a disabled
GameObject does not run `Update()`, so it cannot enable itself. Something already
running must do it, and `SpawnController` is the natural owner: it rented the
instance and already has an `Update()` and a `pendingReclaim` list to mirror.

This is not only mechanical necessity. `CanSpawn` gates on `ActiveCount`
(`SpawnController.cs:29`), and confirmation takes a frame — so without counting
in-flight spawns, `ContinuousStreamBehaviour` at 50/s would submit an entire
frame's worth of unconfirmed requests against a stale count and blow through the
cap. **In-flight spawns must count toward the cap**, which requires the spawner
to track them.

### Decision 4 — a separate despawn event, not a reuse of `TargetProxyDeleteEvent`

Tempting to skip `CombatDespawnEvent` and notify on every delete, since one is
already queued. Wrong: `TargetProxyDeleteEvent` also fires from
`MobRoot.OnDisable` (`MobRoot.cs:155-159`) during ordinary teardown. Notifying
there would record a despawn on an actor already being returned, and
`MobPool.Return` has no double-return guard (`MobPool.cs:34-51`) — the same
instance lands on the free stack twice and gets handed to two rents.

Two events, because there are two distinct facts: *this actor died* and *this
entity should be destroyed*. Death emits both; teardown emits only the second.

### Decision 5 — bridge ordering inside the push phase

```text
PresentationSystemGroup:
  CombatApplyBridge              replay damage/status to the actor
  CombatActorSpawnBridge         push spawn results
  CombatDespawnBridge            push despawn results
  TargetProxyDeleteApplySystem   destroy entities
```

- **Despawn push after `CombatApplyBridge`** — the killing blow's damage is
  replayed through `ICombatTarget.ReceiveCombatTick` (`CombatApplyBridge.cs:103`)
  in the same frame. Pushing despawn first is survivable only because the actor
  does not hide until next frame; ordering it after keeps the guarantee
  independent of that, which matters if `OnCombatDespawned` ever does more than
  record.
- **Both pushes before `TargetProxyDeleteApplySystem`** — the bridges read
  `TargetCompanion` off the entity. After the delete system runs, it is gone.

`SpawnRejectionBridge` (`Assets/Scripts/System/Presentation/SpawnRejectionBridge.cs`)
is the exact precedent for both bridges: a presentation system that drains a
result lane, resolves `TargetCompanion`, and calls a default `ICombatTarget`
member.

### Decision 6 — no dedupe state on despawn

Death is detected in phase 2 and the entity is destroyed in phase 4 of the same
frame, so no second simulation pass ever sees a dead-but-present proxy. This
holds only because of Decision 5's ordering; if `TargetProxyDeleteApplySystem`
ever moves after the next simulation update, the death system silently starts
emitting duplicates every frame. Task 004 puts a comment in the file and task 006
puts a test on it.

## Accepted consequences of the protocol

Stated rather than discovered later:

- **One frame where the proxy exists and the actor is hidden** (frame N, between
  create and enable). The proxy is in the spatial hash with a correct seeded
  position, so a mob can take damage on the frame before it appears.
  If that matters: add a `TargetProxyPendingTag` at create, remove it in
  `CombatActorSpawnBridge`, and add `WithNone<TargetProxyPendingTag>` to
  `TargetSpatialHashSystem`'s query (`TargetSpatialHashSystem.cs:60-64`). Three
  lines. **Not built** — spawns are typically off-screen and the window is one
  frame. Written down so the fix is known if it ever bites.
- **One frame where the actor is visible but its proxy is gone** (frame N+1,
  between destroy and hide). This is the harmless direction — a corpse that
  cannot be hit for one frame.
- **Spawn latency is one frame.** A burst spawner reaches its cap one frame later
  than before. Decision 3's in-flight accounting is what keeps that from becoming
  an overshoot instead.

## Constraints And Invariants

- **ECS simulation may not read managed companions or live Unity objects.**
  Source: `phase-order.md` §*Phase Ownership*, `layer-rules.md` §*Managed And
  Unmanaged Data*. → Decision 2. After this plan, all managed contact is in the
  three presentation bridges.
- **Only presentation bridges may resolve `TargetCompanion`.** Source:
  `coding-standards.md` §*Hybrid ECS/Scene Rule*, which names two by name. → Two
  more join them; task 006 updates the list or the rule reads as violated.
- **`PlayGround.Sim` references no PlayGround assembly.** Source: `layer-rules.md`
  §*Package Boundary*. → Held trivially: bridges talk to `ICombatTarget`, already
  in Sim and already implemented by `MobRoot` (`MobRoot.cs:22`).
- **Proxy components never imply domain.** Source: `coding-standards.md`
  §*Hybrid ECS/Scene Rule*. → `DespawnOnDeathTag` is the discriminator; without
  it the death system would despawn the player.
- **Structural deletion is reserved for target proxy lifecycle cleanup.** Source:
  `layer-rules.md` §*Structural Changes*. → Held: destruction still happens only
  in `TargetProxyDeleteApplySystem`. This plan changes who *asks*.
- **Releases belong at the event that causes them, not at teardown.** Source:
  `coding-standards.md` §*OnDestroy Boundary*. → `MobRoot`'s death-path proxy
  deletion moves off `LateUpdate`/`OnDisable` onto the despawn event.
- **Cross-MonoBehaviour work belongs in `OnEnable`/`Start`, not `Awake`.**
  Source: `coding-standards.md` §*Awake vs OnEnable Boundary*.
- **Combat paths are allocation-light; "mob spawn" is a named hot path.** Source:
  `coding-standards.md` §*Allocation Rule*. → Pending lists are reused, never
  reallocated per spawn.
- **Agents do not run tests and do not edit Unity YAML.** Source:
  `Docs/project-overview.md`, project memory *Editor steps are user steps*.

## Design Validation

| Invariant | Held? |
|---|---|
| No presentation data in ECS | Yes — one tag; events carry `Entity` and an `int` token. |
| Simulation touches no managed state | Yes, and **improved** — Decision 2 removes an existing violation. |
| Player unaffected | Yes — no tag, defaults inherited. |
| Actor never live without a proxy | Yes — that is the spawn protocol's purpose. |
| Death feedback still reaches the actor | Yes — despawn push ordered after `CombatApplyBridge`. |
| Companion readable when bridges run | Yes — both ordered before `TargetProxyDeleteApplySystem`. |
| Regen cannot revive | Yes — ordering (task 004) plus the restored guard (task 001). |
| No double pool return | Yes — Decision 4 keeps teardown deletes off the despawn lane. |
| Cap respected under burst | Yes — Decision 3 counts in-flight spawns. |
| Structural change cost | Unchanged — one create per spawn, one destroy per death, as today. |

## Deliberately not built

Recorded so these read as decisions:

- **ECS-owned spawn decision.** Rate, placement, and the weighted table stay in
  `SpawnController`. Moving them would require prefab knowledge in ECS.
- **Entity pooling for proxies.** Today's code already creates and destroys a
  proxy per mob (`TargetProxyCreateApplySystem.cs:42`,
  `TargetProxyDeleteApplySystem.cs:38`). Pooling would add an `Active` gate on the
  proxy archetype, three query gates, dead-slot reuse, and full component reset —
  machinery for an unmeasured problem, with silent failure modes.
- **`TargetProxyPendingTag`** — see *Accepted consequences*.
- **Unit identity components, template registry, expansion stage, spawn
  commands.** Three earlier drafts of this plan carried these; all cut.

## Task List

| # | Task | Scope |
|---|---|---|
| [001](./001-fix-regen-revive.md) | Restore the depleted-health regen guard | Tiny, pre-existing bug |
| [002](./002-handshake-contract.md) | Interface members, tag, result/despawn events, create-system split | Medium |
| [003](./003-spawn-result-bridge.md) | `CombatActorSpawnBridge` | Small |
| [004](./004-despawn-lane.md) | `CombatDespawnOnDeathSystem` + `CombatDespawnBridge` | Small |
| [005](./005-gameobject-side.md) | `MobPool`, `SpawnController`, `MobRoot` deferred handling | **Large** |
| [006](./006-tests-and-docs.md) | Tests + docs, close the phase-order TODOs | Medium |

Sequential. 001 is independent and can land immediately. 005 carries the risk —
it is where the one-frame deferrals become real and where a missed line shows up
as a mob that never enables or never dies.

## Open Questions

None blocking. One thing to confirm during 002, called out because it is a
blocker if missed: `CombatTargetRegistry.TryCreateProxy` guards on
`target.IsCombatTargetActive` (`CombatTargetRegistry.cs:60-70`), and a disabled
GameObject reports `false` (`MobRoot.cs:109` tests `isActiveAndEnabled`). With
the actor rented disabled, registry-driven proxy creation **will not fire**. The
spawn request must therefore call `CombatTargetProxy.Create` explicitly, which
has no such guard (`CombatTargetProxy.cs:75`). Task 002 covers it.

## Testing

Per `Docs/project-overview.md`, agents do not run tests.

```
Unity.exe -runTests -batchmode -projectPath "e:/UnityHub/projects/play-ground" -testPlatform PlayMode -testResults "e:/UnityHub/projects/play-ground/TestResults/ecs-unit-spawn-playmode-results.xml"
```

Report only against the exported XML. `ResourceRegenDoesNotReviveDepletedHealth`
is expected to be **failing before 001** — capture a baseline run first so the fix
is visible.

## Editor Work (User)

None. No prefab, scene, or ScriptableObject change is required.
