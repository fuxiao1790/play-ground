# Unify Interval Spawn Trigger Variations

## Summary

`ProjectileIntervalSpawnTrigger`, `AoeIntervalSpawnTrigger`, and
`TargetedIntervalSpawnTrigger` are three sealed `TriggerLink` subclasses the
player picks from when socketing a timed-child link between two skill sets in
a loadout (via `SkillUiTriggerCatalog`, same UI role as a socketed support).
They differ only in which extra fields they carry and which `RuntimeSkillDefinition`
subtype they're allowed to target:

| Class | Extra fields | `TargetSkillTags` |
|---|---|---|
| `ProjectileIntervalSpawnTrigger` | `projectileCount`, `sideSpreadDegrees` | `Projectile` |
| `AoeIntervalSpawnTrigger` | `echoCount`, `scatterRadius` | `Aoe` |
| `TargetedIntervalSpawnTrigger` | `echoCount` | `Targeted` |

All three already inherit `energyPerSecond` and the energy/mana helper methods
from the shared abstract `IntervalSpawnTrigger` base, and all three are
compiled by near-duplicate methods in `SkillSetCompiler`
(`ApplyChildSpawn` / `ApplyAoeIntervalSpawn` / `ApplyTargetedIntervalSpawn`)
that share the same preamble (parent-type check, pulse-AOE-lifetime bail,
child compile, energy threshold, mana multiplier) and only differ in which
`Runtime*IntervalSpawnSetup` they build and which field they write it to.

This plan merges the three player-facing trigger classes into one concrete
`IntervalSpawnTrigger` (the existing abstract base, made concrete) carrying all
four extra fields and `TargetSkillTags = Any`. Which `Runtime*IntervalSpawnSetup`
gets built is decided at compile time by the **compiled child's actual runtime
type**, not by which trigger subclass was chosen — this is strictly more
correct, since every existing `Apply*` method already independently re-derives
and checks the compiled child's type (`compiledChild is not RuntimeXDefinition`)
regardless of which trigger subclass called it. The subclass was redundant
metadata duplicating what the effect skill set's own definition already encodes.

It also adds a **new `SkillDefinitionTags.Interval` flag** (on
`ProjectileSkill` and `LingeringAoeSkill` only) so the merged trigger's
**source**-side validation can fail as a hard `Error` when the source skill
isn't a duration source, using the same generic tag-mismatch check every
other trigger already goes through — replacing a separate, ad-hoc,
Warning-only pulse-AOE check that existed only because the tag system
couldn't previously express "has a duration" as distinct from "is an AOE."

