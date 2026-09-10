# 005 — Doc updates

**Depends on:** [001-onhittrigger-collapse.md](001-onhittrigger-collapse.md),
[002-narrow-onhit-aoe-field.md](002-narrow-onhit-aoe-field.md),
[006-interval-trigger-attribute-removal.md](006-interval-trigger-attribute-removal.md).
**Scope:** Small, mostly deletion — four subsections merge into one, and the
interval section loses four fields.

## Pre-existing drift — do not fix here

`Docs/reference/game-logic/skill-system.md` is already stale in two ways that
belong to *other* work:

- It still documents `StackingSupport` and `ConvertsToTriggeredOnly`
  (`:686`, `:731-732`, `:807-808`, `:840`, `:1014`), which task
  `.agent/drop-stacking-support/004-doc-updates.md` owns and which is still
  marked Pending in that plan's implementation log.
- It still describes the legacy `LoadoutSlot` / `SerializeReference` model
  (`:444-491`, `:1062-1090`) rather than normalized `SkillLoadoutNode`.

Touch these lines only where an on-hit trigger name appears in them. Leave the
stacking and slot-model drift alone so the two doc tasks do not collide.

## `Docs/reference/game-logic/skill-system.md`

| Location | Change |
|---|---|
| `:605-677` | Replace the four subsections (`OnImpactAoeTrigger`, `OnImpactTargetedTrigger`, `OnImpactProjectileTrigger`, `OnAoeHitSpawnTrigger`) with a single **`OnHitTrigger`** subsection. See the shape below. |
| `:620-623` | The paragraph beginning "For a targeted effect, `IntervalSpawnTrigger` starts targeted-chain children…" is about `IntervalSpawnTrigger`, not about on-hit links — it is misfiled inside the `OnImpactAoeTrigger` section. Move it up into the `IntervalSpawnTrigger` section rather than deleting it. |
| `:818-828` | Collapse the three pseudocode blocks (`OnImpactAoeTrigger`, `OnImpactProjectileTrigger`, `OnAoeHitSpawnTrigger`) into one `if chain.link is OnHitTrigger` block that dispatches on the compiled effect type. Keep the `IntervalSpawnTrigger` and `StackTrigger` blocks as they are. |
| `:482-491` | Example slot lists naming `OnImpactAoe` → `OnHit`. |
| `:1013-1021` | Stacking example: `[TriggerLinkSlot: OnImpactAoe]` → `OnHit`, and the sentence "…so it still composes with `ProjectileIntervalSpawn`, `AoeIntervalSpawn`, `OnImpactAoe`, `OnImpactProjectile`, and `OnAoeHitSpawn`" becomes "…so it still composes with `IntervalSpawn` and `OnHit`." |
| `:1044-1048` | Second stacking example: `OnAoeHitSpawn` → `OnHit`. |
| `:1074-1080` | Chain examples: `OnImpactAoe` → `OnHit`. |
| `:1127-1150` | Parsed-chain diagrams: `OnImpactAoe` → `OnHit`. |
| `:695-698` | The trailing tag-validation paragraph mentions only `IntervalSpawnTrigger`; no change needed, but confirm nothing above it now dangles after the section merge. |
| `:500-520` | `IntervalSpawnTrigger`'s class listing (task 006): drop `projectileCount`, `sideSpreadDegrees`, `echoCount`, `scatterRadius`, leaving `energyPerSecond`. |
| `:544-551` | The paragraph on what the trigger carries: it now carries `energyPerSecond` and the inherited mana fields, nothing else. |
| `:583-585` | "`sideSpreadDegrees` on `IntervalSpawnTrigger` defines the projectile burst spread; `scatterRadius` … defines the AOE echo scatter" — delete. Replace with a sentence saying both come from the child set (`ProjectileDefinition.spreadDegrees`, `AoeDefinition.scatterRadius`, plus `MultipleProjectilesSupport` / `MultipleAoesSupport`). |
| `:591-603` | The SideSpray / stationary-source-forward and echo-scatter description **stays** — that is spawner behavior (index I12), not an effect attribute. Only the sentence naming the trigger's `scatterRadius` as the source of the disk radius changes to name the child's. |
| `:620-623` | "`echoCount` is additive with the child targeted definition's `echoCount`, floored to one" — delete the additive clause; the child's own `echoCount` is the value. This is the paragraph task 005 also relocates out of the on-hit section, so make both edits together. |

