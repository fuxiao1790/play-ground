# Merge ECS Combat Roots

## Why

Scope ownership is split across three MonoBehaviour classes, which is what makes
the scope problem hard to work with:

- `ProjectileRoot` — owns a projectile scope, projectile type registry + render
  resources, `Spawn(ProjectileSpawnCommand)`, render bridge, teardown.
- `AoeRoot` — same, for AOE.
- `CombatRuntimeRoot` — global coordinator: target-set registry (string-keyed),
  builds target snapshots each frame, pushes them into bound scope endpoints
  (`WriteTargets`), toggles `SetCombatRuntimeManaged`.

Instances today: 2 `ProjectileRoot` (player, mob) + 2 `AoeRoot` (player, mob) +
1 `CombatRuntimeRoot` ⇒ **4 scopes in one world**, each with its own target list.
Every collision result must carry a `Scope` to route back — the root cause of the
single-threaded scatter.

Merging is the prerequisite cleanup before world-as-scope and the per-archetype
stream reuse: it consolidates scope ownership into one place.

## Target

One `CombatRoot` class, **one instance per faction** (player-combat, mob-combat).
Each owns:

- the ECS world handle + **one shared scope entity** (not separate proj/aoe scopes),
- both type registries + render resources (projectile templates/render types AND
  AOE configs/types),
- both spawn APIs: `Spawn(ProjectileSpawnCommand)`, `Spawn(AoeSpawnCommand)`,
- its **single** target set + per-frame snapshot build + write into the shared
  scope's `CombatTargetElement` (absorbs `CombatRuntimeRoot`),
- the render/VFX presentation bridge for both domains, and teardown.

The shared scope holds: `CombatTargetElement` (shared by proj+aoe collision),
`CombatDamageElement` (shared), `ProjectileSpawnRequestElement`,
`AoeSpawnRequestElement`, `VfxSpawnRequestElement`.

This deletes `ProjectileRoot`, `AoeRoot`, `CombatRuntimeRoot` and the
`ICombatScopeEndpoint` indirection (`SetCombatRuntimeManaged`/`WriteTargets`
were only there to let `CombatRuntimeRoot` drive a separate root).

## What merges where

| Current | Becomes |
|---|---|
| `ProjectileRoot.BindWorld` + `AoeRoot.BindWorld` (each creates a scope) | one `BindWorld` creating one scope with all buffers |
| `renderResourcesByType` (per root) | one root holding projectile + AOE render resources |
| `CombatRuntimeRoot.Update` snapshot+WriteTargets | `CombatRoot.Update` builds its own faction snapshot, writes the shared scope's target buffer once |
| target-set string keys (Primary/Secondary) | each root owns one faction target set; key indirection drops |
| `ICombatScopeEndpoint` | gone (root owns its scope directly) |
| 4 spawn-request buffers across 2 scopes per faction | both request buffers on the one scope |

## Key challenges

1. **Render catalog must come off the scope.** `CombatScopeRenderCatalog` is added
   *to the scope entity* and is per-domain (projectile sprites vs AOE sprites). One
   shared scope can't hold both as a single component. Options: keep two catalogs
   resolved by domain, or move render resources to a static int-keyed registry like
   `CombatVfxRoot` already uses. **This is the main structural hurdle.**
2. **Scope tags.** The scope currently carries `ProjectileScope` *or* `AoeScope`;
   `ProjectileMultiExpandSystem` queries `ProjectileScope`, `AoeSpawnSystem` queries
   `AoeScope`. A shared scope needs **both tags**, or unify to one `CombatScope` tag
   and update those queries + the collision systems. Prefer one `CombatScope` tag.
3. **Routing trivializes — `CombatSpawnRouting` becomes unnecessary.** With one
   scope per faction, every internal spawn from that faction's collision (impact
   AOE, impact projectile, AOE burst) targets the **same** scope (the producing
   one). So the convert job writes to `identity.Scope` directly; the routing
   component + binder added in the prior rework can be deleted.
4. **Reuse keying unchanged.** `ProjectileSpawnSystem`/`AoeSpawnSystem` bucket by
   scope+type; one scope per faction means scope is constant, so effectively
   keyed by typeId. `CombatRenderScope` shared component = the one scope. Fine.
