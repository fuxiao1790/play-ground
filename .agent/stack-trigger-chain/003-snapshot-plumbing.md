# 003 — Snapshot plumbing for the stack chain

## Structural role
Threads the 002 carrier through the AOE spawn snapshot path so a spawned AOE
materializes carrying its own chain, and **removes stacks from the damage path**
so each concern owns one typed channel.

## Ownership / data flow
Before: `CombatStatusEffectSnapshot` (single level) lives on `CombatHitPayload`
and rides `DamageReplayEvent` to managed `MobRoot`.
After:
- The 002 chain carrier replaces `CombatStatusEffectSnapshot` inside
  `CombatHitPayload.StackEffect` (or a renamed `StackChain` field).
- It flows: `AoeSpawnRequest` → `AoeSpawnEvent` → `AoeSpawnCommand` →
  `AoeHitSpawnComponent.HitPayload`.
- `DamageReplayEvent.StackEffect` is **removed** — damage no longer carries stacks.

## Change
- `ICombatTarget.cs`: replace `CombatStatusEffectSnapshot` with the chain carrier
  (or redefine `CombatStatusEffectSnapshot` to *be* the carrier). Update
  `CombatHitPayload` (snapshotting.md §Hit Payload) and `CombatHitData`.
- `AoeRuntimeEvents.cs`: `AoeSpawnRequest`/`AoeSpawnEvent`/`AoeSpawnCommand` carry
  the chain + faction/mask.
- `AoeSpawnApplySystem`: copy the chain into `AoeHitSpawnComponent.HitPayload`
  in both `RecordAoeReset` and the Burst `AoeSpawnJob`.
- `SkillSpawnTranslator.SpawnAoe`: pass the 002 carrier into `AoeSpawnRequest`.
- `DamageReplayEvent` (`CombatHitElement.cs`): drop `StackEffect`; remove its copy
  in `AoeCollisionCore.EmitHit` and `DamageDispatchBridge.ReplayDamage`.
- Update `Docs/snapshotting.md` §Hit Payload and §Stack Effect Resolution to the
  new owned path.

## Structural notes
- This is the boundary change that lets 004 read the chain off the AOE entity and
  emit the next stage. Keep the carrier identical in request/event/command so no
  per-phase transformation is needed (intent → allocation → component is a copy).
- Removing `StackEffect` from `DamageReplayEvent` is deliberate de-coupling, not a
  convenience: damage and stacks become independent typed channels.

## Acceptance criteria
- A spawned AOE entity's `AoeHitSpawnComponent.HitPayload` contains the full
  forwarded chain.
- `DamageReplayEvent` no longer references stacks; project compiles; damage
  replay path unchanged for direct damage.
- No managed authoring reference enters any snapshot (plain data only).

## Dependencies
002.

## Scope
Medium. Touches several plain-data structs and the AOE apply system; mechanical
but wide.
