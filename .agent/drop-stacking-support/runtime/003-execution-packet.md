# Task Execution Packet

## Task
003-asset-and-catalog-migration.md

## Goal
Migrate authored catalogs and assets from deleted stacking support presets to trigger-owned configuration.

## Files Allowed To Modify
- Unity Editor-managed `SupportCatalog.asset`, `StackTrigger.asset`, and four orphaned stacking skill-set assets, only through Unity Editor.

## Files Allowed To Create
- None.

## Files Allowed To Delete
- `StackingSupportLow.asset` and `StackingSupportHigh.asset`, including their Unity-managed `.meta` files, only through Unity Editor after references are cleared.

## Dependencies Confirmed
- Task 001 complete: `StackTrigger` fields exist and both support scripts are deleted.

## Step-By-Step Instructions
- Follow all four Unity Editor steps in `003-asset-and-catalog-migration.md`.
- Do not hand-edit YAML.

## Validation Required
- Confirm catalog contents, no remaining support GUID references under `Assets/`, no Unity missing-script warnings, and in-play wiring behavior.

## Hard Boundaries
- This task is explicitly user-performed in Unity Editor. Agent must not edit asset YAML or substitute a scripted asset rewrite.
