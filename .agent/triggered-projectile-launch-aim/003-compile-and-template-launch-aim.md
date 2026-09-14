# 003 - Compile And Template Launch Aim

## Change

Extend `RuntimeProjectileDefinition` and `ProjectileSpawnCommand` with launch-aim
mode/range. After each incoming trigger compiles its target, copy trigger policy
only when target is `RuntimeProjectileDefinition`:

- interval child
- on-hit child
- stack detonation child
- other implemented trigger edges using same compiler path

Do not stamp top-level/root runtime projectile. `SkillIntervalTemplateBuilder`
copies compiled values into command-shaped template. Keep fields in normalized
template so existing whole-struct `SpawnTemplateHash` distinguishes templates with
different launch-aim policy/range.

No policy field is added to `ProjectileSpawnEvent`; event remains template key plus
per-instance frame.

## Acceptance Criteria

- Triggered projectile runtime copy carries incoming link's mode and clamped range.
- Root projectile runtime copy remains `None` even when same source asset also
  appears as triggered effect elsewhere.
- Continuous and discrete runtime projectile definitions carry same policy without
  changing `ContinuousCollision` or `Tracking`.
- Trigger links targeting non-projectile effects do not alter those definitions.
- Registered projectile template contains policy; normalized template retains it.
- Template hashes differ when only mode or range differs and deduplicate identical
  policy/content.
- Spawn event size/fields unchanged.

## Dependencies

Depends on `002-author-trigger-launch-aim.md`.

## Estimated Scope

Medium: compiler edge handling, runtime snapshot, command template, hash tests.
