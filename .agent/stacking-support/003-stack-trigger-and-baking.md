# 003 — StackTrigger link + applicator payload baking

## Change kind: add

## Structural role
Wires an applicator to a stacking set and bakes the detonation onto the applicator's
fire-time stack payload. This activates the feature (002 becomes live).

## Ownership / data flow
- `StackTrigger : TriggerLink` — `SourceSkillTags = Projectile | Aoe`, target = a
  stacking set. Carries no config (pure wiring; the support owns config).
- Applicator runtime: `RuntimeAoeDefinition` and `RuntimeProjectileDefinition` gain an
  optional `RuntimeStackingDetonation StackingDetonation` reference (analogous to a
  projectile's `ImpactAoeDefinition`).
- Compiler `StackTrigger` handler: compile the effect → `RuntimeStackingDetonation`; set
  `cause.StackingDetonation = thatDetonation`. (Cause is the applicator runtime.)
- [SkillSpawnTranslator](Assets/Scripts/Skills/SkillSpawnTranslator.cs): when an applicator
  has a `StackingDetonation`, build the `StackEffectSnapshot` from it (debuff key, threshold,
  lifetime, per-stack `1/threshold` contribution, detonation snapshot) — reusing the existing
  builder, now sourced from `RuntimeStackingDetonation` instead of the wrapper.

## Phase/order
Compile/bake at equip + fire time. The applicator emits `StackApplyEvent` on hit through the
**existing** collision path (`AoeCollisionCore`/`ProjectileCollisionSystem`) — no ECS change.

## Structural notes
- The applicator stays a plain `RuntimeAoe`/`RuntimeProjectile`; the stacking detonation is a
  by-reference payload it carries, so it still composes with every other link.
- `StackTrigger` only ever targets a `RuntimeStackingDetonation`; if the effect is not one,
  the bake is skipped and validation (004) warns — not a silent attach.

## Acceptance criteria
- `SetA → (normal trigger) → SetB → StackTrigger → StackSet` compiles so SetB (applicator)
  carries the baked `StackEffectSnapshot` and, on hit, detonates `StackSet` at threshold.
- The ECS accrual/detonation path is reached unchanged.

## Dependencies
002 (atomic — produces the type this consumes).

## Scope
Medium.
