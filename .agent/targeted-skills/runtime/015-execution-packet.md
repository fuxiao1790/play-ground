# Task Execution Packet

## Task

`015-docs-and-authored-content.md`

## Goal

Document targeted chains and hand Unity editor authoring to the user. Do not
hand-edit Unity asset YAML.

## Files Allowed To Modify

- Listed `Docs/` files in task 015.
- `Docs/reference/simulation/targeted-system.md`.
- `.agent/targeted-skills/implementation-log.md`.

## Dependencies Confirmed

- Targeted single-hit and lingering ECS lanes, pools, resolve systems, trigger
  links, supports, and root API exist.
- Focused PlayMode XML reports task 014 passed 12/12.

## Behavior To Preserve

- `lifetimeSeconds > 0` selects lingering targeted.
- Root targeted casts keep origin and acquire anchor separate.
- Targeted chains use target proxies, not physics/colliders.
- The five spawn-event structs remain temporary refactor debt.

## Acceptance Criteria

- Update every task-015 document.
- Add targeted system reference covering archetypes, pools, resolve loop, walk,
  render mirror, and caps.
- Remove targeted skills from todo while retaining spawn-event consolidation.
- Give user prefab, VFX, skill, loadout, trigger, and registration steps.

## Validation Required

- Static documentation audit only. Unity editor content and real-app confirmation
  are user-owned.
