# Unify on-hit triggers into a single `OnHitTrigger`

## Status
Planned — not started.

## Summary

Two changes under one rule: **a trigger says *when* an effect fires; the
triggered skill set says *what* fires.**

1. Replace the four spawn-on-hit `TriggerLink` subclasses —
   `OnImpactAoeTrigger`, `OnImpactProjectileTrigger`, `OnImpactTargetedTrigger`,
   `OnAoeHitSpawnTrigger` — with one `OnHitTrigger`. The compiler stops
   branching on trigger subtype and instead dispatches on the *compiled source
   type* × *compiled target type*, which is the matrix the runtime definitions
   already declare. (Tasks 001-005.)
2. Strip every effect-describing attribute off the trigger types that carry
   them, so the child skill set is the sole owner of its own burst size, spread,
   echo count and scatter. `OnHitTrigger` ends up with no fields at all;
   `IntervalSpawnTrigger` keeps only its cost fields. (Task 006, plus the field
   removals folded into 001.)

No runtime component, ECS system, or spawn-template path changes shape.

The folder is named for the first change because it is the larger one; the
attribute-ownership rule applies across both and is the reason task 006 lives
here rather than in a plan of its own.

## Motivation

The four types encode nothing that the compiled source/target types do not
already encode. Each one exists solely to name a cell in a 2×3 matrix that the
runtime already spells out as fields:

| source \ target | Projectile | Aoe | Targeted |
|---|---|---|---|
| `RuntimeProjectileDefinition` | `ImpactProjectileDefinition` | `ImpactAoeDefinition` | `ImpactTargetedDefinition` |
| `RuntimeAoeDefinition` | `OnHitProjectileSpawnDefinition` | `OnHitAoeSpawnDefinition` | `OnHitTargetedSpawnDefinition` |

Two of the four already overlap outright: for an AOE source,
`OnImpactAoeTrigger` and `OnAoeHitSpawnTrigger` write the *same field*
(`RuntimeAoeDefinition.OnHitAoeSpawnDefinition`). The design doc says so
explicitly at `Docs/reference/game-logic/skill-system.md:612` — "the same field
as `OnAoeHitSpawnTrigger`". The author-facing choice between them is
meaningless, and `OnAoeHitSpawnTrigger` additionally carries a hand-rolled
validation path (`ValidateAoeHitSpawnLink`, `SkillLoadoutValidator.cs:244-268`)
that re-implements the generic tag check for the same condition.

The player-facing consequence is a catalog with four near-identical entries
whose difference is only discoverable by knowing which runtime type the next
skill set compiles to — information the tag validator already computes.

The second problem is duplicated ownership of the effect's own attributes.
`OnImpactProjectileTrigger` carries `spawnCount` and `spreadDegrees`, but the
effect set already owns both twice over:

- `ProjectileDefinition.count` / `.spreadDegrees` (`SkillDefinition.cs:33-34`),
  read by `BuildRuntime` at `SkillSetCompiler.cs:295-296`.
- `MultipleProjectilesSupport.ApplyToProjectile`, which does
  `ctx.Count += count; ctx.SpreadDegrees += spreadDegrees` on the effect set's
  own def copy before `BuildRuntime` runs.

So a burst's size had three writers, and they did not even agree on the
composition rule: the trigger *added* to count but *overrode* spread. The
trigger's copies are removed and the effect skill set becomes the single owner
of what it spawns — which is the same rule that already governs a directly cast
projectile.

`IntervalSpawnTrigger` has the identical problem in four places
(`projectileCount`, `sideSpreadDegrees`, `echoCount`, `scatterRadius`), against
the same child-owned fields plus `MultipleAoesSupport` and
`MultipleChainsSupport`. Task 006 applies the same rule there. The clearest
evidence that the trigger-side copies are the anomaly: `SkillDriver.cs:853-857`
builds a projectile's *own* spawn template straight from `projDef.Count` and
`projDef.SpreadDegrees`, while `SkillSetCompiler.cs:445-448` builds the same
struct for an interval child and folds the trigger in. The same child compiled
as a root and as a triggered child currently disagree about itself.

## Scope

**In scope:** the four spawn-on-hit trigger types listed above.

