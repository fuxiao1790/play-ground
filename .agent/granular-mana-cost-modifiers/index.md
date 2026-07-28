---
name: granular-mana-cost-modifiers
description: Give ManaCost same increased%/multiplier granularity as Rate and AreaSize, for both supports and trigger links — and apply the same per-stat interface pattern everywhere, not just mana
---

# Granular Mana Cost Modifiers

## Summary

Todo say: "give more granular control to mana cost, mana cost modifiers for
supports and triggers should also have increased modifier and multiplier
modifiers." ([Docs/todo.md:11](../../Docs/todo.md#L11))

Me dig code. Good news: `SkillStat.ManaCost` already fold through same
generic `(base * preMul + added) * (1 + increased) * postMul` accumulator as
`Damage`, `AreaSize`, `Rate`
([StatModifierAccumulator.cs:49-54](../../Assets/Scripts/Skills/Modifiers/StatModifierAccumulator.cs#L49-L54)).
No engine change needed for that part — already confirmed with user: "base
mana cost, increased mana cost, mana cost multipliers should function similar
to rate/aoe... base cost added together, increased modifies base amount,
multiplier multiplies result."

Two real gaps, and the support-side one grew in scope over 5 rounds of
feedback:

1. **Supports.** Every support that taxes `ManaCost` today
   (`MultipleProjectilesSupport`, `PiercingSupport`, `HomingSupport`,
   `MultipleAoesSupport`, `AddedDamageSupport`) each declare their own private
   `manaCostAdded` field and call `sink.Add(SkillStat.ManaCost, manaCostAdded)`
   through the (formerly flat, generic) `IBaseValueModifier` — flat add only.
   Nothing implemented `IIncreasedModifier`/`IMultiplierModifier` for
   `ManaCost`. Round-by-round history:
   1. First draft: two new single-purpose SO types,
      `IncreasedManaCostSupport` / `ManaCostMultiplierSupport`. Rejected:
      "these fields should be plain fields on support instead of requiring
      another layer of scriptable object."
   2. Second draft: all 3 mana-cost fields flatly on the shared
      `StatModifierSupport` base. Rejected: "i prefer to split stat modifier
      support to differentiate increased modifiers and multiplier
      modifiers."
   3. Third draft: 3 kind-matching abstract base classes
      (`BaseValueModifierSupport`/`IncreasedModifierSupport`/
      `MultiplierModifierSupport`), single inheritance,
      `override`+`base.CollectX(sink)`. Rejected after the user opened
      `ModifierKindInterfaces.cs`: "use interface similar to
      `IBaseValueModifier`... might even be better to refactor existing
      logic into [a nested `IManaModifiers` interface family]," exact shape
      supplied.
   4. Fourth draft: `IManaModifiers`, a parallel interface family scoped to
      `ManaCost` only, living alongside the untouched flat generic
      `IBaseValueModifier`/`IIncreasedModifier`/`IMultiplierModifier`.
      Superseded (not exactly "rejected," more "scope grew") by: "every kind
      of modifier should follow this not just mana."
   5. **Final direction (this plan):** generalize the `IManaModifiers`
      pattern to **every `SkillStat` a support currently modifies** — one
      dedicated interface family per stat (`IDamageModifiers`,
      `IAreaSizeModifiers`, `IProjectileSpeedModifiers`,
      `IProjectileLifetimeModifiers`, `IRateModifiers`,
      `IPierceCountModifiers`, `IManaModifiers`), each shaped like
      `IManaModifiers`. The old flat generic interfaces are retired
      entirely — every support (and a test-only helper family discovered
      mid-plan) migrates to stat-specific interfaces via explicit interface
      implementation wherever it touches more than one stat through the same
      kind.
2. **Trigger links.** `TriggerLink.manaCostMultiplier`
   ([TriggerLink.cs:38-43](../../Assets/Scripts/Skills/Trigger/TriggerLink.cs#L38-L43))
   is a single raw scalar, applied outside `StatModifierAccumulator`
   entirely, in a separate compiler-side calculation
   ([SkillSetCompiler.cs:447-514](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L447-L514))
   and in `IntervalSpawnTrigger.ManaToEnergyCost`
   ([IntervalSpawnTrigger.cs:10-11](../../Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs#L10-L11)).
   It has no increased-percent counterpart. Untouched by rounds 1-5 above —
   trigger-side scope never changed.

This plan close both gap. Trigger side stay purely additive (one new field).
Support side is now a genuine interface-architecture change — not a mana-cost
feature anymore on that side, but a reorganization of how every support
declares which stat(s) it modifies, motivated by and delivered alongside the
mana-cost ask.

## Constraints & Invariants

- **Fold order is fixed and already correct for the target shape.**
  `value = (base * preMul + added) * (1 + increased) * postMul`
  ([StatModifierAccumulator.cs:49-54](../../Assets/Scripts/Skills/Modifiers/StatModifierAccumulator.cs#L49-L54)).
  Every stat-specific interface must still feed this fold through the
  existing sinks, not invent a parallel calculation.
- **Set isolation.** "Stat Modifier Supports only modify the Skill in the
  same set." ([skill-system.md:1128](../../Docs/reference/game-logic/skill-system.md#L1128)).
  Every stat-specific interface rides through the exact same
  `CollectSupportModifiers` loop, so this holds for free regardless of how
  many interface families exist.
- **No serialization migration risk.** No field ever moves to a different
  declaring class in this design — `manaCostAdded` on the 5 pre-existing
  mana-taxing supports, and each support's own primary-stat field
  (`addedDamage`, `pierceCount`, `areaSizeMultiplier`, `speedMultiplier`,
  etc.), all stay exactly where they are. Only the *interface type* each
  method satisfies changes, which Unity serialization doesn't care about at
  all (serialization is field-name/type based, not interface based).
- **Explicit interface implementation is required, not optional, wherever a
  support implements two interfaces sharing an identical method signature**
  — e.g. `IDamageModifiers.IBaseValueModifier.CollectAdded(AddedSink)` and
  `IManaModifiers.IBaseValueModifier.CollectAdded(AddedSink)` on
  `AddedDamageSupport`, or all three `IMultiplierModifier`-shaped interfaces
  on `FasterProjectilesSupport`. A single implicit method would satisfy all
  of them simultaneously with one shared body, which is wrong here — each
  needs to target a different stat. This is a real C# constraint, not a
  style choice, and task 001 must get every instance of it right.
- **Every current implementer of the flat generic interfaces must be found
  before deleting them**, including test-only code. `ModifierFoldEditModeTests.cs`
  ([:316-351](../../Assets/Tests/EditMode/ModifierFoldEditModeTests.cs#L316-L351))
  declares 3 generic test-helper supports implementing the interfaces this
  plan deletes — found by grepping `Assets/` (not just `Assets/Scripts/`)
  before finalizing task 001's scope.
- **Old tests that test obsolete logic get removed, not patched to keep
  passing.** User feedback, applied directly to the discovery above: the 3
  generic test-helper supports exist solely to exercise the *old* mechanism
  (one interface, any stat picked at runtime) — that mechanism is exactly
  what this task retires, so the right move is deleting them and rewriting
  the 2 tests they powered to test what they actually verify (accumulator
  fold arithmetic, including `MultiplierTiming.Pre` — a real feature no
  production support exercises) directly against `StatModifierAccumulator`,
  not hardcoding them to a fake stat just to keep the old shape alive. This
  is also *why* `IDamageModifiers` only needs `IBaseValueModifier` in the
  final coverage table — an earlier pass through this plan nearly gave it
  `IIncreasedModifier`/`IMultiplierModifier` purely to satisfy the doomed
  test helpers, which would have been production interface surface existing
  only for a test (the same smell
  [coding-standards.md's Test Hooks section](../../Docs/coding-standards.md#L381-L393)
  warns against for fields/flags, applied to an interface instead).
- **Trigger cost is a per-edge scalar, not a set-level stat.** `manaCostMultiplier`
  composes across the compiled trigger tree by direct multiplication at each
  edge (`GetManaCostMultiplier`/`SumTriggeredSkillManaCosts` recursion,
  [SkillSetCompiler.cs:476-557](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L476-L557)),
  not through `StatModifierAccumulator`. A trigger link has no "base" value of
  its own to add to — it only ever scales an already-resolved child
  `ManaCost`. So the trigger-side fix mirrors only the tail of the stat
  formula — `(1 + increased) * multiplier` — not the full four-term fold.
  This is a real, load-bearing asymmetry between how supports and triggers
  modify `ManaCost`; it is not an oversight to "fix" by forcing triggers
  through the accumulator, and it is unaffected by the support-side interface
  reorganization above.
- **Existing serialized data must keep working (trigger side).**
  `TriggerLink.manaCostMultiplier` already has `[FormerlySerializedAs(...)]`
  history ([TriggerLink.cs:40-42](../../Assets/Scripts/Skills/Trigger/TriggerLink.cs#L40-L42))
  — this project cares about not breaking authored assets across renames. New
  fields must default to neutral (0% increased, x1 multiplier) so existing
  `TriggerLink` and skill-set assets compile to the exact same numbers they do
  today.
- **Tests pin the current trigger formula.** `SkillValidationEditModeTests.cs`
  asserts exact chained mana-cost numbers through `manaCostMultiplier`
  ([SkillValidationEditModeTests.cs:114-146](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L114-L146),
  [:229-251](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L229-L251)).
  Changing the formula shape must not change these numbers when the new field
  is left at its default (0%).
- **ScriptableObject content authoring is a user/editor step**, not something
  to hand-edit as YAML. Interface/compiler/support code is mine to write;
  opening existing `.asset` instances to confirm nothing regressed, and
  authoring new increased%/multiplier values, is the user's editor step —
  same convention as every other support change in
  [skill-system.md:920-925](../../Docs/reference/game-logic/skill-system.md#L920-L925).

## Mechanisms Reused vs. Introduced

Reused:
- `StatModifierAccumulator` fold, `AddedSink`/`IncreasedSink`/`MultiplierSink`
  — all already generic per `SkillStat`, zero changes needed regardless of
  how the interface layer above them is organized.
- `StatModifierSupport`
  ([StatModifierSupport.cs](../../Assets/Scripts/Skills/Support/StatModifierSupport.cs)) —
  untouched, no change at all through any round.
- `SkillSetCompiler.CollectSupportModifiers`'s existing `is`-check loop shape
  ([SkillSetCompiler.cs:180-202](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L180-L202))
  — grown from 3 checks to 12, same flat/explicit style throughout, not
  restructured into a lookup table or reflection-based dispatch.
- `TriggerLink`'s existing `manaCostMultiplier` field and
  `ApplyIncomingTriggerManaCostMultiplier`/`GetManaCostMultiplier`/
  `SumTriggeredSkillManaCosts` call sites — extended in place, not replaced.

Introduced:
- 7 nested interface families in `ModifierKindInterfaces.cs`
  (`IDamageModifiers`, `IAreaSizeModifiers`, `IProjectileSpeedModifiers`,
  `IProjectileLifetimeModifiers`, `IRateModifiers`, `IPierceCountModifiers`,
  `IManaModifiers`), each declaring only the kind(s) an actual implementer
  (production or test) needs. Justification: directly matches explicit user
  direction to generalize the `IManaModifiers` pattern beyond mana.
- 9 more checks in `SkillSetCompiler.CollectSupportModifiers` (12 total, up
  from the original 3). Justification: the compiler has to know about every
  interface family that exists; this is the one place the reorganization
  isn't free.
- One new field: `TriggerLink.manaCostIncreasedPercent`. Justification: the
  trigger side has no accumulator to plug into (see invariant above), so
  granularity has to be a second scalar field on the link itself, folded
  locally at the same two call sites that already read `manaCostMultiplier`.
- Rename: `RuntimeSkillDefinition.IncomingManaCostMultiplier` →
  `IncomingManaCostFactor`. It currently stores a raw copy of
  `link.manaCostMultiplier`; after this change it stores the *combined*
  `(1 + increasedPercent) * multiplier` product, so the old name would be
  misleading. Internal runtime-only field, no serialization, so the rename is
  free.

Removed:
- The flat, generic `IBaseValueModifier`/`IIncreasedModifier`/
  `IMultiplierModifier` interfaces (previously reused across every stat,
  disambiguated only by an enum argument at the call site). Fully superseded
  by the 7 stat-specific families — nothing implements them once task 001 is
  complete, so they're deleted rather than left dead.

## Design Validation

- *Fold order preserved*: every stat-specific interface implementation only
  ever calls `sink.Add(SkillStat.X, ...)` through the existing sinks, so
  `StatModifierAccumulator.Resolve` computes the exact same fold it always
  has — no new code path in the accumulator itself, for any stat.
- *Set isolation*: unaffected — same compiler loop, same set-scoped supports
  array, no cross-set reach introduced by adding more interface families.
- *No silent dead fields, structurally*: no inheritance chain anywhere in
  this design, so there's no `base.CollectX(sink)` call to forget. A support
  either implements a given stat's interface (and the compiler invokes it)
  or it doesn't.
- *Every real implementer accounted for before deletion*: the constraint
  above required grepping all of `Assets/` (not just production code) before
  finalizing which `(stat, kind)` pairs task 001 needs — this caught
  `ModifierFoldEditModeTests.cs`'s test-only helpers, which would otherwise
  have silently broken the build. Resolution was deletion (see "old tests"
  constraint above), not accommodation — so finding them didn't end up
  widening any interface family's kind coverage.
- *`FasterProjectilesSupport` splits cleanly*: its one method setting both
  `ProjectileSpeed` and `ProjectileLifetime` becomes two explicit
  one-line interface methods instead of one two-line implicit method — same
  two fields, same values, no behavior change, just named per-stat instead
  of ad hoc.
- *Trigger asymmetry is intentional*: confirmed above as an invariant, not
  papered over — the plan does not try to route trigger cost through
  `StatModifierAccumulator`, and this is completely unaffected by the
  support-side interface reorganization.
- *Backward compatibility*: `manaCostIncreasedPercent` (triggers) defaults to
  `0f`, so `(1 + 0) * manaCostMultiplier == manaCostMultiplier` — every
  existing `SkillValidationEditModeTests` assertion keeps passing unmodified.
  On the support side, no field moves classes, no default value changes, and
  `ModifierFoldEditModeTests.cs`'s assertions are untouched (same accumulator
  arithmetic, just reached through renamed interface types) — every existing
  support's resolved output is numerically unchanged before any author opts
  into a new field.

## Minimal/Additive vs. Refactor Comparison

This section covers **support-side** shape, which has gone through 5 rounds.
Trigger-side stays additive throughout — see rationale after.

- **Round 1 (rejected): two new single-purpose SO types.**
  Decision: rejected. User feedback: "these fields should be plain fields on
  support instead of requiring another layer of scriptable object."

- **Round 2 (rejected): all 3 mana-cost fields flatly on `StatModifierSupport`.**
  Decision: rejected. User feedback: "i prefer to split stat modifier support
  to differentiate increased modifiers and multiplier modifiers."

- **Round 3 (rejected): 3 kind-matching abstract base classes, single
  inheritance, `override` + `base.CollectX(sink)` chains.**
  Decision: rejected. User, after opening `ModifierKindInterfaces.cs`: "use
  interface similar to `IBaseValueModifier`... might even be better to
  refactor existing logic into [`IManaModifiers`]."

- **Round 4 (superseded, not wrong — just narrower than wanted): parallel
  `IManaModifiers` interface family, scoped to `ManaCost` only, sitting
  alongside the untouched flat generic interfaces for every other stat.**
  - Resulting data flow: correct numeric outcome for mana; every other stat
    still dispatched through the old generic, enum-disambiguated interfaces.
  - Existing concepts/types changed: none — purely additive, 1 new interface
    family, 3 new compiler checks.
  - Decision: **superseded.** User: "every kind of modifier should follow
    this not just mana" — the *principle* round 4 established for `ManaCost`
    (a dedicated, self-documenting interface family instead of a generic one
    disambiguated by an enum argument) was right, but scoping it to one stat
    when the same generic-interface pattern exists for 6 others was
    inconsistent.

- **Round 5 / chosen: one dedicated interface family per `SkillStat`
  currently modified by a support (7 families, `IManaModifiers` plus 6 new
  ones), replacing the flat generic interfaces everywhere, not just for mana.**
  - Resulting data flow: identical numeric outcome to round 4 for mana;
    every other stat now dispatched through its own named interface instead
    of a generic one, via the compiler's 12 explicit checks.
  - Existing concepts/types changed: `ModifierKindInterfaces.cs` fully
    rewritten (3 flat interfaces → 7 nested families); all 9 production
    supports change their primary-stat interface declaration (not just their
    mana one); `ModifierFoldEditModeTests.cs`'s 3 test-only helpers migrate
    too, and lose their now-redundant runtime `SkillStat` parameterization
    since every real usage already targeted one fixed stat.
  - Copies/translations removed: the *pattern* (generic interface + enum
    argument to disambiguate) is fully retired, replaced by one
    self-documenting interface per stat everywhere it's used — no support
    anywhere still relies on an enum argument to say which stat a `CollectX`
    call targets.
  - Long-term benefit: full consistency — there's no longer a "mana is
    special, everything else uses the old generic interfaces" asymmetry for
    a future contributor to trip over. Every `is IXModifiers.IY` check in the
    compiler is self-documenting; `grep`-ing for `IDamageModifiers` finds
    every damage-modifying support directly, without cross-referencing enum
    arguments inside method bodies.
  - Decision: **choose round 5.**
    Reason: directly matches explicit user direction ("not just mana"), and
    the principle round 4 validated (dedicated interface > generic +
    enum-argument disambiguation) applies exactly as well to every other
    stat — there's no structural reason to stop at one.

- **Refactor approach (rejected, all rounds): route trigger
  `manaCostMultiplier`/increased-percent through `StatModifierAccumulator`
  too, so supports and triggers share one fold code path.**
  - A trigger edge doesn't own a "base" `ManaCost` value to fold against (it
    scales an already-resolved child cost), so this doesn't fit the
    accumulator's shape without inventing a fake base term, and would tear
    out working per-edge composition logic to rebuild around per-set
    snapshots for no removed duplication (supports and triggers modify mana
    cost at genuinely different scopes).
  - Decision: **rejected.** Reason: forcing one code path here creates
    "duplicate ownership of the same responsibility" — trigger-edge cost and
    set-level stat cost are different concepts that happen to share a name.
    Unaffected by any support-side round above.

## Default Decision Rule Applied

`ManaCost` (and every other `SkillStat`) on `RuntimeSkillDefinition` stays
the single source of truth for a compiled definition's resolved value — this
plan only changes *how a support declares it contributes* to that value
(interface type), never introduces a second representation of any stat, and
adds one more *scalar* to the existing per-edge trigger calculation. The
collapse-to-one-source-of-truth rule isn't triggered anywhere.

## Tasks

1. [001-mana-cost-modifier-supports.md](001-mana-cost-modifier-supports.md) —
   full `ModifierKindInterfaces.cs` rewrite (7 per-stat interface families);
   `SkillSetCompiler` grows to 10 checks; migrate all 9 production supports'
   primary *and* mana interfaces; delete `ModifierFoldEditModeTests.cs`'s
   obsolete test-only helpers and rewrite the 2 tests they powered against
   `StatModifierAccumulator` directly.
2. [002-trigger-link-mana-cost-fold.md](002-trigger-link-mana-cost-fold.md) —
   `TriggerLink.manaCostIncreasedPercent` + compiler/interval formula update +
   `IncomingManaCostMultiplier` → `IncomingManaCostFactor` rename.
3. [003-mana-cost-modifier-tests.md](003-mana-cost-modifier-tests.md) —
   new EditMode coverage for the mana-cost fold behavior and trigger formula
   (not to be confused with 001's required fix to *existing* tests).
4. [004-mana-cost-docs-update.md](004-mana-cost-docs-update.md) — update
   `skill-system.md` and `resource-spend-gate.md`.
5. [005-verify-mana-cost-support-assets.md](005-verify-mana-cost-support-assets.md) —
   user/editor step: spot-check existing support assets compile/display
   correctly, optionally author new increased%/multiplier values.

Dependency order: 001 and 002 run in parallel (touch disjoint files). 003 and
004 each depend on both 001 and 002. 005 depends only on 001 and is a
user-owned editor step, not code.

## Open Questions / Notes

- Exact default field values (`manaCostIncreasedPercent`/`manaCostMultiplier`
  on the 4 newly-augmented supports, e.g. `0`/`1` neutral) are content-balance
  choices, not architecture — task 001 defaults them to neutral so no
  existing support's resolved cost changes; the user can retune per-asset in
  the Unity inspector.
- `manaCostIncreasedPercent` naming mirrors `increasedRatePercent` on
  `SkillStatSnapshot`
  ([skill-system.md:129-138](../../Docs/reference/game-logic/skill-system.md#L129-L138))
  for consistency, rather than the `xMultiplier - 1f` convention
  `IncreasedAoeSupport`/`IncreasedRateSupport` use on their own fields —
  those two are support-local UI/authoring choices (author types "1.5", code
  subtracts 1), whereas both `TriggerLink.manaCostIncreasedPercent` and the
  new `IManaModifiers.IIncreasedModifier` fields are plain percent fields.
- This plan does not chase deduplicating the 5 pre-existing `manaCostAdded`
  fields any further than they already are — every round stopped moving
  fields between classes once the user's actual ask (interface
  differentiation, now generalized to every stat) was satisfied. If deduping
  those 5 flat fields is wanted later, it's a separate, smaller follow-up,
  not blocking this feature.
- If a future stat gains a new `(stat, kind)` combination no current support
  uses, adding it is a 2-line change (one nested interface + one compiler
  check) — the per-stat family shape was chosen partly because it makes that
  extension trivial and localized, unlike the old generic interfaces where
  "does anything already use `SkillStat.Foo` with `IIncreasedModifier`?"
  required reading every support's method body to answer.
