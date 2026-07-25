# 004 — Neutral typed ECS resource components + GO/ECS ownership split

## Goal
Replace the `Target`-prefixed, split-by-concept ECS resources with neutral,
agnostic typed components that carry regen and follow the ownership model:
GameObject owns Max + initial + regen-rate (pushes on change); ECS owns Current +
regen tick.

## Changes

1. **New file** (move the resource components out of `CombatTargetProxy.cs` so they
   are not tied to "target" identity) — e.g.
   `Assets/Scripts/System/.../Resources/UnitResources.cs`:
   ```csharp
   // ECS Lifecycle: seeded once at proxy create from the unit's stat sheet.
   // GameObject owns Max + RegenPerSecond (pushes on change); ECS owns Current.
   public struct Health : IComponentData { public float Current; public float Max; public float RegenPerSecond; }
   public struct Mana   : IComponentData { public float Current; public float Max; public float RegenPerSecond; }
   ```
   Rename from `TargetHealth`/`TargetMana`. Update every reference:
   - `CombatApplyFinalizeSingleSystem.cs` (L179, L196, L301-306): `HealthLookup`
     type + reads. **Keep the direct `ComponentLookup` access shape** — only the
     type name changes.
   - `CombatTargetProxy.cs`: archetype (L307 area), seed in `Create` (L105-110),
     and `SetHealth`/`SetMana` helpers.
   - Tests/docs referencing `TargetHealth`/`TargetMana`
     (`ProjectileCollisionSimulationTests`, `AoeSimulationTests`,
     `target-proxy.md`, `ecs-simulation.md`, etc.).

2. **`CombatTargetProxy.cs`**
   - Seed both resources at `Create` from `ICombatTarget`:
     `Current = clamp(initialCurrent, 0, Max)`, `Max = CombatMaxHealth/Mana`,
     `RegenPerSecond = CombatHealthRegen/CombatManaRegen`.
   - Add a **Max/regen push** helper (GameObject owns Max):
     `PushHealthMax(target)` / `PushManaMax(target)` (or one
     `PushResourceMaxes(target)`) that writes `Max` + `RegenPerSecond` **without**
     clobbering ECS-owned `Current` (clamp Current to new Max only).
   - Keep a `SetHealth`/`SetMana(current)` for explicit seeds/restores (save-load).

3. **`ICombatTarget.cs`** — add regen members beside the existing resource members:
   ```csharp
   float CombatHealthRegenPerSecond => 0f;
   float CombatManaRegenPerSecond => 0f;
   ```
   (Existing `CombatMaxHealth`/`CombatCurrentHealth`/`CombatMaxMana`/`CombatCurrentMana`
   stay; `CombatCurrent*` are used only for the initial seed now.)

## Acceptance Criteria
- No `TargetHealth`/`TargetMana` identifiers remain (renamed to `Health`/`Mana`).
- Hot apply path still mutates `Health.Current` via direct `ComponentLookup`
  (no buffer/scan introduced).
- Proxy seeds `Health`/`Mana` with `Max`/`RegenPerSecond` from the sheet and
  `Current` from the initial value; project compiles; existing health behavior
  unchanged.
- A Max-change push updates `Max` without resetting `Current` (add a test).

## Dependencies
Supersedes the first-pass `TargetMana`. Blocks 007, 008.

## Scope
Medium (rename touches several systems/tests/docs).