**Out of scope (user decision):**
- `StackTrigger` stays its own type. It routes through the debuff/accrual
  runtime (`RuntimeStackingDetonation` → `StackEffectSnapshot` →
  `HitApplyFinalizeSystem` → `StatusProcessSystem`), not the spawn-template
  path, so its compile branch is genuinely different rather than a matrix cell.
  Its own refactor is recorded under `.agent/drop-stacking-support/` and is
  already implemented in source.
- `OnExpireTrigger` stays as-is despite having no compiler branch, no validator
  branch, `None`/`None` tags, and no catalog entry.
- `IntervalSpawnTrigger` keeps its identity — it is time/energy-driven, not
  hit-driven, so it is not unified into `OnHitTrigger`. Only its
  effect-describing attributes are removed (task 006).
- Targeted-as-*source* on-hit links stay unwired. `RuntimeTargetedDefinition`
  declares `OnHitAoeSpawnDefinition` / `OnHitProjectileSpawnDefinition`
  (`RuntimeTargetedDefinition.cs:28-30`) for a later task; the unified dispatch
  makes wiring them a two-line addition, but adding them here would be a
  behavior change, not a unification.

## Constraints & invariants

| # | Invariant | Source |
|---|---|---|
| I1 | A node has at most one outgoing trigger, so at most one on-hit link per compiled source. `BuildOnHitSpawnRef` returns the *first* non-null of projectile → aoe → targeted, so two simultaneous on-hit spawns on one AOE would silently drop one. | `SkillLoadoutNode` shape; `SkillDriver.cs:1000-1036` |
| I2 | Mana-cost multiplier ordering: the incoming trigger factor is stamped on the compiled target **only when the target is actually attached**, before chain aggregation walks it. | `SkillSetCompiler.cs:64-158`, `ApplyIncomingTriggerManaCostMultiplier` at `:545` |
| I3 | Triggered targets compile with `includeTriggeredManaCosts: false`; only the root call aggregates. | `SkillSetCompiler.cs:38-45, 160-162` |
| I4 | A projectile's burst size and spread are resolved once, in `BuildRuntime`, from the effect set's own `count` / `spreadDegrees` after its supports have mutated the def copy. `Count` is floored to 1. A triggered child must resolve them the same way a direct cast does. | `SkillSetCompiler.cs:170-178, 285-296`; `MultipleProjectilesSupport.cs:26-30` |
| I5 | Compilation is forward-only (`nodeIndex` strictly increases), so recursion terminates and no cycle is possible. | `SkillSetCompiler.cs:56-58` |
| I6 | Spawn-chain depth is bounded by `SpawnTemplateLimits.MaxSpawnChainDepth`; exceeding it emits `SpawnChainDepthExceeded`. Depth is counted by the `SkillDriver` registration walk, not by trigger type. | `RuntimeAoeDefinition.cs:52-54`; `SkillDriver.cs:736-771` |
| I7 | Trigger legality is reported by generic tag validation (`UnsupportedTriggerSource` / `UnsupportedTriggerTarget`) driven by `SourceSkillTags` / `TargetSkillTags`. Only `IntervalSpawnTrigger` escalates a source mismatch to Error. | `SkillLoadoutValidator.cs:227-241` |
| I8 | Trigger subtype is invisible to UI, save/load, loadout editing, and validation-warning rendering — every consumer outside the compiler and validator handles `TriggerLink` polymorphically. | `TriggerCatalog.cs`, `SkillLoadout.cs`, `SkillLoadoutEditCommand.cs`, `SkillLoadoutRestoreNode.cs`, `PlayerSaveController.cs`, `SkillLoadoutUi.cs` |
| I9 | Trigger assets are referenced only by `TriggerCatalog.asset`. No loadout, prefab, or scene references a trigger asset by GUID; player loadouts are assembled at runtime from the catalog. | GUID sweep over `Assets/**/*.{asset,prefab,unity}` |
| I10 | Unity editor work (asset creation, catalog editing, deletion) is performed by the user; `.asset` / `.meta` YAML is never hand-edited. | Project convention |
| I11 | An AOE's echo count and scatter, and a targeted chain's echo count, are resolved onto the compiled child the same way — definition field, then supports, then `BuildRuntime`. The template builders already fall back to the child (`ScatterRadius = scatterRadiusOverride ?? child.ScatterRadius`, `EchoCount = Mathf.Max(1, child.EchoCount)`), so removing the trigger-side override restores the fallback rather than losing a value. | `SkillSetCompiler.cs:352-353, 372`; `SkillDriver.cs:1505, 1539` |
| I12 | Interval children spawn with the `SideSpray` pattern from the source's perpendicular; a stationary source rolls a fresh forward each energy tick, seeded per spawner instance. The pattern is the spawner's concern, not the child's. | `SkillSetCompiler.cs:447`; doc `:591-599` |

