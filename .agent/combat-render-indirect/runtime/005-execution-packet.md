# Task Execution Packet

## Task
005-atlas-completeness.md

## Goal
Ensure the assigned combat `SpriteAtlas` contains every combat sprite and packs into one page.

## Files Allowed To Modify
- Unity content assets only through the Editor, if needed.

## Files Allowed To Create
- None by blind text edit.

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Atlas/Skills.spriteatlasv2`
- `Assets/Scenes/BenchmarkLarge.unity`
- Skill projectile/AOE authoring assets and prefabs

## Behavior To Preserve
- Registry throws when a sprite is absent from the configured atlas.
- Single-page guard throws when sprites resolve to different packed textures.

## Behavior To Change
- Content completeness, if missing, through Unity Editor packables and Pack Preview.

## Relevant Global Context
- One indirect draw samples one atlas texture, so multi-page atlas or missing sprites breaks the design.

## Dependencies Confirmed
- Tasks 001-004 compile.
- Registry single-page guard is present.

## Step-By-Step Instructions
- Enumerate every sprite reachable through combat skill registration.
- Add all as packables to the assigned atlas.
- Verify Pack Preview page count is 1.
- Confirm `BenchmarkLarge.unity` assigns the complete atlas.

## Acceptance Criteria
- Runtime registration throws for none of the real combat sprites.
- Pack Preview shows one page.
- `BenchmarkLarge.unity` references the complete atlas.

## Validation Required
- Unity Editor Sprite Atlas Pack Preview.
- Runtime registration check.

## Hard Boundaries
- Do not blindly edit sprite atlas binary/YAML packables without Editor verification.
- Stop if Editor content work is required.
