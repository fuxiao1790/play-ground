# Task 002 Execution Packet

## Goal

Add the normalized serialized node model and migrate every existing loadout asset
without changing authored definition assets.

## Allowed Changes

- `SkillLoadout`, `SkillSet`, loadout editor, and a focused editor migration
  command.
- Serialized `SkillLoadout` asset data created by the migration command.
- Implementation log and task-local execution packet.

## Required Facts

- Keep legacy slots only temporarily for staged migration and current compiler
  compatibility; task 003 removes them after compiler refactor.
- Node order derives cause/effect adjacency. Empty skill slots remain nodes.
- Runtime clone gives every node a distinct `SkillSet` instance while immutable
  definition assets stay shared.

## Validation

- Compile in Unity batch mode.
- Run migration in Unity batch mode.
- Verify each `SkillLoadout` YAML has normalized `nodes` and report migration
  issues. Do not manually rewrite YAML.
