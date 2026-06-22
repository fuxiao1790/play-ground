# 006 — TargetHealth component (ECS-owned), seeded at proxy creation

**Change:** add · **Depends:** — · **Scope:** small

## Goal

Give the target proxy an ECS-owned HP component. This is the foundation for
applying damage in ECS (007) and dispatching per-tick HP (008). The GameObject
never writes it after seeding.

## Changes

1. **`TargetHealth`** (new `IComponentData`, in
   [CombatTargetProxy.cs](../../Assets/Scripts/System/Common/CombatTargetProxy.cs)
   next to `TargetStackEntry`):
   ```csharp
   public struct TargetHealth : IComponentData { public float Current; public float Max; }
   ```

2. **Add to the proxy archetype** — extend the `CreateArchetype` call
   ([CombatTargetProxy.cs:195-202](../../Assets/Scripts/System/Common/CombatTargetProxy.cs#L195))
   with `typeof(TargetHealth)`.

3. **`ICombatTarget.CombatMaxHealth`** — new read-only member
   ([ICombatTarget.cs](../../Assets/Scripts/System/Common/ICombatTarget.cs)) so the
   proxy can seed HP once. `MobRoot` returns `MaxHealth`
   ([MobRoot.cs:67](../../Assets/Scripts/Mob/MobRoot.cs#L67)); `PlayerRoot` returns
   `health.MaxHealth` ([PlayerHealth.cs:34](../../Assets/Scripts/Player/PlayerHealth.cs#L34)).

4. **Seed at `Create`** — in `CombatTargetProxy.Create`
   ([CombatTargetProxy.cs:68-73](../../Assets/Scripts/System/Common/CombatTargetProxy.cs#L68)),
   set `TargetHealth { Current = Max = target.CombatMaxHealth }`. `Push` does
   **not** touch `TargetHealth` — ECS owns it after creation.

## Acceptance criteria

- New proxies carry `TargetHealth` seeded to the target's max health.
- `Push` never overwrites `TargetHealth`.
- Compiles; nothing reads `TargetHealth` yet (007 applies, 008 pushes) — behavior
  unchanged.

## Notes / risks

- Seeding reads the target's max health at creation. Mobs set
  `CurrentHealth = maxHealth` in `Awake` before the proxy is created, so seeding
  from `CombatMaxHealth` is correct at spawn.
- Keep `Max` for clamp/feedback reference on the GameObject side even though ECS
  does not clamp.
