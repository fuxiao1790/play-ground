---
name: editor-steps-handoff
description: Hand the user the two Unity-editor-only steps this plan cannot perform by editing YAML directly
---

# 009 — Editor Steps Handoff (User Action)

## Goal
Two remaining changes are Unity ScriptableObject asset edits. Per established project convention,
agents do not hand-edit `.asset` YAML — these are instructions for the user to perform in the
Unity Editor, done last (after tasks 001–008 land and the project compiles clean).

## Not a coding task
No files are modified by this task. This is a message to relay to the user.

## Steps for the User

1. **Remove the `OnHitTrigger` entry from the trigger catalog.**
   - Open `Assets/ScriptableObjects/UI/SkillBar/TriggerCatalog.asset` in the Inspector.
   - Remove the list entry that references `OnHitTrigger.asset` (the only asset that referenced
     it, confirmed by GUID search — no `SkillLoadout` asset references it).

2. **Delete the `OnHitTrigger` asset.**
   - Delete `Assets/ScriptableObjects/Skills/Triggers/OnHitTrigger.asset` (and its `.meta`) from
     the Project window, or via `Assets > Delete`.
   - Do this only after step 1, so the catalog doesn't briefly hold a dangling reference.

3. **Verify no other asset references it** (belt-and-suspenders — already confirmed by GUID search
   during planning, but Unity's own "Find References in Scene/Project" can double-check before
   deleting): search for GUID `413cb78f04f1d7d4b8f66cf67d062bbc`.

## Acceptance Criteria
- `TriggerCatalog.asset` no longer lists `OnHitTrigger`.
- `OnHitTrigger.asset` no longer exists in the project.
- Unity shows no missing-reference warnings after the deletion.
