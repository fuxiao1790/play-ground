# 006 — Relocate SkillUi into `Assets/Scripts/`; repoint Editor/Test asmdefs; drop the package

**Depends on:** 004, 005 (Sim + GameLogic assemblies exist). **Scope:** medium.

## Objective
Bring the UI assembly under `Assets/Scripts/` like the rest, remove the embedded
package, and make every consumer reference the renamed assemblies — each referencing
exactly the assemblies whose types it names.

## Changes
1. **Relocate SkillUi** (with `.meta`, GUIDs preserved):
   - Move `Packages/com.playground.skill-ui/Runtime/{SkillLoadoutUi,PlayerSaveController,SkillUiCatalog}.cs`
     and `PlayGround.SkillUi.asmdef` → `Assets/Scripts/SkillUi/`.
   - In the asmdef, replace the `PlayGround.Runtime` reference with
     `PlayGround.GameLogic`; keep `Unity.InputSystem`. Add `PlayGround.Sim` only if
     UI code names sim types (verify by grep — references are not transitive).
   - Delete the package: remove `Packages/com.playground.skill-ui/` (package.json,
     Runtime.meta, etc.) and its `"com.playground.skill-ui": "file:…"` line in
     `Packages/manifest.json`. `packages-lock.json` regenerates.
2. **Repoint remaining asmdefs** off `PlayGround.Runtime`:
   - `Assets/Editor/PlayGround.Editor.asmdef`: `PlayGround.Runtime` →
     `PlayGround.Sim` and/or `PlayGround.GameLogic` (the subset it uses).
   - `Assets/Tests/EditMode/…` and `Assets/Tests/PlayMode/…`: `PlayGround.Runtime` →
     `PlayGround.Sim` + `PlayGround.GameLogic` (tests call both).

## Acceptance criteria
- No `.asmdef` anywhere references `PlayGround.Runtime`; none reference the old
  package.
- `Packages/manifest.json` no longer lists `com.playground.skill-ui`; the folder is
  gone; the scene's `UIDocument`/`SkillLoadoutUi` script GUIDs still resolve.
- Reference graph is acyclic and minimal: `SkillUi → GameLogic → Sim`; Editor/Tests
  may reference Sim and GameLogic.

## Verification (user, Unity)
- Full project compiles; the skill-edit UI still loads and functions.
