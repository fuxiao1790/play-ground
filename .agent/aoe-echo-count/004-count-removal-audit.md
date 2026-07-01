# 004 — Old Count Removal Audit

Guarantee no old AOE stacking semantics survive. The `Count`→`EchoCount` rename
turns most stragglers into compile errors; this task is the deliberate sweep for
the rest (legacy assets, tests, docs strings, request structs).

## Audit checklist

Search the whole solution for AOE-side `count` / `Count` and classify each:

- `AoeSpawnCommand` — renamed in 001. Confirm no lingering `.Count` reads.
- `AoeDefinitionBase` / `RuntimeAoeDefinition` / `AoeBehaviorContext` — renamed in
  002. Confirm.
- `SkillIntervalTemplateBuilder.BuildAoeTemplate` + call sites — 003. Confirm.
- `SkillSpawnTranslator` AOE branch — 003. Confirm.
- **`AoeConfig.cs:17` `private int count = 1;` (`Count` at :28)** — legacy AOE
  authoring asset (separate from the skill/runtime path; feeds `AoeTypeDefinition`
  via `CreateTypeDefinition`). Decide: is this the same "spawn N" concept?
  - If it fed the old stack-at-center spawn, map it to echo (rename +
    `FormerlySerializedAs`) or remove it.
  - If it is pool/preload/type config unrelated to per-cast multiplicity, leave it
    and add a one-line comment noting it is **not** echo, to prevent confusion.
  - Record the finding in this file before editing.
  - Finding: `AoeConfig.Count` is not used by `CreateTypeDefinition`, `CombatRoot.RegisterConfig`,
    or any spawn request path; legacy config spawns build a single `AoeSpawnCommand`.
    It is unrelated to echo and is documented in code as not echo multiplicity.
- `Assets/Tests/PlayMode/SpawnCommandUnificationTests.cs`,
  `AoeSimulationTests.cs`, `AoePlayModeTests.cs` — update any `AoeSpawnCommand`
  construction / assertions using `Count` (test edits proper live in 005, but the
  rename must compile — fix construction here or coordinate with 005).
- Grep `AoeSpawnRequest` / `ProjectileAoeSpawnRequest` — confirmed no count field
  today; ensure none is added.

## Acceptance criteria
- Solution builds with zero references to a removed AOE `Count`.
- `AoeConfig.count` explicitly resolved (mapped, removed, or documented as
  unrelated) — decision written in this file.
- No code path writes multiple AOE copies at an identical center other than the
  `scatterRadius == 0` overlap case.

## Dependencies
Runs alongside 001–003 (the rename must build). Finalize before 005/006.

## Scope
Small-to-medium: mostly mechanical, plus one judgment call on `AoeConfig.count`.
