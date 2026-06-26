# 003 — Expansion: dereference + stamp + explode

## Goal

Make expansion the single dereference-and-explode step: read a `SpawnInvocation`,
fetch its command template from the `[ReadOnly]` registry, stamp the per-instance
frame, explode template-level multiplicity, and emit existing command types into
the existing apply path.

## Changes

- [ProjectileSpawnExpansionSystem.cs](../../Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs)
  and [AoeSpawnExpansionSystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs):
  input becomes `SpawnInvocation`. For each:
  1. look up the template by `TemplateKey` in the registry map (passed `[ReadOnly]`);
  2. copy template to a local value;
  3. stamp `Position`, `AimDirection`, `Faction`, `SourceId`, `JitterSeed`,
     `DeterministicIdTickIndex`, `ContactGateSeedTargetId`;
  4. apply the template's spawn pattern (side-spray / radial / aim-back / 360°) and
     explode `Count` into one `ProjectileSpawnCommand` / `AoeSpawnCommand` per entity.
- If unified queue (open-question #2), route by `Kind` here to the correct domain.
- The registry template entry is never mutated (stamp the local copy only).

## Acceptance criteria

- A registered template + an invocation materializes the correct entity(ies)
  through the unchanged apply path.
- Per-instance stamping is correct: impact projectiles aim back; bursts fan around
  the impact point; contact-gate seed prevents re-hitting the just-hit target;
  tracking config survives (subsumes the three earlier point fixes).
- Determinism preserved (same seed/tick → same ids).

## Dependencies

001, 002.

## Scope

Medium–large. Core behavior change; downstream apply/materialize untouched.
