# 004 — Player mana on stat sheet + `TargetMana` ECS resource + `PlayerMana` mirror

## Goal
Give the character a mana resource authored on `UnitStatSheet` and **owned by ECS
like health**. Mirror the `TargetHealth` seed/own pattern exactly. No consumption
logic in this task (see Decision E in index) — mana is seeded and readable so the
resource "exists in ECS like health."

## Changes

1. **`Assets/Scripts/Common/Stats/UnitStatSheet.cs`**
   - Add under a `[Header("Vitals")]` (near `maxHealth`):
     `[SerializeField] private float maxMana = 100f;`
   - Add `public float MaxMana => Mathf.Max(0f, maxMana);`
   - Non-zero default so testing has a value immediately.

2. **`Assets/Scripts/System/Targets/ICombatTarget.cs`** — add default members
   mirroring the health ones (L82-83):
   ```csharp
   float CombatMaxMana => 0f;
   float CombatCurrentMana => CombatMaxMana;
   ```
   Default `0f` keeps mobs (which use the interface defaults) mana-less unless
   they opt in.

3. **`Assets/Scripts/System/Targets/CombatTargetProxy.cs`**
   - Add a component beside `TargetHealth` (L37):
     ```csharp
     // ECS Lifecycle: target-proxy mana; seeded once when the proxy is created,
     // then owned by ECS until the proxy is destroyed. Mirrors TargetHealth.
     public struct TargetMana : IComponentData { public float Current; public float Max; }
     ```
   - Add `typeof(TargetMana)` to the archetype (L263-280).
   - In `Create` (after the `TargetHealth` seed, L97-99):
     ```csharp
     float maxMana = math.max(0f, target.CombatMaxMana);
     float currentMana = math.clamp(target.CombatCurrentMana, 0f, maxMana);
     entityManager.SetComponentData(entity, new TargetMana { Current = currentMana, Max = maxMana });
     ```
   - Add a `SetMana(ICombatTarget, float)` helper mirroring `SetHealth`
     (L173-192) for future consumption / restore parity.

4. **New `Assets/Scripts/Player/PlayerMana.cs`** — a minimal runtime holder
   mirroring `PlayerHealth`: constructor takes `maxMana`, initializes
   `CurrentMana = MaxMana`; expose `MaxMana`, `CurrentMana`, and a
   `MirrorCombatMana(float)` for when ECS consumption lands. No regen/consume yet.

5. **`Assets/Scripts/Player/PlayerRoot.cs`**
   - Construct `PlayerMana` in `Awake` from `statSheet.MaxMana` (beside the
     `PlayerHealth` construction, L144).
   - Implement the interface members:
     `public float CombatMaxMana => statSheet.MaxMana;`
     `public float CombatCurrentMana => mana?.CurrentMana ?? 0f;`
   - Optionally expose `CurrentMana` for UI parity with `CurrentHealth` (L82).

## Notes
- `MobRoot` also implements `ICombatTarget`; it inherits the `0f` mana defaults,
  so mob proxies seed `TargetMana{0,0}` — inert and harmless. Give mobs mana later
  only if they need to cast.
- No mirror-back in `ReceiveCombatTick` yet because nothing in ECS writes mana in
  this pass. When Decision E adds consumption, mirror mana there exactly like
  `MirrorCombatHealth` (PlayerRoot L267).
- Adding `TargetMana` to the archetype only *adds* a component; existing queries
  (health/status/collision) are unaffected.

## Acceptance Criteria
- `UnitStatSheet` exposes `MaxMana`; player stat sheet asset shows a Mana field.
- At runtime the player's target-proxy entity has `TargetMana` with
  `Max == statSheet.MaxMana` and `Current == Max` (verify via Entities debugger or
  a PlayMode test that inspects the proxy).
- Project compiles; existing health behavior unchanged.

## Dependencies
Independent of 001–003. Blocks 005 verification.

## Scope
Medium.