**Explicitly out of scope** (per user instruction — "classes don't have to
combine into a single huge class, only the support player interacts with"):
the three `Runtime*IntervalSpawnSetup` classes, the three separate
`ChildSpawnSetup` / `AoeIntervalSpawnSetup` / `TargetedIntervalSpawnSetup`
fields on `RuntimeProjectileDefinition` / `RuntimeAoeDefinition`, and the ECS
systems that consume them (`TimedSpawnSystem`, `ProjectileSpawnExpansionSystem`,
`AoeSpawnExpansionSystem`, etc.) are untouched. Those are internal/ECS-facing,
not something the player selects.

## Rationale

- The player-facing choice today is really "pick the trigger whose name
  matches the type of skill you're about to socket after it" — three
  near-identical assets differing mostly in field names. Collapsing them
  removes that redundant choice without losing any capability: every field
  from every variant survives on the merged class, just conditionally
  relevant depending on what's socketed downstream (the same pattern already
  used by `SkillSupport.SupportedSkillTags` — irrelevant fields on a support
  are simply inert for skill types they don't apply to).
- Today, wiring the wrong variant to the wrong effect type (e.g.
  `ProjectileIntervalSpawnTrigger` → an AOE effect set) is a silent no-op with
  a validation warning (`UnsupportedTriggerTarget`, "will do nothing") — see
  `SkillValidationEditModeTests.ValidatorWarnsWhenProjectileIntervalTargetsAoeSkill`
  / `CompilerIgnoresProjectileIntervalTargetAoeSkill`. After this change that
  combination isn't an error case anymore — it's simply supported, because
  there's only one trigger class and it dispatches on the real effect type.
  Those two tests are rewritten (see [005](./005-update-tests.md)).
- Separately, the user asked that **source**-side validation fail (not just
  warn) when the source skill doesn't qualify as an interval source. The
  existing model couldn't express this cleanly: `AoeSkillBase.Tags` is
  `sealed override => Aoe` for *both* pulse `AoeSkill` and `LingeringAoeSkill`
  — there is no tag today that distinguishes "AOE with a duration to accrue
  energy over" from "instant pulse AOE." That's why the current rejection is
  a separate `ValidateIntervalSpawnSource` method doing a `Definition`-type
  check (`is AoeDefinitionBase and not LingeringAoeDefinition`) instead of the
  same generic `HasAny(causeSkill.Tags, link.SourceSkillTags)` check every
  other trigger type is validated with — and it's Warning severity, not
  Error. Adding `SkillDefinitionTags.Interval` fixes the root cause (the tag
  system's expressiveness gap) rather than just changing the severity of the
  existing ad-hoc check, and lets that ad-hoc method be deleted entirely in
  favor of the generic path. See [001](./001-add-interval-tag.md).

## Constraints & Invariants

- **Asset identity is GUID-bound, not name-bound.** A Unity ScriptableObject
  asset's `m_Script` field pins a `(guid, fileID)` resolved from the backing
  `.cs` file's `.meta`. Deleting a `.cs` file that a `.asset` instance points
  at breaks that asset ("missing script") until someone re-points it in the
  Unity Editor. Source: read `Assets/ScriptableObjects/Skills/Triggers/*.asset`
  and matching `.cs.meta` guids directly.
  → Four existing production trigger assets (`ProjectileIntervalSpawnTrigger.asset`,
  `ProjectileIntervalSpawnTrigger2.asset`, `AoeIntervalSpawnTrigger.asset`,
  `AoeIntervalSpawnTrigger2.asset`) currently bind to the two subclasses being
  deleted. This cannot be avoided by picking a different survivor name — at
  best one family's two assets survive at the cost of the other family's two.
  Promoting the existing (unused-by-any-asset) abstract `IntervalSpawnTrigger`
  to concrete breaks all four equally rather than silently favoring one family
  via a reused guid, and is the most legible outcome to hand to the user.
- **Editor/asset fixups are the user's job, not mine.** Per
  [[editor-steps-are-user-steps]], I do not hand-edit `.asset`/`.meta` YAML.
  The four broken assets are re-pointed by the user in the Unity Editor; see
  [007](./007-editor-asset-fixup.md) for the exact steps and expected outcome.
- **`SkillLoadoutNode.TriggerToNext` is a plain object reference**
  (`[SerializeField] private TriggerLink triggerToNext`), not
  `[SerializeReference]`. Confirmed in `SkillLoadout.cs:10`. It points at a
  trigger `.asset` by that asset's own file guid, which does not change here —
  so no `SkillLoadout`/`SkillSet` asset needs touching, only the four trigger
  assets themselves.
- **No string-based type lookups exist for these classes.** Confirmed no hits
  for the three type names under `Assets/Editor/` (build processors, migration
  scripts) — this is a plain C# type refactor with no reflection/string-based
  coupling to worry about.
- **Validation tag-mismatch machinery is generic** (`SkillLoadoutValidator.ValidateTriggerLink`
  checks `HasAny(effectSkill.Tags, link.TargetSkillTags)` and
  `HasAny(causeSkill.Tags, link.SourceSkillTags)` for every trigger type, not
  just interval-spawn ones). Setting `TargetSkillTags = Any` makes the target
  check permanently pass for the merged trigger; setting `SourceSkillTags =
  Interval` makes the source check newly precise (previously `Projectile |
  Aoe`, which incorrectly let pulse AOE through at the tag level). Neither
  change requires touching the check itself — this is the point of it being
  generic.
- **`Skill.Tags` is a fixed, class-level property, not per-instance data.**
  Every existing `Tags` override (`ProjectileSkill`, `AoeSkillBase`,
  `TargetedSkillBase`) returns a constant regardless of authored field values
  on that particular asset. `Interval` follows the same pattern — it's a
  property of the *skill type* (does this shape have a lifetime to tick at
  all), not of a specific asset's authored lifetime value. Whether a specific
  source can accrue *enough* energy before it expires is a separate, already-
  existing, already-instance-aware check (`ValidateTargetedIntervalEnergyReachability`,
  unaffected by this plan).
- **Validation severity is advisory only today.** Confirmed by grep: nothing
  outside `SkillLoadoutValidator.cs`/`SkillLoadoutCompiler.cs` reads
  `SkillValidationWarning.Severity`; `SkillDriver.ValidationWarnings` stores
  results for "future UI" per existing doc comments, and `SkillSetCompiler.Compile`
  runs independent of validation results, gated only by its own runtime-type
  checks (e.g. the `LifetimeSeconds: <= 0f` no-op guard). So making the
  source-tag mismatch `Error` changes what's *reported*, not what's
  *compiled* — the compiler's existing no-op guard for a pulse-AOE source
  still does the actual blocking at compile time, unchanged by this plan.
  "Validation should fail" is implemented as "reports as Error," matching the
  doc's existing convention for other hard-authoring-mistake cases
  (`TargetedConfigurationError`, the Hurtbox `TargetedVisualWarning` case).
- **`SkillDefinitionTags.Any` must stay `Projectile | Aoe | Targeted`.**
  `Interval` is an orthogonal capability flag, not a definition shape, so it's
  deliberately excluded from `Any`. Every existing `SupportedSkillTags => Any`
  support must keep matching every skill regardless of the new bit — `HasAny`
  is bitwise, so this holds automatically, verified by inspection against the
  one place `Skill.Tags` currently feeds a UI compatibility filter
  (`SkillLoadoutUi.cs:291`, the support picker).

## Mechanisms Reused vs. Introduced

- **Reused:** `SkillDefinitionTags.Any` (existing flag combination, already the
  pattern `SkillSupport` uses for "compatible with anything"); the existing
  `IntervalSpawnTrigger` base's `energyPerSecond` field and
  `ResolveEnergyPerSecond`/`ManaToEnergyCost` helpers (untouched); the existing
  `Runtime*IntervalSpawnSetup` types and their host fields (untouched, per
  scope); the existing generic tag-validation machinery in
  `SkillLoadoutValidator` (now doing *more* work — it absorbs the pulse-AOE
  case instead of that case having a separate method); `SkillValidationSeverity.Error`
  (existing enum value, already used elsewhere for hard-authoring-mistake
  cases).
- **Introduced:** `SkillDefinitionTags.Interval`, a new flag marking "this
  skill has a duration to accrue energy over." This is genuinely new surface,
  not pure consolidation — but it replaces an existing ad-hoc, less-general
  check (`ValidateIntervalSpawnSource`'s `Definition`-type inspection) rather
  than sitting alongside it, so the net validation-mechanism count goes down,
  not up.

## Design Validation

| Invariant | Held by this design? |
|---|---|
| Asset GUID binding | Respected — no attempt to preserve it by trickery; breakage is explicit and handed to the user as an editor step. |
| `TriggerToNext` is a plain object ref | Unaffected — no `SkillLoadout`/`SkillSet` asset needs edits. |
| No reflection/string coupling | Confirmed absent — safe rename. |
| Generic tag validation | Unaffected as a mechanism — reused for both a newly-permissive target check (`Any`) and a newly-precise source check (`Interval`). |
| `Tags` is class-level, not instance-level | `Interval` follows the existing pattern; energy-reachability (instance-level) stays a separate, already-existing check. |
| Validation severity is advisory-only | Confirmed by grep; `Error` on source mismatch changes reporting, not compile-time behavior, which the compiler's own no-op guard already handles. |
| `Any` excludes `Interval` | Explicit design choice, verified against the one UI call site that filters on `Tags`. |
| ECS/runtime spawn path untouched | Held — `Runtime*IntervalSpawnSetup` classes, their host fields, and the systems consuming them are not part of this change. |

## Minimal/Additive vs. Refactor Comparison

- **Minimal/additive approach** (rejected): leave the three sealed subclasses
  in place; for the source-validation ask, just bump `ValidateIntervalSpawnSource`'s
  existing warning to `Error` severity in place, without introducing a tag.
  - resulting data flow: unchanged — three near-identical player-facing
    classes remain, dispatch still keys off trigger subtype; source validity
    still determined by a separate `Definition`-type check living outside the
    generic tag-check path.
  - new concepts/types introduced: none, but the two-code-paths-for-one-concept
    problem (generic tag check *and* ad-hoc pulse-AOE check, both nominally
    validating "is this a legal interval source") persists.
  - copies/translations added: none, but the redundant subtype-as-implicit-
    target-type encoding remains, and the requested trigger-class consolidation
    doesn't happen.
  - long-term cost: every future interval-spawn field addition still needs to
    ask "which of the three classes does this belong on"; source-eligibility
    logic stays split across two methods instead of being one tag fact
    consumed by one check.
- **Refactor approach** (chosen): collapse the three sealed subclasses into
  one concrete `IntervalSpawnTrigger`; collapse the three `Apply*` compiler
  methods into one that switches on the compiled child's runtime type; add
  `SkillDefinitionTags.Interval` and delete the ad-hoc pulse-AOE check in
  favor of the generic tag-mismatch check, now Error for this trigger.
  - resulting data flow: `TriggerLink` (one concrete type) →
    `SkillLoadoutValidator` checks `causeSkill.Tags` against
    `SourceSkillTags = Interval` (one generic check, Error on mismatch) →
    `SkillSetCompiler.CompileInternal` compiles the child once → switches on
    the child's actual `Runtime*Definition` type → builds the matching
    `Runtime*IntervalSpawnSetup` → writes it to the matching field. Same
    downstream ECS consumption as today, unchanged.
  - existing concepts/types changed or removed: `ProjectileIntervalSpawnTrigger`,
    `AoeIntervalSpawnTrigger`, `TargetedIntervalSpawnTrigger` removed;
    `IntervalSpawnTrigger` goes from abstract to concrete;
    `ValidateIntervalSpawnSource` and `IsIntervalSpawnTrigger` removed;
    `AoeSkillBase.Tags` un-sealed so `LingeringAoeSkill` can add `Interval`.
  - copies/translations removed or avoided: three duplicated preambles in
    `SkillSetCompiler` become one; the redundant "trigger subtype implies
    target type" encoding is removed in favor of the single real source of
    truth (the compiled child's type); the redundant "two ways to check
    source validity" (generic tag check + ad-hoc type check) collapses to one.
  - long-term benefit: one player-facing interval-spawn trigger to author,
    catalog, and balance; new interval-spawn fields have one obvious home;
    the "wrong variant targets wrong effect" no-op/warning case disappears;
    source eligibility is one tag fact on the skill, checked by the same
    mechanism every other trigger's source/target compatibility already uses.
  - **Decision: refactor.** Reason: this is exactly what was asked for on both
    fronts (class consolidation and hard-failing source validation), it
    removes two genuine second-sources-of-truth (subtype vs.
    compiled-child-type; ad-hoc type check vs. generic tag check), and the
    only cost (four assets need a one-time re-point in the Unity Editor) is
    unavoidable under any naming choice and is a one-time, well-scoped manual
    step, not an ongoing cost.

## Default Decision Rule Applied

Two representation pairs collapsed to one source of truth each, with no
compatibility/migration reason to keep either duplicate:
- Trigger subtype and compiled-child-type were two representations of "what
  kind of skill does this interval spawn target." Refactored toward the
  compiled-child-type.
- The generic `SourceSkillTags` tag check and the ad-hoc `Definition`-type
  check were two representations of "is this skill a legal interval source."
  Refactored toward one generic tag check, enabled by giving the tag system
  the expressiveness (`Interval`) it was previously missing.

## Task List

1. [001-add-interval-tag.md](./001-add-interval-tag.md) — add
   `SkillDefinitionTags.Interval`; tag `ProjectileSkill` and
   `LingeringAoeSkill` with it; leave pulse `AoeSkill` and `TargetedSkill`
   without it.
2. [002-merge-trigger-class.md](./002-merge-trigger-class.md) — promote
   `IntervalSpawnTrigger` to concrete, merge in all four fields,
   `SourceSkillTags = Interval`, `TargetSkillTags = Any`, delete the three
   sealed subclasses.
3. [003-collapse-compiler-dispatch.md](./003-collapse-compiler-dispatch.md) —
   replace the three-way `SkillSetCompiler` dispatch/Apply methods with one.
4. [004-update-validator.md](./004-update-validator.md) — delete the ad-hoc
   pulse-AOE check, make the generic source-tag-mismatch check Error for
   `IntervalSpawnTrigger`, update the targeted-energy-reachability type check.
5. [005-update-tests.md](./005-update-tests.md) — update all EditMode/PlayMode
   test call sites; rewrite the tests whose expected behavior changes
   (target-mismatch now supported; source-mismatch now Error with new message
   text).
6. [006-update-docs.md](./006-update-docs.md) — update
   `skill-system.md`, `folder-structure.md`, `targeted-system.md`.
7. [007-editor-asset-fixup.md](./007-editor-asset-fixup.md) — **user-performed**
   Unity Editor steps to re-point the four broken trigger assets. Not
   executed by the agent.

Dependency order: 001 blocks 002 and 004. 002 blocks 003 and 004. 003 and 004
block 005. 006 can happen any time after 001-004 are settled (doc content, no
code dependency) but is listed after so the written docs describe the final
shape. 007 can happen any time after 002 lands but realistically comes last,
once the user opens the Editor.

## Open Questions

None outstanding. Two real judgment calls came up and are both resolved:
- Which class name survives the trigger merge — resolved above (promote the
  unused abstract base; every option breaks some existing assets equally, so
  there's no lower-cost alternative to surface for a decision).
- How to model "the source skill doesn't have the interval tag" — asked the
  user directly (new `Interval` flag vs. just bumping the existing check's
  severity in place); user chose the new-flag approach, which is reflected
  throughout this plan.
