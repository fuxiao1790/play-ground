# 002 — StackingSupport + RuntimeStackingDetonation

## Change kind: add

## Structural role
Adds the stacking support and the dedicated runtime type the compiler produces for a
set that carries it. The set's own skill is the detonation; the support is the debuff.

## Ownership / data flow
- `StackingSupport : ConversionSupport`:
  - config: `stackThreshold`, `debuffLifetimeSeconds`, `stacksPerHit` only —
    **no authored name**. One support asset is reusable across every stacking skill;
    you never clone it just to vary a label.
  - `ConvertsToTriggeredOnly => true`.
- `RuntimeStackingDetonation : RuntimeSkillDefinition`:
  `{ RuntimeSkillDefinition Detonation; int StackThreshold; float DebuffLifetimeSeconds;
     int StacksPerHit; int DebuffKey = -1; ... cosmetic }`.
- Compiler: when a set's supports contain a `StackingSupport`, compile the set's skill
  into the detonation runtime (`RuntimeAoe`/`RuntimeProjectile`), then wrap it in a
  `RuntimeStackingDetonation` carrying the support's config. Additive supports in the
  same set still apply to the detonation skill.
- **Cosmetic name is derived, not authored.** The compiler sets the detonation's display
  name from the linked stacking set's skill (its SO name) — so a shared support never
  forces per-skill copies. Accrual identity remains the minted `DebuffKey`, independent of
  any name; the derived name is for UI/VFX/debug only.
- Registration walk ([PlayerSkillDriver](Assets/Scripts/Skills/PlayerSkillDriver.cs#L184)):
  register the detonation effect's type, and **mint a dedicated `DebuffKey`** per
  `RuntimeStackingDetonation` instance (monotonic, never reset — separate from the
  detonation type id).

## Phase/order
Equip-time compile + registration, before any spawn.

## Structural notes
- `RuntimeStackingDetonation` is produced here but consumed only in 003; until then it is
  inert (a triggered-only set with no `StackTrigger` simply never fires). The plan calls
  this out so the "green but inert" intermediate is intentional, not hidden.
- The debuff key is registration-minted (collision impossible, no authored key) — same rule
  the wrapper used; document it on the field.

## Acceptance criteria
- A set with a `StackingSupport` compiles to a `RuntimeStackingDetonation` wrapping its
  detonation skill, with config + a unique `DebuffKey`.
- Two sets (or two slots) with stacking supports get distinct keys.
- A set without a `StackingSupport` is unaffected.

## Dependencies
001. Green-but-inert until 003.

## Scope
Medium.
