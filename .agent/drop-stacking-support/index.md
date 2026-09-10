# Drop StackingSupport — StackTrigger Owns the Stack Detonation

## Summary

Delete `StackingSupport` and move everything it owns onto `StackTrigger`:

- the three authored accrual fields (`stackThreshold`, `debuffLifetimeSeconds`, `stacksPerHit`)
- construction of the `RuntimeStackingDetonation` wrapper around the compiled effect
- derivation of `DebuffName` from the effect set's skill

After the change, "this compiled instance is a stacking detonation" is expressed
purely by the wiring — exactly how every other trigger link already decides the
compiled shape of its effect — instead of by a marker support that must be placed
on the target set.

`ConversionSupport` goes with it: `StackingSupport` is its only implementer, so
the abstraction, its `Compile` hook, and both call sites become unreachable.

Net type/method delta: **nothing introduced, six things deleted.**

| Deleted | Location |
|---|---|
| `StackingSupport` | `Assets/Scripts/Skills/Support/StackingSupport.cs` |
| `ConversionSupport` | `Assets/Scripts/Skills/Support/ConversionSupport.cs` |
| `SkillSetCompiler.ApplyConversionSupports` | `SkillSetCompiler.cs:284-303` |
| `SkillLoadoutCompiler.HasTriggeredOnlyConversionSupport` | `SkillLoadoutCompiler.cs:56-72` |
| `SkillLoadoutValidator` stacking checks (3) + `HasStackingSupport` | `SkillLoadoutValidator.cs:57-66, 214-224, 331-376` |
| `SkillValidationWarningCode.UnsupportedStackingDetonation` | `SkillValidationWarning.cs:12` |

Plus two now-dead special cases in `SkillSetCompiler`: the root `DebuffName`
assignment (`:54-55`) and the `triggerHost` unwrap (`:59-66`).

## Motivation

`StackTrigger` is the only trigger that requires its target set to carry a
specific support asset before the link does anything. Every other link
(`OnImpactAoeTrigger`, `OnImpactProjectileTrigger`, `IntervalSpawnTrigger`,
`OnAoeHitSpawnTrigger`) reads its effect node, decides the runtime shape, and
attaches it — no cooperation from the target set required. The stacking path
splits one concept across two assets and forces three bespoke validator rules
whose only job is to police that split.

## Constraints & invariants the change must respect

| # | Invariant | Source | Consequence for this plan |
|---|---|---|---|
| I1 | Debuff accrual identity is a registration-minted counter, never authored, one per compiled `RuntimeStackingDetonation` object | `SkillDriver.cs:1240-1246`; `RuntimeStackingDetonation.cs:14-17` (`DebuffKey = -1` default) | The refactor must not make any authored value part of debuff identity. Wrapping per `StackTrigger` branch still produces one fresh object per compiled chain instance, so keys stay unique — including `A → trigger → A → trigger → A`. |
| I2 | Root detection is positional: a node is a root iff the previous node has both a `SkillSet` and a `TriggerToNext` | `SkillLoadoutCompiler.cs:20-29` | A `StackTrigger` effect node is already excluded from roots by position. `ConvertsToTriggeredOnly` is redundant with it, not load-bearing. |
| I3 | The wrapper is never spawned; the inner detonation is the entity that spawns and hits, and therefore the applicator for any downstream link | `SkillSetCompiler.cs:59-66` (comment + `triggerHost`) | Must be preserved. It becomes structural rather than compensatory: the effect node now compiles to the inner definition directly, so its own outgoing trigger attaches to the inner definition with no unwrap step. |
| I4 | Mana aggregation treats the wrapper as transparent — cost comes from `stacking.Detonation`, and the link's factor is stamped on the wrapper as `IncomingManaCostFactor` | `SkillSetCompiler.cs:539-554, 556-590` | Wrap must happen **before** `ApplyIncomingTriggerManaCostMultiplier`, and the inner definition must remain the mana-cost carrier. Ordering inside the `StackTrigger` branch is load-bearing. |
| I5 | Driver registration walks are shape-agnostic — they unwrap `RuntimeStackingDetonation` wherever encountered | `SkillDriver.cs:567-572, 685, 1146, 1202` | No driver change required; the wrapper still hangs off `projectile/aoe/targeted.StackingDetonation`. |
| I6 | `BuildStackEffectSnapshot` materializes AOE and projectile detonations only; a targeted detonation returns `default` and bakes nothing | `SkillDriver.cs:1670-1745` | Deleting the bespoke target check removes the only loud signal for a bad `StackTrigger` target. Replace it by narrowing `TargetSkillTags` to `Projectile \| Aoe` so the **existing** generic tag check covers it. |
| I7 | `RecoveryTime` is read only for root slots | `SkillDriver.cs:232`; `SkillSetCompiler.cs:57` | The wrapper's `RecoveryTime` is irrelevant. After the change the inner definition carries the rate-derived value, which is strictly more correct. |
| I8 | Compiler and validator skip null support entries | `SkillSetCompiler.cs:212-213`; `SkillLoadoutValidator.cs:103` | Deleting the C# type before the four stacking `SkillSet` assets are cleaned degrades quietly (missing-script slot), it does not throw. Relevant to migration sequencing, not correctness. |
| I9 | `TriggerLink` assets are shared `PersistentScriptableObject`s referenced by player-facing catalogs | `TriggerLink.cs:32`; `Assets/ScriptableObjects/UI/SkillBar/TriggerCatalog.asset` | Accrual config becomes per-trigger-asset. The existing Low(10)/High(30) support presets are **not** preserved — user has accepted breaking them, so the single `StackTrigger.asset` carries the type defaults and is retuned by hand if desired. |