### Shape of the replacement subsection

Keep every mechanical detail the four sections carried — none of it is obsolete,
it just now lives under one heading:

- **What it does:** fires the effect set when the cause skill hits. What spawns
  is decided by the effect set's compiled type, not by the trigger.
- **The dispatch table** (source × target → runtime field), reproduced from the
  index's motivation table.
- **Compatible tags:** source `Projectile` or `Aoe`; target `Projectile`, `Aoe`,
  or `Targeted`. Note that targeted-as-source is not wired yet even though
  `RuntimeTargetedDefinition` declares the fields.
- **Projectile-target detail**, rewritten from `:653-668`. The `spawnCount` /
  `spreadDegrees` sentences are **deleted, not reworded** — the burst is now
  entirely the effect set's (`ProjectileDefinition.count` / `.spreadDegrees`,
  plus any `MultipleProjectilesSupport`), identical to casting that set
  directly. State that explicitly so the removal reads as a rule rather than an
  omission. Everything else in that passage survives verbatim: the burst
  originates at the impact point aimed back from impact and fans around that
  direction; proj→proj→proj nesting is unsupported and dropped with a compile
  warning; from an AOE source the burst is a flat `AoeProjectileBurstSnapshot`,
  so the spawned projectile's own chains are dropped with a compile warning;
  only top-level and interval-spawned AOEs carry the on-hit burst.
- **Aoe-target detail** (from `:609-613`): projectile source centers the AOE at
  the impact point; AOE source fires on the AOE's hit.
- **Targeted-target detail** (from `:627-629`): the child starts at the impact
  position and uses it as the acquisition anchor.
- **Class shape:**
  ```csharp
  class OnHitTrigger : TriggerLink { }
  ```
  With a line saying the trigger carries no attributes: it decides *when* the
  effect fires, the effect set decides *what* fires.
- Drop the "(the same field as `OnAoeHitSpawnTrigger`)" aside at `:612` — the
  duplication it described is what this change removed.

## `Docs/folder-structure.md:252`

`Assets/Scripts/Skills/Trigger/OnImpactTargetedTrigger.cs: targeted trigger` →
an `OnHitTrigger.cs` entry. Check the surrounding lines for entries covering the
other three deleted files and remove those too.

## Acceptance criteria

1. A search across `Docs/` for `OnImpactAoeTrigger`, `OnImpactProjectileTrigger`,
   `OnImpactTargetedTrigger`, `OnAoeHitSpawnTrigger`, `OnImpactAoe`,
   `OnImpactProjectile`, `OnAoeHitSpawn` returns nothing.
2. The trigger-types section has one on-hit subsection. `StackTrigger` is
   unchanged. `IntervalSpawnTrigger` lists only `energyPerSecond`, and its
   spawner-behavior description (SideSpray, stationary-source forward, echo
   placement) survives intact.
   - The doc states the ownership rule once, plainly: a trigger says when, the
     skill set says what, `TriggerLink` prices the link. Put it with the
     `TriggerLink` base description near `:491-497` rather than repeating it in
     each subsection.
3. No mechanical detail from the four old subsections is lost — check the
   projectile-burst caveats in particular. The deliberate deletions are the
   attribute descriptions: a search across `Docs/` for `spawnCount`,
   `projectileCount`, `sideSpreadDegrees`, `scatterRadius` and the trigger's
   `echoCount` returns nothing. `AoeDefinition.scatterRadius` and the child
   definitions' `count` / `spreadDegrees` / `echoCount` are different fields and
   should now be the ones the doc names.
4. The stacking and legacy-slot-model drift is untouched.
5. `RuntimeAoeDefinition.OnHitAoeSpawnDefinition` is described with its narrowed
   type (task 002) wherever the doc names it.
