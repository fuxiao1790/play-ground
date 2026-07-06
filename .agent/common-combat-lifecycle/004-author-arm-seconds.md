# 004 — Author `ArmSeconds` through the skill/content pipeline

## Goal

Let content set arm time. Add an authored arm-seconds field to the projectile/AOE
skill definitions and thread it into the `ArmSeconds` command field added by 003.

## Depends on

**003** — needs `ProjectileSpawnCommand.ArmSeconds` / `AoeSpawnCommand.ArmSeconds`
to exist.

## Changes

- Authoring assets:
  [SkillDefinition.cs](../../Assets/Scripts/Skills/SkillDefinition.cs) — add a
  serialized `armSeconds` (>= 0) to the projectile and AOE definition types (mirror
  how `trackingInitialDelaySeconds` and other per-skill floats are declared/copied).
- Compiled runtime:
  [RuntimeProjectileDefinition.cs](../../Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs)
  and [RuntimeAoeDefinition.cs](../../Assets/Scripts/Skills/Runtime/RuntimeAoeDefinition.cs)
  — carry `ArmSeconds` from the authored definition.
- Command construction — set `command.ArmSeconds` from the runtime definition at
  every site that builds a `ProjectileSpawnCommand` / `AoeSpawnCommand`:
  [CombatRoot.cs](../../Assets/Scripts/System/Common/CombatRoot.cs),
  [PlayerSkillDriver.cs](../../Assets/Scripts/Skills/PlayerSkillDriver.cs),
  [ProjectileSpawnPipeline.cs](../../Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs),
  [AoeSpawnPipeline.cs](../../Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs)
  (confirm the full set during impl via the `ProjectileSpawnCommand` /
  `AoeSpawnCommand` construction sites).
- Interval / on-hit children: decide whether spawned children inherit or reset
  `ArmSeconds`. Default: children get `ArmSeconds = 0` (only the authored root
  arms) unless the child definition authors its own. State the choice explicitly in
  the command-build for child spawns.

## Acceptance criteria

- A skill authored with `armSeconds > 0` produces the frozen-then-live behavior
  end-to-end in PlayMode (visible telegraph, delayed first action).
- A skill with `armSeconds == 0` (default) is unchanged.
- No command-build site leaves `ArmSeconds` uninitialized where the definition sets
  it.

## Scope

Small–medium. Field plumbing across authoring → runtime → command; no new systems.
