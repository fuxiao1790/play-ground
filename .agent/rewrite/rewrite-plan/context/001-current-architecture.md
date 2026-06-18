# Context 001 — Current Architecture

> **Status banner (read first).** This file documents the **pre-Increment-1** cluster — the original "mess" the rewrite started from. Increment 1 (tasks 001–010) is now **landed in code**, so the literal symbols below (`ProjectileSpawnRequestElement`, `ProjectileMultiExpandSystem`, `CombatSpawnConvertJob`, the split lifetime systems, etc.) **no longer exist**. Keep this file as the historical "from" picture and for the relocated-logic references it still names accurately.
>
> **What Increment 2 actually changes from (the Increment-1 end-state):**
> - typed spawn pipeline exists: `ProjectileSpawnEvent` + `ProjectileSpawnCommandData` (awkward suffix), `TimedProjectileSpawnSystem`, `ProjectileSpawnExpansionSystem`, a **single bucketed** `ProjectileSpawnApplySystem`; AoE mirror; `CombatSpawnConvertJob`/`CombatPendingSpawn` already deleted;
> - occupancy is still **per-domain** `ProjectileActiveTag` / `AoeActiveTag` (Increment 2 → generic `Active`, Task 011);
> - spawn types use the **`...CommandData`** name and the managed authoring types are still called **`...SpawnCommand`** (Increment 2 → Event/Command rename, Task 012);
> - damage still flows `NativeStream → CombatHitFlushJob → CombatDamageElement` buffer, keyed by **`(TargetId, Faction)`**, dispatched by `DamageDispatchBridge` via the `targetsById` dictionary (Increment 2 → `Entity`-keyed `NativeQueue` transport + proxy/companion, Tasks 014/015);
> - targets are still synced into the **`CombatTargetElement`** buffer by `CombatTargetSyncSystem` (Increment 2 → proxy entities, Task 014);
> - lifetime is already unified (`CombatLifetimeSystem` + `AoePulseVfxSystem`).
>
> The "Disposition" notes further down reflect the **Increment-1** plan; the authoritative end-state is now `context/002` + `context/005`.

Snapshot of the combat spawn / event / damage / lifetime cluster **as it existed before Increment 1**, grounded in real files. Read this for the "from" picture and the relocated-logic references.

All paths are under `Assets/Scripts/`. Namespaces: `PlayGround.System.Common`, `PlayGround.System.Projectile`, `PlayGround.System.Aoe`, `PlayGround.System.Vfx`.

---

## 1. World, scope, ownership

- `System/Common/CombatEcsComponents.cs`
  - `CombatEcsWorld` — ref-counted owner of `World.DefaultGameObjectInjectionWorld`.
  - `CombatScopeOwner` — ref-counted owner of ONE shared `CombatScope` entity. On `Acquire` it creates the scope and adds buffers: `CombatTargetElement`, `CombatDamageElement`, `ProjectileSpawnRequestElement`, `AoeSpawnRequestElement`, `VfxSpawnRequestElement`.
  - There is exactly **one** scope entity for the whole world, shared by both factions and both domains. `CombatFaction` on each element disambiguates owner.
- `System/Common/CombatScope.cs` — `CombatScope : IComponentData` (tag) and `enum CombatFaction { None=0, Player=1, Mob=2 }`.
- `System/Common/CombatRoot.cs` — one MonoBehaviour per faction. Pure authoring/registry (no `Update`/`LateUpdate`). Public spawn entry points:
  - `Spawn(ProjectileSpawnCommand, int seedContactGateTargetId=0)` → builds a `ProjectileSpawnRequestElement` via `ProjectileRequestFor(...)` and appends it to the scope's `ProjectileSpawnRequestElement` buffer (main thread).
  - `Spawn(AoeSpawnCommand)` / `Spawn(ProjectileAoeSpawnRequest)` → builds `AoeSpawnRequestElement` via `AoeRequestFor(...)` and appends to the scope's `AoeSpawnRequestElement` buffer.