## Mechanisms reused vs. introduced

**Reused — nothing here is a new pattern:**

- *Trigger-owned authored parameters.* `IntervalSpawnTrigger` (`projectileCount`,
  `sideSpreadDegrees`, `echoCount`, `scatterRadius`, `energyPerSecond`) and
  `OnImpactProjectileTrigger` (`spawnCount`, `spreadDegrees`) already author
  mechanical config on the link as public fields. The three stack fields adopt
  the same style and placement.
- *Trigger-branch shape decisions.* `CompileInternal` already has a
  `link is StackTrigger` branch; `OnImpactProjectileTrigger` already mutates its
  compiled effect in-branch (`:123-124`). Constructing the wrapper there is the
  same move, one step further.
- *Generic tag validation.* `SourceSkillTags` / `TargetSkillTags` already gate
  every other link. Narrowing `StackTrigger.TargetSkillTags` replaces
  `ValidateStackTriggerTarget` with the mechanism that already exists.
- *Positional root detection.* Replaces `ConvertsToTriggeredOnly` with no new code.

**Introduced:** none.

## Design validation

- **I1 (key identity):** `EnsureStackingDetonationDebuffKey` mints on any object
  whose `DebuffKey < 0`. The wrapper is now constructed inside the cause node's
  `StackTrigger` branch, once per compiled chain instance — same cardinality as
  today, since the effect node was already compiled once per incoming link. Keys
  remain unique per instance; no authored field enters identity. ✔
- **I2 (roots):** A `StackTrigger` effect node has `hasIncomingTrigger == true`,
  so it is skipped by `SkillLoadoutCompiler` without consulting supports. An
  *unwired* former detonation set becomes an ordinary castable root — the same
  behavior every other trigger's effect set already has when left unwired. This
  is a deliberate behavior change, listed under Open Questions. ✔
- **I3 (wrapper not spawned):** effect node compiles to the inner definition and
  attaches its own outgoing trigger to that same object; the cause node then
  wraps it. The downstream applicator is still the inner definition. The
  `triggerHost` unwrap becomes unnecessary rather than being relocated. ✔
- **I4 (mana):** branch order is compile effect → construct wrapper →
  `ApplyIncomingTriggerManaCostMultiplier(wrapper, link)`. `GetOwnManaCost` and
  `GetManaCostMultiplier` still recurse through `stacking.Detonation` unchanged. ✔
- **I5 (driver):** untouched. ✔
- **I6 (snapshot capability):** narrowed `TargetSkillTags` makes a targeted
  detonation a visible `UnsupportedTriggerTarget` warning instead of a silent
  no-op — strictly better than today, where the check only asked whether the
  target carried a support. ✔