5. **GameRoot wiring** collapses from 4 roots + runtime root + routing binder to
   2 `CombatRoot`s; target registration goes through the root's own target set.

## Relationship to world-as-scope

This is **step 1**. After merging there are still 2 scopes (player, mob) in one
world, so collision still spans factions and `Scope` is still needed cross-faction.
**Step 2** (faction-per-world) then removes cross-faction mixing, and `Scope`
disappears entirely. Merge first because it puts world+scope ownership in one class,
making the world split a localized change instead of a 3-class coordination.

## Decisions (resolved)

- **Render resources → static int-keyed registry** (like `CombatVfxRoot`); ECS
  holds only an int. Decouples render data from the scope. (Resolves challenge #1.)
- **Scope of this step = merge roots only.** Single world retained;
  faction-per-world (`Scope` removal) is a separate step 2.
- One `CombatScope` tag (preferred) vs both `ProjectileScope`+`AoeScope` on the
  shared entity — confirm during planning; lean to one unified tag + updated queries.
- Naming: `CombatRoot` (working name).

## Status

Design only. Not started. Prior effort (internal-spawn-rework) is implemented but
uncompiled; note this merge will delete the `CombatSpawnRouting`/binder it added.


# Plan To Merge Combat Roots into `CombatRoot` (world-as-scope, step 1)

## Context

Scope ownership is split across three MonoBehaviours — `ProjectileRoot`,
`AoeRoot`, `CombatRuntimeRoot` — with 2+2+1 instances producing **4 scopes in one
world**, each with its own target list. That split is what makes the `Scope`
routing problem hard to work with and is the prerequisite blocker for both
world-as-scope and the per-archetype-stream parallel reuse.

This step merges the three classes into **one `CombatRoot` per faction**
(player-combat, mob-combat), each owning one world handle and **one shared scope**
that serves both projectile and AOE domains with a single shared target list
(already the intent — `GameRoot.BindCombatScopes` binds proj+aoe roots to the same
target-set key). Decisions already made: **render resources move to a static
int-keyed registry** (like `CombatVfxRoot`); **single world retained** —
faction-per-world (`Scope` removal) is a separate step 2.

Outcome: scope/world ownership lives in one class, one scope per faction, and
`CombatSpawnRouting` becomes unnecessary (internal spawns target the producing
scope). Sets up the world split as a localized follow-up.

## ⚠️ Dominant cost: serialized assets

`ProjectileRoot`/`AoeRoot` are referenced by **scenes and prefabs**, so this is
not a code-only refactor — merging the component types breaks serialization and
requires re-authoring:

- Prefabs: `Assets/Prefabs/System/ProjectileRoot_PlayerToMob.prefab`,
  `ProjectileRoot_MobToPlayer.prefab` (and the AOE root prefabs/objects).
- Scenes: `Assets/Scenes/BenchmarkLarge.unity`, `BenchmarkSmall.unity`,
  `Assets/_Recovery/0.unity`.
- Editor builder: `Assets/Editor/BareMinimumPrototypeBuilder.cs`.
- `ProjectSettings/TagManager.asset` + `GameplayTags.cs` if root tags change.

Plan: build new `CombatRoot` prefabs (one per faction, carrying both projectile
and AOE config) and rewire the scenes + builder to them. Old prefabs/objects retire
with the old types.

## Phases

### Phase 1 — Render resources off the scope (static registry)
- Replace managed `CombatScopeRenderCatalog { Dictionary Resources; Layer; Bounds }`
  with a blittable `CombatScopeRenderCatalog { int RootId }`, mirroring
  `CombatScopeVfxCatalog { VfxRootId }` (`CombatVfxRoot.cs:41-48`).
- Static `Dictionary<int, CombatRoot>` registry in `CombatRoot` (mirror
  `CombatVfxRoot.Registry`/`rootId`). The root holds per-domain render-resource
  dicts (projectile + AOE) + layer/bounds.
- `CombatBatchedRenderSystem` (`CombatBatchedRenderSystem.cs`): resolve the root by
  `RootId`, then submit per domain using **two tag-scoped render queries**
  (`WithAll<ProjectileTag>` / `WithAll<AoeTag>`) filtered by `(scope, typeId)` —
  tags disambiguate domains that share a `typeId` value on one scope. Resources
  come from the root, not a per-scope dict.

### Phase 2 — `CombatRoot` class (the merge)
New `CombatRoot : MonoBehaviour` merging the three. Owns:
- world handle + **one scope** created in a single `BindWorld`, carrying:
  `CombatTargetElement`, `CombatDamageElement`, `ProjectileSpawnRequestElement`,
  `AoeSpawnRequestElement`, `VfxSpawnRequestElement`, `CombatScopeRenderCatalog{RootId}`,
  `CombatScopeVfxCatalog`, `CombatDamageTargetSource`, `CombatTargetSyncSource`.
- both type registries + render resources; both `Spawn(ProjectileSpawnCommand)` and
  `Spawn(AoeSpawnCommand)` (reuse existing `SpawnRequestFor` bodies).
- its **own single target set** + `Update()` building the snapshot and writing the
  shared `CombatTargetElement` once — absorbing `CombatRuntimeRoot.Update` /
  `BuildSnapshot` / `WriteTargets`. The string-keyed multi-set indirection and
  `ICombatScopeEndpoint`/`SetCombatRuntimeManaged` drop.

### Phase 3 — Unify scope tag + system queries
- Add `CombatScope` tag; the shared scope carries it (retire `ProjectileScope`/
  `AoeScope`, or keep both on the entity — prefer one `CombatScope`).
- Update scope queries: `ProjectileSimulationSystem`, `AoeSimulationSystem`
  (clear loops), `ProjectileMultiExpandSystem.scopeQuery`, `AoeSpawnSystem.scopeQuery`,
  and the `WithAll<ProjectileScope>`/`<AoeScope>` queries in
  `ProjectileCollisionSystem`/`AoeCollisionSystem`.

### Phase 4 — Drop routing
- `CombatSpawnConvertJob` writes spawn requests to `identity.Scope` (the producing
  scope) directly — impact AOE → `AoeSpawnRequestElement`, impact/burst projectiles
  → `ProjectileSpawnRequestElement`, all on the same scope.
- Delete `CombatSpawnRouting` + `CombatSpawnRoutingBinder` (added in the prior
  rework) and the `GameRoot` binding call.

### Phase 5 — Managed call sites + assets
- `GameRoot`: two `CombatRoot`s; register mobs/player into each root's target set;
  drop `CombatRuntimeRoot`, the 4 root fields → 2, and the routing binder.
- Update API call sites: `MobSpawnerRoot` (`BindProjectileRoots`/`BindAoeRoot`/
  `BindCombatRuntime`), `PlayerSkillDriver.BindAoeRoot`, `MobRoot.BindAoeRoot`/
  `Register`, `MobProjectileAttack`, `DebugOverlay`, `SkillSpawnTranslator`.
- Re-author prefabs + scenes + `BareMinimumPrototypeBuilder` (see asset section).

### Phase 6 — Delete old + tests
- Delete `ProjectileRoot`, `AoeRoot`, `CombatRuntimeRoot`, `ICombatScopeEndpoint`
  (if fully internalized), `CombatSpawnRouting`, `CombatSpawnRoutingBinder`.
- Rework tests: `BareMinimumPrototypePlayModeTests`, `AoePlayModeTests`,
  `CombatRuntimeRootPlayModeTests` (its subject is being merged — repoint or
  remove), `AoeSimulationTests`, `ProjectileTrackingSimulationTests` (they build
  `ProjectileScope`/`AoeScope` by hand → use `CombatScope` + shared buffers).

## Risks

- **Serialized-asset breakage is the main risk** (prefabs/scenes/builder) — code
  won't fully compile/run until assets are re-authored to `CombatRoot`.
- Wide managed surface (~12 script call sites) changes in lockstep with the type
  merge — no clean half-compiling intermediate; treat as one landing.
- Render-registry change (Phase 1) is required for the shared scope and is the one
  genuinely structural sub-piece.
- Still step 1: `Scope` remains (2 scopes, one world) until the world split.

## Verification

- Editor compile (Console clean) — blocked while the editor holds the project lock;
  run when free.
- PlayMode suites in Test Runner: the reworked combat tests; assert a projectile/AOE
  hit produces its follow-up spawn on the shared scope same-frame.
- Visual run: projectiles **and** AOEs render correctly via the static registry
  (two-domain submit), impact AOEs / bursts appear, one shared target list drives
  both.