> NOTE: the **managed authoring** types `ProjectileSpawnCommand` (`System/Projectile/ProjectileSpawnCommand.cs`) and `AoeSpawnCommand` (`System/Aoe/AoeRuntimeEvents.cs`) are the public API used by `Mob/MobProjectileAttack.cs`, `Skills/SkillSpawnTranslator.cs`, and `CombatRoot.cs`. They are **out of scope to rename** (see decision log D-NAMING).

---

## 2. The spawn pipeline (the "mess" the rewrite targets)

### 2.1 The dual-use element — root of the problem
`System/Projectile/ProjectileEcsComponents.cs` lines 110–147 — `ProjectileSpawnRequestElement : IBufferElementData`. This single type is simultaneously:
1. **gameplay intent with multiplicity**: `Count`, `BaseDirection`, `Speed`, `SpreadDegrees`, `JitterDegrees`, `JitterSeed`;
2. **a per-entity command**: resolved `Position`, `Velocity`, `BoundsMin/Max`, `HitPayload`, `Tracking`, `Render`, `ChildSpawner`, `ChildSpawnState`, `HasChildSpawner`, `SeedContactGateTargetId`;
3. **a scope `IBufferElementData`** (the transport).

Its own doc comment calls it "Dual-use". This violates design R3 (event vs command) and R4 (phase separation). **This is the resolution of design §12's open question: the dual-use element + `ProjectileMultiExpandSystem` + `CombatSpawnConvertJob` together are the "main source of truth for the spawn mess".**

### 2.2 Current projectile spawn stages
1. **Producers** (all write the scope `ProjectileSpawnRequestElement` buffer):
   - Managed: `CombatRoot.Spawn` appends one element (may have `Count>1`).
   - `System/Projectile/ProjectileChildSpawnSystem.cs` — `ISystem`, `IJobEntity` over `(ProjectileTag, ProjectileActiveTag, ProjectileChildSpawnerTag)`. Timed catch-up loop; appends child elements (`Count=1`, `HasChildSpawner=0`) via `EndSimulationEntityCommandBufferSystem` ECB `AppendToBuffer` (parallel writer).
   - `System/Common/CombatSpawnConvertJob.cs` — single `IJob` run after collision; reads the collision `CombatPendingSpawn` stream and appends `ProjectileSpawnRequestElement` / `AoeSpawnRequestElement` directly onto the scope buffers via `BufferLookup`.
2. `System/Projectile/ProjectileMultiExpandSystem.cs` — `SystemBase`, `[UpdateAfter ProjectileCollisionSystem, AoeCollisionSystem]`, `[UpdateBefore ProjectileSpawnSystem]`. Copies the scope buffer to a `NativeArray`, `Clear()`s the buffer, then one `IJob` expands `Count>1` elements into `Count` individual elements (computes per-shot spread angle + jitter + per-shot velocity + per-shot render Z + per-shot id) into a `NativeStream PendingStream` (owned by this system, exposed via `PendingStream`/`PendingHandle` fields).
3. `System/Projectile/ProjectileSpawnSystem.cs` — `SystemBase`, `[UpdateAfter ProjectileCollisionSystem, ProjectileLifetimeSystem]`. Reads `ProjectileMultiExpandSystem.PendingStream` via `World.GetExistingSystemManaged`. Buckets requests into a `Dictionary<ProjectileSpawnKey, bucket>` keyed by `(faction, typeId, hasChildSpawner)`. Per bucket: a cached `WithDisabled<ProjectileActiveTag>` query + `SetSharedComponentFilter(CombatRenderFaction, CombatRenderTypeId)`, one `ProjectileSpawnJob : IJobChunk` that overwrites disabled slots and enables tags. Overflow → `CreateProjectileEntity` cold-create via `EntityCommandBuffer`. Two archetypes: `archetypeNoChildSpawner`, `archetypeWithChildSpawner` (built in `OnCreate`).

