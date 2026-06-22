# Stacking via Support + Trigger — supersedes the wrapper

## Status: supersedes `.agent/stacking-skill/`

The wrapper design (`StackingSkill` bundling applicator + detonation →
`RuntimeStackingSkillDefinition`) is implemented but **does not compose**:
the wrapper is a sibling runtime type that every normal trigger link
type-checks and rejects —
`OnImpactAoe` (`is RuntimeAoeDefinition`), `ChildSpawn`/`OnImpactProjectile`
(`is RuntimeProjectileDefinition`). So a chain like
`projectile → travel-spawn → projectile → (lingering-aoe stacking)` silently
fails to wire. Bundling applicator + detonation into one node is the root cause.

## Design

Decompose the wrapper. The **applicator** becomes a normal skill (composes with
every link); **stacking** becomes a support category + a dedicated trigger.

- **Support taxonomy.** `SkillSupport` base; `AdditiveSupport` (stat mods,
  existing) and `ConversionSupport` (fundamentally changes the skill).
  `StackingSupport : ConversionSupport` carries the debuff config
  (threshold, lifetime, stacks-per-hit, name, cosmetic) and marks its set
  triggered-only.
- **`StackTrigger` link.** Pure wiring, applicator (cause) → stacking set
  (effect). At compile it bakes the effect's detonation into the applicator's
  fire-time stack payload.
- **`RuntimeStackingDetonation`.** The compiled stacking set =
  `{ detonation runtime (the set's own AOE/projectile), threshold, lifetime,
  stacks-per-hit, registration-minted debuff key }`. Reached **only** by
  `StackTrigger`.

## Why composition is fixed

Roles are split by type:
- **Applicator** → plain `RuntimeAoe`/`RuntimeProjectile` → every existing link
  accepts it. This is what `SetA → trigger → SetB` targets.
- **`RuntimeStackingDetonation`** → special, single consumer (`StackTrigger`),
  never faced by a normal link.

`RuntimeStackingDetonation` is the old wrapper *minus the applicator*; extracting
the applicator into a normal chain node is the fix.

## Mid-game safety (already built, unchanged)

The applicator bakes a self-contained `StackEffectSnapshot` at fire time (no live
reference to the stacking set); fizzle discards stale stacks; the registration key
keeps instances independent. A link is therefore safe — it is authoring-time
wiring compiled into a baked snapshot, which is why returning to a link does not
reopen the original mid-game objection.

## Decisions (no open structural questions)

- Config lives on the **support**; `StackTrigger` is pure wiring.
- `StackingSupport` is a `ConversionSupport` (own category), not `AdditiveSupport`.
- The compiled stacking set is its own type (`RuntimeStackingDetonation`),
  reached only via `StackTrigger`.
- Triggered-only via a `ConversionSupport.ConvertsToTriggeredOnly` flag read by
  root detection — not a new `SkillDefinitionTags` value (equivalent, simpler).
- Debuff key minted per `RuntimeStackingDetonation` instance (per stacking-set slot).
- **No authored name/cosmetics on the support.** It is pure accumulation config
  (threshold/lifetime/stacks-per-hit) + the triggered-only flag, so one asset is reusable on
  every stacking skill — never cloned to vary a label. The cosmetic name is **derived by the
  compiler from the linked skill's SO name**; visual flavor comes from the detonation skill's
  own VFX. Accrual identity is the minted key, independent of any name.

## Ownership / phases

| Concern | Owner |
|---|---|
| Debuff config (threshold/lifetime/stacks-per-hit) | `StackingSupport` |
| Wiring applicator → detonation | `StackTrigger` link (slot position) |
| Detonation effect | the stacking set's own skill |
| Debuff key | registration walk (per `RuntimeStackingDetonation`) |
| Stack accrual / fizzle / detonation spawn | `StackAccrualSystem` (single writer) — unchanged |

## Reuse (untouched)

The entire ECS side — `StackApplyEvent`, the `TargetStackEntry` buffer,
`StackAccrualSystem` (tick/fizzle/accumulate), `BuildDetonationSpawn` (AOE case;
projectile case stays the `.agent/projectile-detonation/` extension) — does not
change. `StackTrigger` feeds the same `StackEffectSnapshot` the applicator already
emits.

## Tasks

| # | File | Change | Summary | Depends |
|---|---|---|---|---|
| 001 | [001-support-taxonomy.md](001-support-taxonomy.md) | adapt | `SkillSupport` base; `AdditiveSupport`/`ConversionSupport`; retype `SkillSet.supports`; compiler dispatch by category | — |
| 002 | [002-stacking-support-and-runtime.md](002-stacking-support-and-runtime.md) | add | `StackingSupport`; compile a stacking set → `RuntimeStackingDetonation` (+ minted key) | 001 |
| 003 | [003-stack-trigger-and-baking.md](003-stack-trigger-and-baking.md) | add | `StackTrigger` link; applicator runtime gains the stacking payload; bake `StackEffectSnapshot` at fire time | 002 |
| 004 | [004-root-detection-and-validation.md](004-root-detection-and-validation.md) | adapt | triggered-only root detection; validate stacking sets reach a `StackTrigger` | 003 |
| 005 | [005-remove-wrapper.md](005-remove-wrapper.md) | remove | delete `StackingSkill`/`StackingSkillDefinition`/`RuntimeStackingSkillDefinition` and the wrapper special-cases | 004 |
| 006 | [006-docs-and-tests.md](006-docs-and-tests.md) | add | rewrite authoring docs; tests for the full chain, triggered-only, fizzle, validation | 005 |

## Sequencing & reviewability

- **001 is standalone green** — existing additive supports keep working.
- **002 is green-but-inert** (produces `RuntimeStackingDetonation`, unconsumed);
  **003 activates it.** Land 002+003 together for a working feature.
- 004 after 003; **005 removes the wrapper only after the new path works**
  (no compatibility flag left behind); 006 last.

## Constraints (document in code)

- The applicator stays a plain runtime type; only `StackTrigger` touches
  `RuntimeStackingDetonation`.
- Single writer of stack state remains `StackAccrualSystem`.
- Retyping `SkillSet.supports` to `SkillSupport[]` must preserve existing
  serialized `AdditiveSupport` references (same field name; common SO base).
- A stacking set unreached by a `StackTrigger` never fires — validation must
  surface it, never a silent no-op (focus item 7).
