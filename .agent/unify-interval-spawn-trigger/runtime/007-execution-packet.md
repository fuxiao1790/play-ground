# Task Execution Packet

## Task

007-editor-asset-fixup.md

## Goal

User rebinds four orphaned trigger assets to `IntervalSpawnTrigger` in Unity Editor.

## Files Allowed To Modify

- None by agent.

## Behavior To Preserve

- Existing serialized trigger values and catalog references.

## Dependencies Confirmed

- Task 002 created `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs` and removed old scripts/metas.

## User Steps

- Rebind each listed asset's Missing Script field to `IntervalSpawnTrigger` in Unity Editor.
- Verify retained values and trigger catalog references.

## Validation Required

- Unity Editor inspection; user-run Unity tests exporting XML.

## Hard Boundaries

- Agent does not hand-edit Unity asset/meta YAML.