### 2.3 Current AoE spawn stages (simpler — no multi-shot, no expansion)
- `System/Aoe/AoeSpawnSystem.cs` — `SystemBase`. Reads scope `AoeSpawnRequestElement` buffer directly (no expand system). Buckets by `(faction, typeId)`. Same reuse-`IJobChunk` + cold-create pattern, single `archetype`. Also writes `VfxSpawnRequestElement` (Trigger=0 spawn VFX) inline while draining.
- `System/Aoe/AoeEcsComponents.cs` — `AoeSpawnRequestElement` (no multiplicity fields; AoE never fans out today).

---

## 3. Collision & consequence (current)

- `System/Projectile/ProjectileCollisionSystem.cs` — `ISystem`, `[UpdateAfter ProjectileContactGateSystem]`. `IJobEntity` over `(ProjectileTag, ProjectileActiveTag, ProjectileCollisionActiveTag, ...)`. Builds a per-faction spatial hash of `CombatTargetElement` (cell size 1). For each projectile:
  - Checks the contact gate **inline** (`IsGated`).
  - On qualified hit writes to **three separate `NativeStream`s**: `pendingDamage` (`CombatPendingDamage`), `pendingSpawns` (`CombatPendingSpawn` — carries `ImpactAoe` + `ImpactProjectile` snapshots), `vfxPending` (`VfxPendingSpawn`).
  - Mutates source state inline (pierce decrement, `Deactivate` disables `ProjectileActiveTag` + `CombatRenderActiveTag`), refreshes the contact gate inline (`AddOrRefreshGate`).
  - Three downstream jobs: `CombatHitFlushJob` (damage stream → `CombatDamageElement` buffer), `CombatSpawnConvertJob` (spawn stream → spawn request buffers), `VfxStreamFlushJob` (vfx stream → `VfxSpawnRequestElement` buffer).
- `System/Aoe/AoeCollisionSystem.cs` — `ISystem`, `[UpdateAfter AoeContactGateSystem]`, `[UpdateBefore ProjectileSpawnSystem, CombatRenderPrepareSystem]`. Same shape; cell size 64; spawn consequence is `ProjectileBurst` only; pulse AOEs self-deactivate after collision.

> **Observation:** collision already emits *separated* damage / spawn / vfx streams. The design's remaining gap is that the spawn stream is **generic** (`CombatPendingSpawn` carries every consequence kind) and is re-interpreted by `CombatSpawnConvertJob`, instead of collision emitting the **final typed** `ProjectileSpawnEvent` / `AoeSpawnEvent` directly (design §7.2).

- Gate-state maintenance: `System/Projectile/ProjectileContactGateSystem.cs` and `System/Aoe/AoeContactGateSystem.cs` — `ISystem`, `[UpdateAfter movement/sim, UpdateBefore collision]`. Each just ticks down `CooldownRemaining` on the contact-gate buffer and compacts expired entries. **These already match the design's narrowed "gate state maintenance only" role (§7.1).**

---

## 4. Damage & target bridge (current)

- `System/Common/CombatHitElement.cs` — holds the consequence snapshot structs (`ProjectileImpactAoeSnapshot`, `ProjectileImpactProjectileSnapshot`, `AoeProjectileBurstSnapshot`), the transient `CombatPendingDamage` / `CombatPendingSpawn`, and the scope buffer element `CombatDamageElement`.
- `System/Common/CombatHitFlushJob.cs` — `IJob`; copies the `CombatPendingDamage` stream into the scope `CombatDamageElement` buffer.
- `System/Common/CombatHitDispatchSystem.cs` — `SystemBase` in `PresentationSystemGroup`. Reads the `CombatDamageElement` buffer, groups consecutive entries by `(TargetId, Faction)`, rolls crit on the main thread with `UnityEngine.Random`, resolves the live `ICombatTarget` via `CombatRoot.TryGetByFaction(...).TargetsById`, calls `ICombatTarget.ReceiveHits(...)`, then `Clear()`s the buffer. **This already implements design §8.1 atomic-per-hit replay (no condensation).**
- Target identity is `TargetId` + `CombatFaction`, NOT an ECS entity. Targets are synced each tick from the managed registry into the `CombatTargetElement` buffer by `System/Common/CombatTargetSyncSystem.cs` (`OrderFirst`) via `CombatRoot.AppendTargetsToBuffer` → `CombatTargetSync.SyncToBuffer`. **There are NO per-target ECS proxy entities today** (design §8.2/8.3 describe a model that does not exist yet).

