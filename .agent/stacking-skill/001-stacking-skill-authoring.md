# 001 — StackingSkill authoring + compile + registration key

## Change kind: add

## Structural role
Introduces the self-contained skill that replaces the generic stack link. It owns its
applicator, detonation, threshold, and lifetime, and is the single authoring surface for
the stacking mechanic.

## Authoring
- `StackingSkill : Skill` (new concrete SO). Fields:
  - applicator definition (reuse `AoeDefinition`/`LingeringAoeDefinition` or
    `ProjectileDefinition` — the cast output that hits),
  - detonation definition (`AoeDefinition`/`ProjectileDefinition` — fired at threshold),
  - `stackThreshold` (X), `debuffLifetimeSeconds`,
  - optional cosmetic `debuffName` / `DebuffStatus` (UI/VFX only).
- `CreateAssetMenu` under `PlayGround/Skills/Stacking Skill`.

## Compile / runtime
- `RuntimeStackingSkillDefinition` (or extend the AOE/projectile runtime with stacking
  fields): applicator runtime def + detonation runtime def + threshold + lifetime.
- `SkillSetCompiler` compiles the applicator and detonation as normal sub-definitions
  (supports apply to each at compile, so per-stack contribution reflects current stats).

## Registration key (collision-impossible)
- During the existing registration walk ([PlayerSkillDriver.RegisterAoeTypes](Assets/Scripts/Skills/PlayerSkillDriver.cs#L184)),
  mint a **dedicated** debuff key (monotonic registration counter), distinct from the
  detonation type id, one per compiled stacking-skill instance.
- Stored on the compiled instance; never authored. Document: "Debuff key is a dedicated
  registration-minted id; uniqueness is structural, no validation needed. Kept separate from
  the detonation type id so detonation dedup cannot reintroduce collisions."

## Ownership / data flow / phase
- **Structural role:** sole authoring + compile surface for the stacking mechanic.
- **Ownership:** the SO owns authored config; the compiler owns the runtime def; the
  registration walk owns the debuff key — no other layer assigns it.
- **Data flow:** SO → `SkillSetCompiler` → `RuntimeStackingSkillDefinition` (+ key at register)
  → consumed by 005 (applicator emit) and 004 (detonation).
- **Phase/order:** equip-time compile + registration, before any spawn. No per-frame work.

## Detonation is kind-tagged, not AOE-bound (focus item 5)
- The detonation runtime def is polymorphic (a `RuntimeSkillDefinition`: AOE or projectile),
  and the baked `DetonationSnapshot` carries a **kind** tag.
- **Implement the AOE detonation path only** now; the projectile path is a localized later
  addition (authoring field + one dispatch case in 004 + the existing projectile spawn event).
  The accrual/buffer never change for it.
- Implementation is AOE-only; the structure is not limited to AOE.

## Acceptance criteria
- A `StackingSkill` SO compiles to a runtime def carrying applicator, detonation,
  threshold, lifetime, and a registration-assigned debuff key.
- Two distinct `StackingSkill` instances receive distinct keys with no authored input.
- `DebuffStatus`/name is carried as cosmetic only.

## Dependencies
None (slot-index compiler from prior work is reused).

## Scope
Medium.