## Mechanisms reused vs. introduced

**Reused — nothing new is invented:**
- Generic tag validation (I7) absorbs `ValidateAoeHitSpawnLink` wholesale.
  `CanSourceAoeHitSpawn` / `CanTargetAoeHitSpawn` test
  `definition is AoeDefinitionBase`, which is exactly what the `Aoe` tag means.
- Runtime-type dispatch on the compiled definition is already how every branch
  ends (`if (runtime is RuntimeProjectileDefinition) … else if (runtime is RuntimeAoeDefinition)`);
  the change hoists that dispatch up one level and drops the redundant outer
  subtype test.
- Burst size, spread, echo count and scatter already have an owner: the effect
  set's own definition fields plus `MultipleProjectilesSupport` /
  `MultipleAoesSupport` / `MultipleChainsSupport`. Dropping the trigger's copies
  does not remove the capability — it routes it through the mechanism that
  already exists and that a directly cast skill already uses.
- The template builders already contain the fallback the removal needs
  (`scatterRadiusOverride ?? child.ScatterRadius`), so task 006 deletes an
  override path rather than adding a default path.

**Introduced:** one type, `OnHitTrigger`, with **no fields of its own** beyond
what `TriggerLink` gives every link (UI fields and the two mana-cost factors).

**Removed:**
- Four types (`OnImpactAoeTrigger`, `OnImpactProjectileTrigger`,
  `OnImpactTargetedTrigger`, `OnAoeHitSpawnTrigger`) and four compiler branches
  collapsed into one.
- Six duplicated attributes: `spawnCount` / `spreadDegrees` on the on-hit
  trigger, `projectileCount` / `sideSpreadDegrees` / `echoCount` /
  `scatterRadius` on `IntervalSpawnTrigger` — plus the compiler code that folded
  each into the child.
- Three runtime setup fields that only existed to carry them:
  `RuntimeAoeIntervalSpawnSetup.Count`, `.ScatterRadius`,
  `RuntimeTargetedIntervalSpawnSetup.EchoCount`.
- Two template-builder parameters (`BuildAoeTemplate`'s `echoCount` and
  `scatterRadiusOverride`) and one overwrite line
  (`SkillDriver.cs:937`).
- One bespoke validator path (`ValidateAoeHitSpawnLink` +
  `CanSourceAoeHitSpawn` + `CanTargetAoeHitSpawn`) and two downstream
  `is RuntimeAoeDefinition` guards (task 002).

## Design validation

- **I1** — unchanged. One trigger per node before and after; the unified branch
  writes exactly one field per compile, same as each old branch did.
- **I2** — the unified attach helper returns a bool, and the caller stamps the
  mana factor only on `true`. This reproduces the old behavior precisely: each
  old branch stamped inside the successful source-type arm and skipped the stamp
  on a type mismatch.
- **I3** — the recursive `CompileInternal` call keeps `includeTriggeredManaCosts: false`.
- **I4** — the attach helper stops writing `Count` and `SpreadDegrees`
  altogether. The compiled child keeps exactly what `BuildRuntime` resolved from
  its own definition and supports, so a triggered projectile and a directly cast
  one now resolve their burst identically. The `Mathf.Max(1, …)` floor still
  lives in `BuildRuntime` (`:294`), so nothing loses the floor.
