# 002 — Gate Acquires The First Target

**Depends on:** 001. **Scope:** medium — one system, two contract fields.

## Change

`ExternalSpawnGateSystem.AppendInternalSpawn`
([ExternalSpawnGateSystem.cs:144-165](../../Assets/Scripts/System/Spawning/ExternalSpawnGateSystem.cs#L144-L165))
gains, for `IntervalChildKind.Targeted` only:

1. Read `TargetedSpawnTemplate` (same `SystemAPI.TryGetSingleton` pattern
   `TargetedSpawnExpansionSystem.OnUpdate` uses) and look up `request.TemplateKey` to get
   `command.Resolve.ChainDistance`. Missing template → append unchanged, no acquisition.
2. Read `TargetSpatialHashSingleton`, `BuildHandle.Complete()`, then
   `TargetedAcquisition.TryNearestHostile(snapshot, acquireAnchor, chainDistance, request.Faction)`.
3. On success: `AcquireAnchor = acquiredPosition`, `HasAcquiredTarget = 1`.
   On failure: anchor unchanged (raw cursor), `HasAcquiredTarget = 0`.

System attribute: add `[UpdateAfter(typeof(TargetSpatialHashSystem))]`. Cycle check is in
[index.md §4](./index.md#4-constraints-this-change-must-respect).

New fields, both `byte`, both with their `ECS Lifecycle:` comment updated (C12):

- `TargetedSpawnEvent.HasAcquiredTarget` ([TargetedSpawnPipeline.cs:13](../../Assets/Scripts/System/Targeted/TargetedSpawnPipeline.cs#L13))
- `TargetedSpawnCommand.HasAcquiredTarget` ([TargetedSpawnPipeline.cs:29](../../Assets/Scripts/System/Targeted/TargetedSpawnPipeline.cs#L29))

`TargetedExpansionCore.Stamp` copies the flag from event to command alongside `AcquireAnchor`.
`CombatRoot.SpawnTemplateFor(in TargetedSpawnCommand)` must clear it when normalising the
registry template, next to the existing `AcquireAnchor = default`
([CombatRoot.cs:772-788](../../Assets/Scripts/System/Core/CombatRoot.cs#L772-L788)) — otherwise the
flag becomes part of the template hash and splits identical templates.

## Constraints

- Only the targeted branch changes. Projectile and AOE requests keep their current path.
- No job scheduled here, so nothing is published into `ConsumerHandle` (C8).
- The scan is per targeted request; a cast is a rare event, not a per-entity hot path (C9, C11).

## Acceptance criteria

- A cast with a hostile inside `chainDistance` of the aim point produces an event whose
  `AcquireAnchor` equals that proxy's position and `HasAcquiredTarget = 1`.
- Same cast with only same-faction proxies in range → anchor unchanged, flag `0`.
- Nothing in range → anchor unchanged, flag `0`.
- Interval-child and on-hit chains are untouched: flag `0`, anchor as before (D2).
- Template-hash test: two casts of the same skill still resolve to one registry entry.
