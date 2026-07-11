# 005 — Docs + tests

## Design doc — `Docs/reference/game-logic/skill-system.md`

1. **`OnImpactAoeTrigger`** section: it currently shows an empty body
   `class OnImpactAoeTrigger : TriggerLink { }`. Update to:
   ```csharp
   class OnImpactAoeTrigger : TriggerLink {
       float cooldownSeconds;   // per source entity; 0 = fire every hit
   }
   ```
   Add a sentence: "`cooldownSeconds` rate-limits the impact AOE **per source
   entity**: after firing, that projectile/AOE instance cannot spawn another
   impact AOE until the cooldown elapses, across any target. It gates only the
   impact spawn — direct damage still applies on every hit. `0` fires on every
   hit (default)."

2. **`OnImpactProjectileTrigger`** section: add `float cooldownSeconds;` to the
   listed fields and the same per-source-entity / gates-only-the-spawn note.

3. Note the interaction: on an AOE source, `OnImpactAoeTrigger` and
   `OnAoeHitSpawnTrigger` compile to the same `OnHitAoeSpawnDefinition` field; the
   cooldown is what the impact trigger adds. `OnAoeHitSpawnTrigger` remains
   cooldown-free (fires every hit).

## Contract doc — `Docs/contracts/skill-runtime-snapshots.md`

If it enumerates `OnHitSpawnRef` fields, add `CooldownSeconds` (authored config,
part of the content-hashed template).

## Tests

- **Edit-mode (compile)**: authored `cooldownSeconds` reaches
  `OnHitSpawnRef.CooldownSeconds` for projectile and AOE sources; two loadouts
  differing only in cooldown produce distinct `TemplateKey`s (the task-002 hash
  check — keep it here if not added earlier).
- **PlayMode — projectile** (extend `ProjectileCollisionSimulationTests`):
  piercing projectile + impact-AOE with cooldown spawns one impact AOE across a
  multi-target frame; a second impact AOE after the cooldown elapses; `0` fires
  every hit.
- **PlayMode — AOE** (extend `AoeSimulationTests`): lingering AOE + on-hit spawn
  with cooldown fires at ~cooldown spacing, not per tick; direct damage every
  tick; `0` unchanged.

## Acceptance criteria

- Doc matches shipped field names and semantics.
- New tests pass alongside the existing suites.

## Dependencies

- Tasks 001–004 complete.

## Scope

Small–medium (mostly test authoring).
