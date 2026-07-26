# Skill Loadout Editing — Close Three Known UI/Implementation Gaps

## Summary

Closes 3 of the 6 items in [ui.md](../../Docs/ui.md)'s "Known UI/Implementation
Gaps (Temporary)" section, per user direction on each:

1. **Cooldown progress resets on unrelated edits** — `SkillDriver.CompileAndRegister`
   rebuilds every root's `SkillSlotState` from scratch on every accepted edit, so
   editing node A silently wipes node B's in-progress cooldown. User direction:
   fix the underlying preservation bug, **and** disable a root's skill/support/cap
   controls in the bar while that root is on cooldown, so the (already-enforced)
   "can't edit a root during its cooldown" rule is visible before the player tries.
2. **Picker closes before `EditResolved`** — `SkillLoadoutUi.AddChoice` closes the
   picker the instant a command is *queued*, not once the driver *resolves* it, so
   a rejection can never reach the user. User direction: make the picker actually
   wait; no new eligibility/validation UI yet (explicitly future work).
3. **Picker does not disable ineligible choices** — all catalog entries render as
   always-enabled buttons. User direction, scoped specifically to supports: add a
   tag system — skills have tags, and the support picker should only *show*
   supports that share a tag with the node's skill (hide, not disable-with-reason).
4. *(Explicitly deferred by user: "bare bone ui is acceptable in current state")*
   No Escape/backdrop/scrolling work in this plan.

Two other items in that doc section (duplicate-support rejection, stale
`definitionId` contract text) are **not in scope** — the user did not raise them
here, and re-reading [skill-loadout-editing.md](../../Docs/contracts/skill-loadout-editing.md)
during grounding shows duplicate supports are explicitly *allowed* by the current
contract text, so that particular original finding no longer describes a real gap.

## Key Discovery During Grounding

Item 3's "tag system" **already exists** at the data layer and is unused only by
the UI:

- `SkillDefinitionTags` ([SkillDefinitionTags.cs](../../Assets/Scripts/Skills/SkillDefinitionTags.cs)) —
  `[Flags]` enum (`Projectile`, `Aoe`, `Any`) + `HasAny`/`Format` helpers.
- `Skill.Tags` ([Skill.cs:13](../../Assets/Scripts/Skills/Skill.cs)) — abstract, every
  concrete skill already reports its tags.
- `StatModifierSupport.SupportedSkillTags` ([StatModifierSupport.cs:5](../../Assets/Scripts/Skills/Support/StatModifierSupport.cs)) —
  abstract, but only declared on this one `SkillSupport` subclass.
- `SkillLoadoutValidator.ValidateSkillSet` ([SkillLoadoutValidator.cs:97-109](../../Assets/Scripts/Skills/SkillLoadoutValidator.cs)) —
  already computes `HasAny(skillTags, support.SupportedSkillTags)` and warns when a
  *StatModifierSupport specifically* mismatches — but only after the player has
  already equipped it.

So task 004 below is "generalize `SupportedSkillTags` to the `SkillSupport` base
and reuse it in the picker," not "invent a tag system."

## Constraints & Invariants (with sources)

- **One command boundary, revision-gated.** UI only creates `SkillLoadoutEditCommand`
  values; the driver applies exactly one pending edit per `Tick`, gated on
  `expectedRevision`. ([skill-loadout-editing.md:88-97](../../Docs/contracts/skill-loadout-editing.md))
  → tasks 002/003 must keep reading driver state, never mutate it locally.
- **Cooldown semantics are contractual, not incidental.** "New root nodes start at
  zero cooldown progress; unchanged roots preserve their progress; an accepted
  skill/support edit on a direct root resets that root's progress... Skill/support
  changes to a direct root are disabled while that root's cooldown is running.
  Trigger changes are still allowed." ([skill-loadout-editing.md:100-103,133-134](../../Docs/contracts/skill-loadout-editing.md))
  → task 001 must key preservation off the *edited node index*, not "any edit
  happened"; trigger-only edits must never force a reset.
- **Node identity is index-stable.** Nodes are never removed/reordered; clearing a
  skill sets `skillSet = null` in place (`SkillDriver.TryApplyEdit`,
  [SkillDriver.cs:412-417](../../Assets/Scripts/Skills/SkillDriver.cs)). → node index is
  a safe preservation key across recompiles.
