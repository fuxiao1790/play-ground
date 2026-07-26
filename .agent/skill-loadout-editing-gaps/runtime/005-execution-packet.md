# Task Execution Packet

## Task

005-update-ui-doc-known-gaps.md

## Goal

Remove exactly the three now-closed UI gap entries from `Docs/ui.md`, retaining the two deferred/unaddressed entries and all other document content.

## Files Allowed To Modify

- `Docs/ui.md`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- `Docs/ui.md`
- `implementation-log.md`

## Behavior To Preserve

- Retain the known-gaps heading and introduction.
- Retain the Escape/backdrop/scrolling and stale `definitionId` entries as-is.
- Do not change unrelated UI architecture text.

## Behavior To Change

- Remove only cooldown reset, early picker close, and unfiltered support picker gap bullets, including their continuation prose.

## Relevant Global Context

Tasks 001 through 004 are complete and statically verified. This is final documentation cleanup; it must not include the explicitly deferred picker interaction gap or stale contract-reference gap.

## Dependencies Confirmed

- 001-004 are marked Complete in `implementation-log.md` and static checks have passed.

## Step-By-Step Instructions

1. Remove exactly the three named known-gap bullets and their indented explanations.
2. Keep section heading plus remaining two bullets unchanged.

## Acceptance Criteria

- Exactly two unaddressed known gaps remain.
- No other `ui.md` section changes.

## Validation Required

- Inspect the document diff and count remaining known-gap bullets.

## Hard Boundaries

- Do not modify code, contracts, or any document other than `Docs/ui.md`.
