# Skill Loadout Edit Flow

## Purpose

Describe the main-thread path from a player picker action to a complete runtime
loadout update. Game Logic owns commit; the UI only presents and requests it.

```mermaid
sequenceDiagram
    participant UI as UI Toolkit
    participant Player as PlayerRoot
    participant Driver as SkillDriver
    participant Validator as Loadout Validator
    participant Compiler as Compiler + CombatRoot
    participant ECS as ECS combat

    UI->>Player: modal and pointer input state
    UI->>Driver: queue edit command(expected revision)
    Player-->>Player: block gameplay input; do not pause time
    Driver->>Driver: Tick start: clone current runtime loadout
    Driver->>Validator: validate candidate and edit eligibility
    Validator-->>Driver: valid or rejection reason
    alt candidate valid
        Driver->>Compiler: compile and register staging definitions
        Compiler-->>Driver: staged compiled roots
        Driver->>Driver: atomically swap loadout, roots, cooldown mapping
        Driver-->>UI: LoadoutChanged(new revision)
        Driver-->>UI: EditResolved(accepted)
    else candidate invalid or staging fails
        Driver-->>UI: EditResolved(rejected reason)
    end
    ECS-->>ECS: existing entities keep old copied snapshots
```

## Ordering

1. Picker computes presentational eligibility from validator rules and opens
   without pausing gameplay time.
2. UI queues one command and marks only its own presentation state pending.
3. At `SkillDriver.Tick` start, the driver rechecks revision, cooldown, and
   dependencies before cloning and applying a candidate.
4. The candidate validates, compiles, and registers before any live reference
   changes.
5. On success, one atomic swap exposes the new runtime loadout, compiled roots,
   and cooldown mapping. The driver emits `LoadoutChanged`, then
   `EditResolved`.
6. On failure, the old state remains active and only `EditResolved` reports the
   rejection.
7. Future casts use new compiled roots. Existing combat entities continue with
   copied values, IDs, hashes, and template keys.

## Failure Rules

- A stale picker revision, cooldown-blocked skill swap, support-cap change
  outside zero-to-authored-maximum bounds, out-of-range support position,
  missing dependency, invalid compatibility, compile failure, or registration
  failure rejects the command without partial mutation.
- UI may explain invalid choices before submit, but driver validation is final
  authority.
- No edit validates or compiles per frame; all such work is edit-frequency only.

## Related Documents

- [Skill Loadout Editing](../contracts/skill-loadout-editing.md)
- [Player Skill UI](../reference/design/player-skill-ui.md)
- [Skill To Combat Spawn](./skill-to-combat-spawn.md)
