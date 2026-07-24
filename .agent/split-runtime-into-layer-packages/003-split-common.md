# 003 — Split `Common` into shared primitives vs game-logic

**Depends on:** 002 (`Common → sim` edge already removed). **Scope:** small.

## Objective
`Common` currently mixes plain primitives the sim needs with game-logic authoring.
Partition it so the sim can own only what it uses, keeping the dependency one-way.

## Facts (verified by grep)
- Sim uses from `Common`: `DamageSnapshot`, `GameplayTags`, `GameplayLayers` only
  (16 refs across 5 System files).
- Sim uses **none** of: `Common.Stats.UnitStatSheet`, `Common.StatusEffects.*`,
  `PersistentScriptableObject`.

## Changes (grouping only — physical move happens in 004/005)
- Mark for **sim** package: `Common/DamageSnapshot.cs`, `Common/GameplayTags.cs`,
  `Common/GameplayLayers.cs`.
- Mark for **game-logic** package: `Common/Stats/**`, `Common/StatusEffects/**`,
  `Common/PersistentScriptableObject.cs`.
- No namespace change required (a namespace may span assemblies). Optional cosmetic:
  rename the sim-side trio to `PlayGround.Sim.Shared` later.

## Acceptance criteria
- The three sim-shared files reference no game-logic type (they are plain structs /
  static tag classes — confirm no `using PlayGround.(Skills|...)`).
- Game-logic `Common` files may reference the sim-shared trio and sim contracts
  (legal downward).
