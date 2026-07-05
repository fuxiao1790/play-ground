# 002 — Classify variant at authoring (main thread)

## Goal
Every site that authors a spawn-ref, timed-spawner, detonation snapshot, or main-thread AOE
spawn request must set the AOE `Kind` to `ImpactAoe` or `LingeringAoe` using the child's
`Lifetime`. All these sites run on the managed main thread and already have the child
definition/command (hence its lifetime) in scope, so classification is a pure local decision
(uses the `AoeChildKindFor` / `AoeDetonationKindFor` helper from task 001) — **no runtime
lookup**.

Depends on: 001.

## Sites to update
1. **`PlayerSkillDriver.BuildOnHitSpawnRef(RuntimeProjectileDefinition)`**
   ([PlayerSkillDriver.cs:428](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L428)) — the
   `ImpactAoeDefinition` branch: set `Kind = AoeChildKindFor(impactAoeDef.LifetimeSeconds)`.
2. **`PlayerSkillDriver.BuildOnHitSpawnRef(RuntimeAoeDefinition)`**
   ([PlayerSkillDriver.cs:456](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L456)) — the
   `OnHitAoeSpawnDefinition` branch: same.
3. **`PlayerSkillDriver.AoeTimedSpawnFromSetup`**
   ([PlayerSkillDriver.cs:498](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L498)) — set
   `TimedSpawnComponent.ChildKind` from the interval child AOE's lifetime.
4. **Detonation snapshot builder** `SkillIntervalTemplateBuilder.BuildApplicatorStackEffectSnapshot`
   ([PlayerSkillDriver.cs:835](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L835)) — the
   `DetonationKind = StackDetonationKind.Aoe` case becomes
   `AoeDetonationKindFor(detonationAoe.LifetimeSeconds)`.
5. **`CombatRoot.Spawn` / `AoeEventFor`** ([CombatRoot.cs:433](../../Assets/Scripts/System/Common/CombatRoot.cs#L433))
   — main-thread scope append: choose the impact vs lingering event struct + buffer by
   `request.LifetimeSeconds` (implemented in task 003's append edit; classification decided here).
6. **`CombatRoot.SpawnRegisteredAoe`** ([CombatRoot.cs:249](../../Assets/Scripts/System/Common/CombatRoot.cs#L249))
   — only has `templateKey`. **Prefer passing the variant from the caller**
   `SkillSpawnTranslator` ([SkillSpawnTranslator.cs:46](../../Assets/Scripts/Skills/SkillSpawnTranslator.cs#L46)),
   which has the `RuntimeAoeDefinition` (lifetime). Add a variant parameter rather than a
   `Hash128→variant` side table in `CombatRoot`.

## Notes
- `TimedSpawnComponent.ChildKind` and `OnHitSpawnRef.Kind` are both `IntervalChildKind`; they
  now carry the AOE variant directly, so the Burst producers in task 003 route with zero lookup.
- `DetonationSnapshot.Kind` is `StackDetonationKind`; carries the detonation AOE variant.
- Confirm no other `IntervalChildKind.Aoe` / `StackDetonationKind.Aoe` authoring sites remain
  (grep after edits). Tests author these directly — updated in task 006.

## Acceptance criteria
- No authoring site produces a bare "Aoe" kind; each resolves to `ImpactAoe`/`LingeringAoe`
  via the shared helper.
- `SpawnRegisteredAoe` receives the variant explicitly from its caller.

## Scope: medium (localized edits across ~5 authoring methods).