---

## 5. Lifetime (current — split, the design wants unified)

- `System/Projectile/ProjectileLifetimeSystem.cs` — `ISystem`, `[UpdateAfter ProjectileChildSpawnSystem, UpdateBefore ProjectileCollisionSystem]`. `IJobEntity` over `(ProjectileTag, ProjectileActiveTag)`: `RemainingLifetime -= dt`; on expiry disables `ProjectileActiveTag` + `CombatRenderActiveTag` and enqueues a despawn VFX (`Trigger=2`, area = `max(render.VisualScale.xy)`).
- `System/Aoe/AoeLifetimeSystem.cs` — `ISystem`, `[UpdateAfter AoeSimulationSystem, UpdateBefore AoeCollisionSystem]`. `AoeLifetimeJob` over `(AoeTag, AoeActiveTag)`: **skips pulse** (`IsPulse==1`) and zero/negative lifetime; otherwise counts down, disables on expiry, despawn VFX (`Trigger=2`, area = `AoeAreaComponent.Size`). A second `AoePulseVfxJob` emits periodic pulse VFX (`Trigger=3`) for lingering AOEs.

> The countdown+disable logic is identical between the two; the split is incidental (design §4). The AoE-only parts are: pulse skip, the pulse-VFX job, and the despawn-VFX area source (`area.Size` vs render scale).

---

## 6. Occupancy / active tags (current)

- Projectiles: `ProjectileActiveTag` (occupancy), `ProjectileCollisionActiveTag` (collision opt-in), `CombatRenderActiveTag` (render opt-in) — all `IEnableableComponent`.
- AOEs: `AoeActiveTag`, `AoeCollisionActiveTag`, `CombatRenderActiveTag`.
- Despawn disables the active tag; reuse query is `WithDisabled<...ActiveTag>` + shared-component filter. **This already realizes the functional intent of design §3** (occupancy = enableable flag, kind = component presence, reuse by `WithDisabled` query). The design's *generic* single `Active` flag is a naming/consolidation idea — see decision log D2 (deferred).

---

## 7. Frame ordering (current)

`SimulationSystemGroup`, `OrderFirst` systems: `CombatTargetSyncSystem` (clears + fills target buffer), `ProjectileSimulationSystem` (clears `CombatDamageElement`), `AoeSimulationSystem` (**also** clears `CombatDamageElement` — redundant double-clear; see T008). Then per `[UpdateBefore/After]`: tracking → movement → child-spawn → projectile lifetime → projectile contact gate → projectile collision → aoe sim/lifetime/contact-gate/collision → multi-expand → projectile spawn / aoe spawn → render prepare. `PresentationSystemGroup`: `CombatHitDispatchSystem`, `CombatBatchedRenderSystem`, `CombatVfxDispatchSystem`.

---

## 8. Tests (current)

- `Assets/Tests/PlayMode/AoeSimulationTests.cs` — builds a bare `World`, adds the AoE systems to a `SimulationSystemGroup`, manually creates a scope entity with the four buffers, drives `SpawnCircle`/`AddTarget`/`Tick`, asserts on `CombatDamageElement` length, active/total AOE counts, and entity reuse. **This is the canonical pattern new tests must follow.**
- `Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs`, `AoePlayModeTests.cs`, `BareMinimumPrototypePlayModeTests.cs`.
- EditMode: `CritEditModeTests.cs`, `ProjectileAuthoringEditModeTests.cs`, `SkillValidationEditModeTests.cs`, etc.
