# 005 — Remove closed entries from ui.md's known-gaps list

## Goal

[ui.md](../../Docs/ui.md)'s "Known UI/Implementation Gaps (Temporary)" section
says: "Remove each entry once the implementation is fixed or the referenced doc is
corrected to match reality." Once tasks 001-004 land, three of its six entries are
fixed. Remove exactly those three; leave the rest.

## Changes

1. **`Docs/ui.md`** — in the `## Known UI/Implementation Gaps (Temporary)` section,
   remove these three bullets (closed by 001-004):
   - "Cooldown progress resets on unrelated edits."
   - "Picker closes before `EditResolved`."
   - "Picker does not disable ineligible choices."

   Keep these as-is (not addressed by this plan):
   - "No Escape key, backdrop cancel, or scrolling in the picker." (explicitly
     deferred by user)
   - "`SkillLoadoutEditCommand` carries object references, not `definitionId`."
     (stale contract text, not touched by this plan)

   If removing three of five bullets empties the section down to two items, keep
   the section (don't delete the heading) — it's still tracking real remaining
   gaps.

## Acceptance Criteria

- `Docs/ui.md` lists only the two remaining, unaddressed gaps.
- No other section of `ui.md` changes.

## Dependencies

Depends on 001, 002, 003, and 004 all being implemented and verified — this task
is doc cleanup, not a code change, and should be the last one done.

## Scope

Trivial — doc edit only.