- **`CompileAndRegister` is reflectively invoked with zero args by an existing
  test.** `AoePlayModeTests.CompileAndRegister(driver)` does
  `typeof(SkillDriver).GetMethod("CompileAndRegister", BindingFlags.Instance |
  BindingFlags.NonPublic)` then `method.Invoke(driver, null)`
  ([AoePlayModeTests.cs:894-901](../../Assets/Tests/PlayMode/AoePlayModeTests.cs)).
  `GetMethod(string, BindingFlags)` throws `AmbiguousMatchException` if a second
  overload is added, and `Invoke(driver, null)` throws
  `TargetParameterCountException` if the sole method gains parameters (optional or
  not — `Invoke` does not auto-apply C# default parameter values). → task 001 must
  keep `CompileAndRegister()` as a genuine zero-parameter method and pass edit
  context through private fields instead of a signature change.
- **Restore is not an edit.** `TryRestoreRuntimeLoadout` replaces the whole node
  list wholesale (save/load), and today always compiles fresh cooldown state
  ([SkillDriver.cs:318-337](../../Assets/Scripts/Skills/SkillDriver.cs)). This is
  correct and must stay unaffected by task 001 — preservation-by-node-index must
  not accidentally leak stale session cooldowns into a freshly loaded save just
  because node indices coincide.
- **`LoadoutChanged` fires before `EditResolved` on success**, and
  `SkillLoadoutUi.OnLoadoutChanged` already calls `RefreshBar()` → `ClosePicker()`
  ([skill-loadout-editing.md:112-113](../../Docs/contracts/skill-loadout-editing.md),
  [SkillLoadoutUi.cs:251-252](../../Assets/Scripts/SkillUi/SkillLoadoutUi.cs)). → task
  003's `OnEditResolved` only has real work to do on the *rejection* path; the
  modal is already gone by the time a successful `EditResolved` arrives.
- **No full-screen backdrop exists** (`.picker-modal` is a positioned box, not an
  overlay — [SkillLoadoutUi.uss:82-90](../../Assets/Scripts/Ui/SkillLoadout/SkillLoadoutUi.uss)),
  so bar buttons remain clickable while a picker is open. Pre-existing, out of
  scope (gap 4 deferred); tasks below must not silently rely on the bar being
  inert while a picker is up.

## Mechanisms Reused vs. Introduced

- **Reused:** the existing `SkillDefinitionTags`/`HasAny` tag-matching mechanism
  (task 004, generalized from `StatModifierSupport`-only to all `SkillSupport`);
  the existing `FindRootSlotForNode` + `SkillSlotState.IsReady` cooldown-lookup
  (task 001/002, factored into one shared helper instead of two near-duplicate
  checks); the existing per-frame `Update()` cooldown-label refresh loop in
  `SkillLoadoutUi` (task 002 extends it, doesn't add a new loop); the existing
  `VisualElement.SetEnabled` disable pattern already used for cap buttons (task
  002/003).
- **Introduced:** two private fields on `SkillDriver` (`preserveCooldownState`,
  `cooldownResetNodeIndex`) as the side channel `ProcessPendingEdit` uses to tell
  `CompileAndRegister` which node (if any) should reset, required by the
  reflection constraint above; a `SkillSupport.SupportedSkillTags` virtual default
  (`Any`) on the base class, re-abstracted on `StatModifierSupport`, so every
  support type — not just stat modifiers — has a queryable tag.

## Design Validation

- **Cooldown contract (001):** preservation is keyed by node index and explicitly
  skipped for the edited node → "unchanged roots preserve, edited root resets"
  holds exactly. Trigger edits pass `resetNodeIndex = -1` (no forced reset) →
  "trigger changes don't reset cooldown" holds. `Start()`/`TryRestoreRuntimeLoadout`
  never set the preserve flags → restore-is-not-an-edit holds.
- **Reflection constraint (001):** `CompileAndRegister()` keeps its exact current
  signature; edit context flows through fields set immediately before the call and
  consumed (and cleared) at the top of the method. Verified against the exact
  `GetMethod`/`Invoke` call in `AoePlayModeTests.cs:894-901`.
- **Event ordering (003):** confirmed from both the contract text and the current
  `OnLoadoutChanged`/`ClosePicker` code that success needs no explicit close in
  `OnEditResolved` — only rejection does. Avoids a redundant/racy double-close.
- **Tag filtering (004):** reuses the exact `HasAny(skillTags, support.SupportedSkillTags)`
  expression the validator already uses, so "shown in picker" and "accepted without
  a validator warning" stay the same predicate — no second, drifting definition of
  compatibility.

## Additive vs. Refactor Comparison

This only meaningfully applies to task 001 (the cooldown bug); tasks 002-004 are
inherently additive (read more driver state, wait for an existing event, filter
existing data) with no refactor alternative worth comparing.

**Additive option for 001 (rejected):** leave `CompileAndRegister` rebuilding
everything fresh, and instead have `SkillDriver` snapshot/restore cooldown values
into a `Dictionary<int, float>` before/after each edit from the *caller* side
(`ProcessPendingEdit`).
- Resulting data flow: a second, parallel record of "cooldown by node index" that
  must be kept in sync with `slotStates`/`rootNodeIndices` by hand on every future
  change to compilation.
- New concepts: a snapshot map with its own lifetime rules.
- Long-term cost: two sources of truth for the same state; easy to reintroduce
  this exact bug class the next time compilation logic changes.

**Refactor option for 001 (chosen):** `CompileAndRegister` itself looks up and
reuses the previous `SkillSlotState` instance for any node index it recognizes,
unless told (via the field-based signal) that this specific node was just edited.
- Resulting data flow: `slotStates` stays the single source of truth; there is
  exactly one place (`CompileAndRegister`) that decides fresh-vs-reused state.
- Existing concepts changed: `CompileAndRegister` grows a small lookup step;
  `IsCooldownBlocked` is factored to share its "which kind touches a root" check
  with the new reset-decision logic (`AffectsRootCooldown`).
- Long-term benefit: any future caller of `CompileAndRegister` gets correct
  preserve/reset behavior for free; no parallel bookkeeping to forget.

**Decision:** refactor `CompileAndRegister` directly. It collapses to one source of
truth and directly fixes the root cause instead of papering over its symptom.

## Task List

- [001](001-preserve-root-cooldown-on-unrelated-edits.md) — Preserve per-root
  `SkillSlotState` across recompiles unless that node was the one just edited.
- [002](002-disable-root-controls-during-cooldown.md) — Disable a root's skill,
  support, and cap buttons in the bar while that root is on cooldown.
- [003](003-picker-waits-for-edit-resolved.md) — Picker enters a pending
  (disabled) state on submit and only closes/reports on `EditResolved`.
- [004](004-tag-filtered-support-picker.md) — Generalize `SupportedSkillTags` to
  the `SkillSupport` base; filter the support picker to tag-compatible entries.
- [005](005-update-ui-doc-known-gaps.md) — Remove the three closed entries from
  [ui.md](../../Docs/ui.md)'s "Known UI/Implementation Gaps (Temporary)" section.

## Dependencies

- 001 and 002 both address the cooldown gap but are independently landable (002
  only needs a new read-only accessor, not 001's preservation fix).
- 003 and 004 both edit `SkillLoadoutUi.OpenPicker`/`AddChoice`. No functional
  dependency, but implement 004 after 003 to avoid touching the same methods
  twice in the same session.
- 005 depends on 001-004 all landing (it removes doc entries those tasks close).

## Open Questions / Follow-ups (not in this plan)

- The contract's Eligibility Rules say a rejected choice should be "disabled with
  its reason" (validator-driven), while the user's direction for 004 is to *hide*
  tag-incompatible supports entirely. These aren't the same UI treatment. Filed as
  a future reconciliation (either update the contract wording, or add real
  disable-with-reason once broader validator-driven eligibility work happens) —
  not resolved here.
- `TriggerLink` already carries `SourceSkillTags`/`TargetSkillTags` and the
  validator already checks them the same way it checks support tags
  ([SkillLoadoutValidator.cs:146-172](../../Assets/Scripts/Skills/SkillLoadoutValidator.cs)).
  The trigger picker could get the same tag-filtering treatment as task 004 gives
  the support picker. Not requested, not included — noted as a natural, low-cost
  extension if wanted later.
