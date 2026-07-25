# 008 — Shared managed `Resource` type; rewire PlayerRoot + MobRoot

## Goal
One reusable managed resource abstraction, used by every unit for every resource.
Remove `PlayerHealth`, `PlayerMana`, and `MobRoot`'s inline health. Unit-specific
reactions (death freeze, hurt flash) stay in each root, driven by resource events.

## Design

1. **New `Resource` (a.k.a. `Vital`) managed type** — plain C#, agnostic:
   ```csharp
   public sealed class Resource
   {
       public Resource(float max, float regenPerSecond) { ... Current = Max = ...; }
       public float Max { get; private set; }
       public float Current { get; private set; }
       public float RegenPerSecond { get; private set; }
       public bool IsDepleted => Current <= 0f;
       public void SetMax(float max);            // GO owns Max; clamps Current
       public void MirrorCurrent(float current); // pull ECS-owned Current for presentation
       public event Action Depleted;             // fired when Current crosses to 0
       public event Action Changed;              // for UI
   }
   ```
   `Current` here is a **mirror** of the ECS authority, updated each frame from the
   proxy entity; `Max`/`RegenPerSecond` are the GO-owned authored values pushed to
   ECS. Decided: a **plain C# class** (not a `Vitals` MonoBehaviour). Health and
   mana use this same class — health is mana with a different name; both default
   `RegenPerSecond` to `0`.

2. **Stat sheet regen** — `Assets/Scripts/Common/Stats/UnitStatSheet.cs`: add
   `healthRegenPerSecond` (default `0`) and `manaRegenPerSecond` (default non-zero
   for testing, e.g. `5`) with clamped getters. `SetRuntimeValues` (used by
   `MobRoot.ConfigureAuthoring`) extended to carry regen if mobs need it.

3. **`ICombatTarget` mapping** — each root exposes `CombatMax*`, `CombatCurrent*`
   (initial), and `Combat*RegenPerSecond` from its `Resource`/sheet so
   `CombatTargetProxy.Create` (004) seeds from one place.

4. **`PlayerRoot`**
   - Replace `PlayerHealth`/`PlayerMana` fields with
     `Resource health; Resource mana;` seeded from the sheet.
   - Move the death/hurt reactions (body/collider/sprite/animator, hurt flash) out
     of `PlayerHealth` into `PlayerRoot`, subscribed to `health.Depleted` /
     `health.Changed`.
   - Each frame: push Max if changed, and mirror `Current` back from the proxy for
     both resources (see index perf note — pull only what presentation needs).
   - Save/load (`CapturePersistentState`/`Restore`) uses `Resource` + the ECS
     `SetHealth` restore path.

5. **`MobRoot`**
   - Replace inline `CurrentHealth`/`MaxHealth`/`ApplyCombatHealth`/`SoftDie`
     health bookkeeping with a `Resource health` (and `Resource mana` if mobs cast;
     otherwise mana seeds to 0 and is inert). `SoftDie` becomes a `health.Depleted`
     handler.
   - `InitializeForSpawn` seeds/reset the `Resource` (pool reuse must reset Current
     to Max).

6. **Delete** `Assets/Scripts/Player/PlayerHealth.cs` and
   `Assets/Scripts/Player/PlayerMana.cs` once callers move to `Resource`.

## Acceptance Criteria
- No `PlayerHealth`/`PlayerMana` types remain; `MobRoot` has no bespoke health
  fields — both use the shared `Resource`.
- Player and mob health behave exactly as before (damage, death, flash, pool reuse,
  save/load) through the unified type — verified by existing PlayMode tests.
- Player mana is visible and regenerates (ECS-owned Current mirrored to the root);
  mob mana seeds correctly (0 or authored) with no special-casing.
- Adding a resource to a unit is a stat-sheet value + seed mapping, no new bespoke
  holder.

## Dependencies
Depends on 004 (neutral components + push helpers) and 007 (regen owns Current).

## Scope
Large (touches both roots, deletes two types, save/load, pool reuse, tests).
