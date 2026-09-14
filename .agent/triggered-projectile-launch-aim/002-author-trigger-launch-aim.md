# 002 - Author Trigger Launch Aim

## Change

Add shared `ProjectileLaunchAimMode` enum:

- `None = 0`
- `NearestHostile = 1`

Add trigger-link authoring fields for projectile launch-aim mode and acquisition
range. Keep enum zero as default for existing assets. Expose non-negative resolved
range through `TriggerLink`; no automatic enablement or migration.

Fields belong to trigger edge because launch aim changes how triggered effect is
launched, not projectile skill's root-cast behavior. Values are inert when trigger
target compiles to AOE/targeted effect.

## Acceptance Criteria

- Every trigger link can author `None` or `NearestHostile` plus range.
- Existing trigger assets deserialize to `None` and keep current behavior.
- Negative serialized range resolves to zero.
- Projectile skill definition gains no launch-aim field.
- Homing/tracking and continuous-collision authoring unchanged.
- Inspector tooltip states policy affects projectile effects spawned through this
  trigger only; root/player cast aim is unaffected.

## Dependencies

None.

## Estimated Scope

Small: enum plus base trigger authoring fields/accessors.