- **I5** — recursion structure untouched; still exactly one forward call.
- **I6** — depth accounting reads runtime fields, never trigger types. Unchanged.
- **I7** — `OnHitTrigger` declares `Source = Projectile | Aoe` and
  `Target = Any` (= `Projectile | Aoe | Targeted`). Every source/target pair the
  four old triggers accepted still validates; every pair they rejected still
  warns, now through the generic path. The one narrowing lost is
  `OnAoeHitSpawnTrigger`'s `Aoe`-only source restriction, which was redundant
  because a projectile source with an AOE target was already legal via
  `OnImpactAoeTrigger` and compiles to the same place.
- **I8** — no consumer needs a change; the catalog holds `TriggerLink`
  references and the UI reads `DisplayName` / `Description` / `Icon`.
- **I9** — the asset migration is confined to `TriggerCatalog.asset`.
- **I10** — asset work is task 004, written as user steps. Task 006 needs no
  asset work at all: the removed keys simply stop being read.
- **I11** — task 006 deletes the overrides and lets the existing fallbacks fire.
  The one place the fallback did not exist —
  `RegisterTargetedIntervalTemplate`'s `template.EchoCount = …setup.EchoCount`
  — was *overwriting* a value `BuildTargetedTemplate` had already set correctly
  from the child, so deleting the line leaves the correct value standing.
- **I12** — `SideSpray` stays hardcoded in the projectile arm. It is the only
  part of the old `Behavior` construction that is genuinely the spawner's, so it
  is the only part kept.

## Minimal/additive vs. refactor comparison

**Minimal/additive approach** — keep the four types, add a fifth "generic"
`OnHitTrigger` alongside them:
- *Resulting data flow:* five authoring types feeding the same six runtime
  fields; the compiler keeps four subtype branches and gains a fifth.
- *New concepts/types:* one, on top of four that now overlap it entirely.
- *Copies/translations added:* none, but the catalog gains a fifth entry whose
  relationship to the other four is "does what any of them do."
- *Long-term cost:* the authoring surface permanently has two ways to express
  every on-hit link, and the doc has to explain when each is correct. This is
  the "parallel old/new systems that must stay in sync" warning outright.

**Refactor approach** — replace the four with one:
- *Resulting data flow:* one authoring type → one compiler branch → the same
  six runtime fields, selected by the types that already determine them.
- *Existing concepts/types changed or removed:* four trigger classes and three
  assets deleted; one fieldless class and one asset added; one bespoke validator
  path deleted; two duplicated attributes deleted.
- *Copies/translations removed:* the subtype→field lookup table that currently
  lives as class identity is replaced by the type test each branch already
  performed anyway. The third writer of burst count/spread is removed, leaving
  one owner. One dead assignment path is removed (see behavior changes).
- *Long-term benefit:* adding a fourth runtime skill kind means adding one arm
  to one helper, not three new trigger classes. The catalog shrinks from four
  on-hit entries to one.

**Decision: refactor.** The additive path creates a duplicate authoring surface
for a concept that has exactly one meaning. The target design is unambiguous —
the runtime field matrix already *is* the design.

## Default decision rule

When two representations describe the same domain concept, collapse to one
source of truth unless a concrete migration or compatibility reason forbids it.
Here the concept is "when this skill hits, run the next skill set"; the four
types are four spellings of it, and the migration cost is one catalog asset
(I9). Nothing forbids the collapse.

## Intended behavior changes

These are the only differences a player or author can observe:

1. **The catalog offers one on-hit trigger instead of four.** Choosing the
   effect skill set now fully determines what spawns.
2. **AOE → AOE no longer has two spellings.** Previously `OnImpactAoe` and
   `OnAoeHitSpawn` both expressed it; now there is one.
3. **AOE source + non-AOE target via the old `OnAoeHitSpawnTrigger` stops being
   a silent no-op.** That branch (`SkillSetCompiler.cs:126-134`) assigned any
   `compiledTarget` to `OnHitAoeSpawnDefinition`, but every consumer guards with
   `is RuntimeAoeDefinition` (`SkillDriver.cs:1017, 1045`), so a projectile or
   targeted effect wired that way registered spawn templates and sounds and then
   never fired. After unification it routes to
   `OnHitProjectileSpawnDefinition` / `OnHitTargetedSpawnDefinition` and works.
