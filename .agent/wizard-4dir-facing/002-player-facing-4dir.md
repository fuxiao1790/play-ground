# 002 — PlayerFacing 4-quadrant rewrite

**File**: `Assets/Scripts/Player/PlayerFacing.cs` (rewrite in place)
**Dependencies**: 001
**Scope**: small (single class body)

Public surface (`AimDirection`, `AimAt(Vector2)`) unchanged so `PlayerRoot`/`SkillDriver` callers are unaffected. Constructor gains `SpriteFacingSet facingSet`; sets `spriteRenderer.flipX = false` and assigns the initial sprite (down-right — matches initial `aimDirection = Vector2.right`, y=0 → down bucket).

`AimAt`: keep the existing degenerate-vector early-return (holds last facing) and normalize; then bucket with hysteresis:

- `if (Mathf.Abs(aimDirection.x) >= AxisDeadzone) facingRight = aimDirection.x > 0f;`
- `if (Mathf.Abs(aimDirection.y) >= AxisDeadzone) facingUp = aimDirection.y > 0f;`
- reassign `spriteRenderer.sprite` only when a bucket changed.

`AxisDeadzone = 0.05f` on the normalized vector ≈ ±2.9° band around each axis — no left/right flicker when aiming near straight up/down.

## Acceptance criteria

- No `flipX` writes remain anywhere on the player path.
- Sprite reassigned only on bucket change; no per-frame allocations.
- `AimDirection` values identical to before the change.