- **I7 (recovery):** wrapper keeps the base default; nothing reads it. ✔

## Minimal/additive vs. refactor comparison

**Minimal/additive approach** — move the three fields to `StackTrigger` but keep
`StackingSupport` as an empty marker (its `ConvertsToTriggeredOnly` and the
validator's existence checks stay):

- resulting data flow: authoring split across two assets — accrual on the link,
  "is a detonation" on the set; compiler consults both.
- new concepts/types introduced: none, but `ConversionSupport` +
  `StackingSupport` are retained with zero fields and one no-op implementer.
- copies/translations added: none.
- long-term cost: a support type that exists only to be checked for existence —
  a shim retained to avoid touching old code. `StackTrigger` stays the one link
  gated on an unrelated asset, and the three bespoke validator rules survive to
  police a split that no longer carries data.

**Refactor approach (chosen):**

- resulting data flow: one authoring surface. `StackTrigger` reads its own
  fields, compiles its effect, wraps it, attaches it. Nothing else participates.
- existing concepts/types changed or removed: `StackingSupport`,
  `ConversionSupport`, `ApplyConversionSupports`,
  `HasTriggeredOnlyConversionSupport`, three validator rules + `HasStackingSupport`,
  one warning code, the root `DebuffName` special case, the `triggerHost` unwrap.
- copies/translations removed: one indirection hop — the trigger no longer reads
  accrual values back off a wrapper that a support populated.
- long-term benefit: `StackTrigger` becomes structurally identical to every
  other link; "detonation" becomes a per-chain compiled outcome rather than
  intrinsic asset state that can be orphaned or mis-targeted.

**Decision: refactor.**
Reason: the additive variant preserves duplicate ownership of a single concept
("is this a detonation" living in both the support and the wiring) and keeps a
single-implementer abstraction alive purely to avoid deletions. The refactor has
strictly fewer data paths and one source of truth, and it is what the user
asked for.

## Default decision rule

Two representations of the same domain concept collapse to one source of truth
unless a concrete compatibility or migration reason blocks it. Here the only
migration cost is asset-side (task 003) and is bounded to six assets.

## Behavior changes (intended, not incidental)

1. An unwired former detonation set is castable as a root instead of being
   silently excluded. (I2)
2. A `StackTrigger` pointed at a targeted skill now raises
   `UnsupportedTriggerTarget` instead of `UnsupportedStackingDetonation`. (I6)
3. Stack accrual values are authored per **trigger** asset, so two chains sharing
   one `StackTrigger` asset share accrual config; two chains sharing one
   detonation set no longer do. (I9)
4. The `StackingSupportLow` / `StackingSupportHigh` balance presets are dropped
   rather than migrated. Accepted by the user; the single `StackTrigger` asset
   starts at the type defaults.

## Tasks

| # | Task | File | Depends on |
|---|---|---|---|
| 001 | `StackTrigger` owns stack config and wrapping; delete support hierarchy | [001-stacktrigger-owns-stack-config.md](001-stacktrigger-owns-stack-config.md) | — |
| 002 | Migrate EditMode/PlayMode tests off `StackingSupport` | [002-test-migration.md](002-test-migration.md) | 001 (same commit — shared assembly compile) |
| 003 | Asset + catalog migration (Unity editor, user-performed) | [003-asset-and-catalog-migration.md](003-asset-and-catalog-migration.md) | 001 |
| 004 | Doc updates across five reference docs | [004-doc-updates.md](004-doc-updates.md) | 001 |

## Resolved decisions

1. **Accrual presets are not preserved.** The Low(10)/High(30) support presets
   are dropped; `StackTrigger` carries the type defaults inherited from
   `StackingSupport` (`3 / 4f / 1`) and is retuned by hand if the feel is off.
   This removes the only reason task 003 would have needed to create additional
   trigger assets.
2. **Migration window is benign.** Field values can only be authored after the
   fields exist, so 001 lands before 003. Unity runs field initializers before
   applying YAML, so the existing `StackTrigger.asset` picks up the defaults
   without being touched — stacking keeps working across the window, just at
   default tuning.

## Open questions

None outstanding.
