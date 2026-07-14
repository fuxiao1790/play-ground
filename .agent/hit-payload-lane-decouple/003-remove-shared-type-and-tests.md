# 003 — Delete shared type, update tests & doc

## Goal

With both lanes owning their payload (001, 002), delete `CombatHitPayload` and fix
every remaining (test) reference. Update the contract doc.

## Change

### Delete the shared type
- Remove `Assets/Scripts/System/Application/CombatHitPayload.cs` (and its `.meta`).
- Confirm zero remaining references outside tests (grep `CombatHitPayload`).
  Expected production refs after 001/002: **none**.

### Update tests (both lanes)
Every test that constructs the payload must switch to the lane-owned type. Known
sites (from grep):

- **Projectile → `ProjectileHitPayload` (flattened, no nested payload):**
  - `ProjectileCollisionSimulationTests.cs` (:198, :289, :347, :436)
  - `ProjectileSpawnPipelineTests.cs` (:439, :482)
  - `ProjectileTrackingSimulationTests.cs` (:349)
  - `CombatPoolCleanupSystemTests.cs` (:188)
  - `ProjectileAuthoringEditModeTests.cs` (:99, :119)
  - `SpawnCommandUnificationTests.cs` (:188, :239) — these build projectile
    commands; retype to flattened `ProjectileHitPayload`.
  - `AoeSimulationTests.cs:1282`, `AoePlayModeTests.cs:203` — projectile payloads
    built inside AOE test files; flatten.
- **AOE → `AoeHitPayload`:**
  - `AoeSimulationTests.cs` (:1230, :1373, :1431, :1498)
  - `AoePlayModeTests.cs` (:254, :955, :1196 `FirstScopedAoeHitPayload` return type)
  - `ProjectileCollisionSimulationTests.cs:436` / others building AOE commands —
    retype as appropriate to the command being constructed.

Note: the `AoeSimulationTests` hit-queue tests (:1766, :1783) enqueue
`CombatHitEvent` directly — those are **unaffected** by this task (CombatHitEvent
is unchanged here); they only change in the follow-on source-lookup plan.

For each: replace `new ProjectileHitPayload(new CombatHitPayload { … }, onHit)`
with flat `new ProjectileHitPayload { …, OnHitSpawn = onHit }`, and
`new CombatHitPayload { … }` (AOE contexts) with `new AoeHitPayload { … }`.

### Update the contract doc
- `Docs/contracts/combat-hit-and-tick-results.md` — the "Fields / Shape" list names
  `CombatHitPayload`. Replace with the two lane payloads (`ProjectileHitPayload`,
  `AoeHitPayload`) and note they are independent per-lane types.

## Acceptance

- `CombatHitPayload` no longer exists anywhere in the solution.
- Full EditMode + PlayMode suites compile and pass (projectile + AOE + spawn +
  pool tests).
- Contract doc reflects the decoupled types.

## Scope

Medium — mechanical but touches ~10 test files. Purely construction-site retypes.

## Depends on

001 and 002 (must remove all production references first).
