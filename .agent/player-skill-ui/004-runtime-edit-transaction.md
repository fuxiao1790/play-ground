# 004 - Add atomic runtime editing and cooldown contract

## Change

Make `SkillDriver` own the live runtime clone and edit transaction.

- Clone assigned `SkillLoadout` and each node's `SkillSet` during initialization.
- Expose read-only runtime nodes, `EvaluateEdit`, `TryQueueEdit`,
  `EditResolved`, and `LoadoutChanged` (revision + affected indices).
- `SkillLoadoutEditCommand` supports set/clear Skill, Support, and Trigger by
  logical node/support/link index. It carries definition references selected from
  the catalog, not UI objects.
- Allow one pending edit. At start of `SkillDriver.Tick`, clone current runtime
  state into a transient candidate, apply command, revalidate, compile/register
  staging roots, then atomically swap candidate + compiled arrays + root cooldown
  map. Failure preserves old state and emits rejection.
- Key cooldown state by logical node index instead of compact root order so UI
  and driver share stable positions.
- Reject direct-root skill/support edits while that node is not ready. Triggered
  nodes have no active cast cooldown. Trigger edits remain allowed.
- Reset an accepted changed root and every newly created root to zero progress.
  Preserve state for unchanged roots. Remove state for nodes made triggered-only
  or empty.
- Keep old in-flight entity snapshots/template keys intact; only future casts use
  the new compiled revision.

## Acceptance criteria

- No UI code mutates `SkillLoadout`, `SkillSet`, cooldowns, compiler, or
  `CombatRoot` directly.
- Successful event fires only after state and compiled runtime swap together.
- Compile/registration/validation failure leaves prior revision active.
- Source ScriptableObjects never change.
- Cooldown restrictions match the documented direct-root rules.
- Edit path allocates only when an edit is queued; normal Tick has no new GC.

## Dependencies

- 003 normalized validator/compiler path.

## Scope / complexity

High. Main behavioral and atomicity work.

