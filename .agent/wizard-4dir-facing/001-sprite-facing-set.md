# 001 — New SpriteFacingSet ScriptableObject

**File**: `Assets/Scripts/Player/SpriteFacingSet.cs` (new)
**Dependencies**: none
**Scope**: small (one sealed SO class)

Data table holding the 4 directional sprites, per the ScriptableObject Rule. `CreateAssetMenu` path `PlayGround/Player/Sprite Facing Set`. Fields `upLeft/upRight/downLeft/downRight`, `HasAllSprites` guard for fail-fast validation, `Resolve(bool up, bool right)` lookup (named `Resolve` to avoid shadowing the `Sprite` type).

No hand-written `.meta` — Unity generates it on import.

## Acceptance criteria

- Compiles under `PlayGround.Player` namespace, sealed, one class per file (project conventions).
- `Assets → Create → PlayGround → Player → Sprite Facing Set` menu appears in the editor after compile.
