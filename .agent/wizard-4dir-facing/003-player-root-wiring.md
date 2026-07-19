# 003 — PlayerRoot wiring

**File**: `Assets/Scripts/Player/PlayerRoot.cs` (3 small diffs)
**Dependencies**: 001, 002
**Scope**: trivial

1. After the `statSheet` field (line 30): `[SerializeField] private SpriteFacingSet facingSet;`
2. In `Awake()` after the `statSheet` throw (line 103), fail-fast per coding standards:
   `if (facingSet == null || !facingSet.HasAllSprites) throw new MissingReferenceException($"{nameof(PlayerRoot)} on {name} needs a fully assigned {nameof(SpriteFacingSet)}.");`
3. Line 125: `facing = new PlayerFacing(transform, spriteRenderer, facingSet);`

Leave `Configure(...)` untouched — editor-builder path, already stale vs the existing `statSheet` validation.

## Acceptance criteria

- Compiles; `Facing Set` field appears on PlayerRoot in the inspector.
- Entering Play with the field unassigned throws a clear `MissingReferenceException` (expected until 004-C wires it).
