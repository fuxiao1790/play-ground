# 001 — Carry + bake projectile detonation params

## Change kind: adapt

## Structural role
`DetonationSnapshot` currently describes an AOE detonation only (AoeGeometry + lifetime/tick).
This task lets it also describe a projectile-nova detonation, reusing the existing burst
snapshot type, and bakes/registers it at fire time.

## Ownership / data flow
- `DetonationSnapshot` ([StackEffectSnapshot.cs](Assets/Scripts/System/Common/StackEffectSnapshot.cs#L17))
  gains an `AoeProjectileBurstSnapshot ProjectileBurst` field, used when
  `Kind == StackDetonationKind.Projectile`. `AoeGeometry` stays the AOE-only field. The struct
  remains kind-tagged; the unused block for the other kind is accepted for single-path data flow.
- Baking: [SkillSpawnTranslator](Assets/Scripts/Skills/SkillSpawnTranslator.cs) builds the
  `ProjectileBurst` from the compiled projectile-detonation definition (type id, spread, speed,
  lifetime, radius, halfExtents, rotation, shape, pierce, visual). `Count`/`Damage` in the burst
  are **placeholders** — overwritten at detonation from the summed contribution (002).
- Registration: the registration walk ([PlayerSkillDriver](Assets/Scripts/Skills/PlayerSkillDriver.cs#L149))
  must register the projectile-detonation prefab template and store its `TypeId` into the snapshot,
  exactly as projectile applicators/impact projectiles are registered today. Without this,
  `TypeId < 0` and `DetonationSnapshot.Enabled` is false.

## Phase/order
Equip-time compile + registration, then fire-time bake in `SkillSpawnTranslator`. No per-frame work.

## Structural notes
- Reuse `AoeProjectileBurstSnapshot` rather than minting a parallel projectile-detonation struct
  (focus item 8: fewer types threaded through the pipeline).
- Contribution fields (`StackContribution.ProjectileCount`, `Damage`) already exist and already
  sum in the buffer — no buffer/contribution change here.

## Acceptance criteria
- A `StackingSkill` with `detonationKind = Projectile` compiles to a `DetonationSnapshot` whose
  `Kind == Projectile`, with a registered projectile `TypeId` and authored burst params.
- `DetonationSnapshot.Enabled` is true for a valid projectile detonation.
- AOE detonation snapshots are unchanged.

## Dependencies
None (atomic with 002).

## Scope
Medium.
