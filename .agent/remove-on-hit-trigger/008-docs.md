---
name: docs
description: Remove OnHitTrigger/on-hit-spawn documentation from the doc set
---

# 008 — Docs

## Goal
Remove documentation describing the now-deleted mechanism. Do not leave a dangling reference to a
type or field that no longer exists in the codebase.

## Dependencies
Tasks 001–006 complete (so doc removal matches final code shape).

## Files to Inspect (found via `grep -rl "OnHitTrigger\|OnHitSpawnRef\|AoeHitSpawnComponent\|Impact.*Definition\|OnHit.*SpawnDefinition"` under `Docs/`)

- `Docs/reference/game-logic/skill-system.md`
- `Docs/reference/game-logic/skill-gameplay-system.md`
- `Docs/folder-structure.md`
- `Docs/reference/simulation/spawn-template-registry.md`
- `Docs/reference/simulation/aoe-system.md`
- `Docs/contracts/skill-runtime-snapshots.md`
- `Docs/decisions/adr-002-plain-data-snapshot-boundary.md`

## Step-by-Step

1. **`skill-system.md`**: delete the `**OnHitTrigger**` section (~line 605–656, table + prose +
   the `class OnHitTrigger : TriggerLink {}` snippet). In the `compile()` pseudocode
   (~line 793–815), delete the `if chain.link is OnHitTrigger:` block. Leave `IntervalSpawnTrigger`
   and `StackTrigger` sections/branches untouched.

2. **`skill-gameplay-system.md`**: update the sentence "`OnHitTrigger` and `StackTrigger` do not
   accrue..." (~line 169) — remove the `OnHitTrigger` mention; keep whatever remains true for
   `StackTrigger` alone (re-read the surrounding sentence to phrase correctly, don't just delete
   the substring).

3. **`folder-structure.md`**: delete the `Assets/Scripts/Skills/Trigger/OnHitTrigger.cs` entry
   (~line 254).

4. **`spawn-template-registry.md`**: remove mentions of `OnHitSpawnRef` / on-hit spawn template
   references; keep the interval/stack-detonation registry description intact.

5. **`aoe-system.md`**: remove mentions of `AoeHitSpawnComponent` / on-hit AOE spawn; keep the
   impact-vs-lingering AOE distinction (that concept is unrelated to on-hit triggers — see
   index.md grounding — do not remove it).

6. **`Docs/contracts/skill-runtime-snapshots.md`**: remove any `OnHitSpawnRef` field mentions from
   the documented snapshot contracts.

7. **`Docs/decisions/adr-002-plain-data-snapshot-boundary.md`**: read the section referencing this
   mechanism and update it to reflect that on-hit spawn is no longer part of the snapshot boundary
   — do not delete the ADR's actual decision record, only the now-stale detail.

## Behavior to Preserve
- All documentation for `IntervalSpawnTrigger`, `StackTrigger`, impact-vs-lingering AOE
  distinction, and the shared spawn-event/expansion/apply pipeline.

## Acceptance Criteria
- `grep -rn "OnHitTrigger\|OnHitSpawnRef\|AoeHitSpawnComponent"  Docs/` returns nothing.
- No doc section reads as self-contradictory after the edit (re-read surrounding paragraphs, not
  just the matched line).

## Validation
- `grep -rn "OnHitTrigger\|OnHitSpawnRef\|AoeHitSpawnComponent" Docs/` returns nothing.
