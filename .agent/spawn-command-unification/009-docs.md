# 009 — Docs reconciliation

## Goal

Move the ECS docs from "intended / being unified" to "as-built" once the refactor
lands.

## Changes

- [spawn-template-registry.md](../../Docs/reference/simulation/spawn-template-registry.md):
  drop the "target model" / "being unified" caveats on **Events As Templates** and
  **Cross-Domain Spawn Rules**; fold the `(kind, key)` model into the main body;
  update **Current Data Levels** and the AOE/projectile snapshot lists to the keyed
  form; keep the Registry Concurrency Contract and Bounded Nesting sections.
- [spawn-events-and-commands.md](../../Docs/contracts/spawn-events-and-commands.md):
  finalize the slim-event / command-template description (remove the transitional
  "older event types still embed..." note).
- [skill-runtime-snapshots.md](../../Docs/contracts/skill-runtime-snapshots.md):
  reconcile any references to the deleted snapshot structs.
- [adr-002](../../Docs/decisions/adr-002-plain-data-snapshot-boundary.md): confirm
  the key-reference bound description matches the shipped code.

## Acceptance criteria

- No doc references a deleted snapshot struct as a current mechanism.
- Registry concurrency contract and 3-level bound remain documented.
- Docs match code (checked against the merged implementation).

## Dependencies

007 (and ideally 008 green).

## Scope

Small–medium. Documentation only.
