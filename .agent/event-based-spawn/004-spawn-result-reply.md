# 004 — Spawn result reply bridge

Close the loop: replay `CombatSpawnResult`s to the managed caster. Behavior is a
ready-but-inert stub this task (the "consuming methods ready" requirement); mana fills
in the reaction.

## `ICombatTarget.ReceiveSpawnResult` (new default method)

Add to `ICombatTarget`, mirroring `ReceiveCombatTick`:

```csharp
void ReceiveSpawnResult(in CombatSpawnResult result) { }
```

Default no-op so no target is forced to implement it. `PlayerRoot` / `MobRoot` may
override with an empty body + `// TODO(mana): react to accept/reject` marker to make
the seam obvious, but no logic yet.

## `CombatSpawnResultBridge` (new managed system)

Structural copy of `CombatApplyBridge`:

- `[UpdateInGroup(typeof(PresentationSystemGroup))]` (ordering vs
  `CombatBatchedRenderSystem` does not matter; place it near `CombatApplyBridge`).
- `OnUpdate`:
  1. `CompleteDependency()`.
  2. `TryGetSingletonRW<SpawnResultSingleton>` (the lane from task 001/002); early-out
     if absent.
  3. Complete `ProducerHandle`; if `Results` not created or empty, return.
  4. For each `CombatSpawnResult`: resolve the caster the same way `CombatApplyBridge`
     resolves a target — `TargetCompanion` on `result.Caster` → `ICombatTarget`; skip
     `Entity.Null` / missing / no-companion / unusable targets.
  5. `target.ReceiveSpawnResult(in result)`.
  6. Clear the list (single-frame).

Reuse the `ResolveTarget` / `IsTargetUsable` helpers' logic from `CombatApplyBridge`
(extract to a shared helper if convenient, or duplicate the small guard — prefer
extracting to avoid a second copy of the companion-resolution rule).

## Acceptance criteria

- A produced result whose `Caster` is a live player/mob proxy reaches that caster's
  `ReceiveSpawnResult` (verifiable with a temporary counter in a test override).
- `Entity.Null` casters and dead/among-destroyed proxies are skipped without throwing.
- No gameplay behavior changes (stub is inert).

## Dependencies

001 (result type + lane) + 002 (intake produces results).

## Scope / complexity

Small-medium. One system + one interface default method; mirrors existing bridge.