4. **`ValidateAoeHitSpawnLink`'s two bespoke warnings become the generic
   `UnsupportedTriggerSource` / `UnsupportedTriggerTarget` warnings.** Same
   codes, same severity, slightly different message text.
5. **Impact-projectile bursts get smaller and narrower unless the effect set is
   retuned — this is a real balance change.** The shipped
   `OnImpactProjectileTrigger.asset` carries `spawnCount: 4` and
   `spreadDegrees: 360`, applied to whatever projectile set followed it. After
   the change the effect set alone decides: a set with `count = 1` and no
   `MultipleProjectilesSupport` fires one projectile at its own spread. Restoring
   the old feel is authoring work on the effect set (its `count` /
   `spreadDegrees`, or a `MultipleProjectilesSupport`), covered in task 004.
6. **Interval projectile waves lose one projectile and take the child's spread
   instead of a flat 30°.** `IntervalSpawnTrigger.asset` ships
   `projectileCount: 1` and omits `sideSpreadDegrees`, so the C# default `30f`
   applied to every interval projectile child regardless of what that set
   authored.
7. **An interval AOE child now scatters if its own set says so.** The trigger
   overrode scatter with its own field, which the asset omits — so the effective
   value was a forced `0`. Sets authoring `scatterRadius`, or carrying
   `MultipleAoesSupport`, will now actually scatter. Interval targeted children
   are unaffected: the trigger's `echoCount` defaulted to `0`, an additive
   identity.

No warning code is added or removed; `SkillValidationWarningCode` is untouched.

## Tasks

| # | Task | Depends on | Scope |
|---|---|---|---|
| 001 | [Introduce `OnHitTrigger`, collapse the compiler and validator](001-onhittrigger-collapse.md) | — | Medium |
| 002 | [Narrow `RuntimeAoeDefinition.OnHitAoeSpawnDefinition` to `RuntimeAoeDefinition`](002-narrow-onhit-aoe-field.md) | 001 | Small |
| 003 | [Test migration](003-test-migration.md) | 001 (same commit) | Small |
| 004 | [Asset and catalog migration](004-asset-and-catalog-migration.md) | 001 | Small, user-performed |
| 005 | [Doc updates](005-doc-updates.md) | 001, 002, 006 | Small |
| 006 | [Strip effect attributes from `IntervalSpawnTrigger`](006-interval-trigger-attribute-removal.md) | — | Medium |

Tasks 001 and 003 must land in the same commit: the EditMode and PlayMode
assemblies reference the four trigger types directly, so a split leaves the test
assemblies uncompilable.

Task 006 is independent of 001-005 and can land in either order; it touches
`ApplyIntervalSpawn`, which the on-hit work does not. Doing 001 first keeps each
diff smaller.

## Resolved decisions

- `StackTrigger` is not folded in — separate concern, already refactored under
  `.agent/drop-stacking-support/`.
- `OnExpireTrigger` is left in place despite being unreachable.
- Targeted-as-source on-hit wiring stays unimplemented; this task preserves
  behavior rather than extending it.
- `OnHitTrigger` carries no attributes of its own. Anything describing *what*
  spawns belongs to the triggered skill set; the trigger only says *when*. The
  mana-cost factors inherited from `TriggerLink` stay, because they price the
  link itself rather than describe the effect.

## Triggers after this plan

| Trigger | Fields it keeps |
|---|---|
| `OnHitTrigger` | none of its own — UI + `manaCostMultiplier` / `manaCostIncreased` from `TriggerLink` |
| `IntervalSpawnTrigger` | `energyPerSecond`, plus the inherited mana factors and the two conversion methods |
| `StackTrigger` | `stackThreshold`, `debuffLifetimeSeconds`, `stacksPerHit` |
| `OnExpireTrigger` | none (unreachable; left alone) |

`StackTrigger`'s three fields are **not** effect attributes — they describe the
accrual condition, which is the trigger's own "when", and no skill definition
owns them. They stay. That is the line the rule draws: a field describing what
spawns belongs to the skill; a field describing when it fires belongs to the
trigger; a field pricing the link belongs to `TriggerLink`.

## Open questions

None outstanding.
