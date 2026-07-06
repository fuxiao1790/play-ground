# 006 - Arming VFX Path

## Goal

Add arming VFX as a visual-only path separate from sprite rendering.

## Scope

- Define an arming VFX trigger, likely:

  ```text
  Vfx trigger 4 = arming
  ```

- Decide implementation shape:
  - trigger-only request whose duration is authored in the VFX asset, or
  - extend `VfxPendingSpawn` with a visual-only duration field.
- Emit arming VFX when lifecycle starts in `Arming`.
- Stop relying on sprite render for telegraph visibility.
- Do not route damage/status/spawn authority through VFX.

## Acceptance Criteria

- Arming projectiles/AOEs can show VFX while sprite gate is disabled.
- Armed entities show sprite and no longer need arming VFX.
- VFX requests remain visual-only and job-safe.
- Existing spawn/hit/expire/pulse VFX triggers keep behavior.

## Dependencies

003 and 004. Can be implemented after core lifecycle is green.

## Estimated Scope

Medium, possibly high if VFX duration must be added to request data and dispatch.
