# 005 — Tests and docs

Validate the new path and update the design docs to describe request → intake → reply.

## Tests

- **Regression sweep.** Run the projectile/AOE play-mode suites
  (`BareMinimumPrototypePlayModeTests`, `AoePlayModeTests`,
  `ProjectileCollisionSimulationTests`, `AoeSimulationTests`,
  `SpawnCommandUnificationTests`). They call `CombatRoot.Spawn(...)` /
  `SkillSpawnTranslator.Spawn(...)` then advance the world; confirm the entity still
  appears on the same submit-then-update schedule (intake runs inside the same tick).
  Fix any test that asserted on the scope event *buffer* contents directly rather than
  on resulting entities.
- **New: request → intake.** A focused test that appends a `CombatSpawnRequest` to the
  scope buffer, advances one world update, and asserts the expected projectile/AOE
  entity exists (covers intake independent of `CombatRoot`).
- **New: reply routing.** With a stub `ICombatTarget` whose `ReceiveSpawnResult`
  increments a counter and a live proxy, submit a spawn with that caster and assert the
  counter advances; assert an `Entity.Null` caster produces no callback and no throw.
- **Caster propagation.** Assert a `SkillDriver`-driven cast puts the owner's
  `CombatTargetProxy` on the request (via a test intake shim or by exposing the last
  request in a debug hook), and that ownerless `CombatRoot.Spawn` uses `Entity.Null`.

## Docs

- [spawn-events-and-commands.md](../../Docs/contracts/spawn-events-and-commands.md):
  add the request stage — managed submits `CombatSpawnRequest` (cast intent + caster);
  `SpawnIntakeSystem` produces events; note the three-stage
  request → event → command model and that the managed scope event buffers are gone
  (single queue channel).
- [combat-bridge.md](../../Docs/layers/combat-bridge.md) /
  [combat-root-api.md](../../Docs/contracts/combat-root-api.md): the bridge now appends
  spawn **requests** (not events) and takes a caster proxy; ECS owns event
  construction and the reply.
- [runtime-frame.md](../../Docs/flows/runtime-frame.md): insert the intake step before
  spawn expansion and the spawn-result reply in the presentation step; note the reply
  routes by the same proxy/`TargetCompanion` binding as combat results.
- Add a short note that this is the mana prerequisite: the gate and the
  result payload fields land in the mana task, in `SpawnIntakeSystem` and
  `CombatSpawnResult`.

## Acceptance criteria

- All existing + new tests green.
- Docs describe the request container, the intake processor, the single event channel,
  and the reply, with the mana seam called out.

## Dependencies

002 + 003 + 004.

## Scope / complexity

Medium. Test updates + doc edits.
