# 005 — SkillDriver Caster-Snapshot Fix

## Change

`Assets/Scripts/Skills/SkillDriver.cs`:
- Replace `private Entity casterProxy;` (line 53) with `private ICombatTarget casterOwner;`.
- Change `public void BindCaster(Entity proxy)` (line 139-141) to
  `public void BindCaster(ICombatTarget owner) { casterOwner = owner; }`.
- At the `Tick()` call site (line 111, currently passing `casterProxy` into
  `SkillSpawnTranslator.Spawn(...)`), read the live value instead:
  `casterOwner?.CombatTargetProxy ?? Entity.Null`.

## Why This Is a Real Bug, Not a Style Preference

`casterProxy` is a **snapshot taken once**, at `BindCaster` time, and never refreshed.
Today this is harmless because `Create()` sets `target.CombatTargetProxy` synchronously
before `BindCaster` is ever called (`PlayerRoot.Register`/`MobRoot.Register` call
`targetRegistry.Register(this)` — which synchronously creates the proxy today — then
immediately `skillDriver?.BindCaster(combatTargetProxy)`). Once `Create` becomes
deferred (002), the proxy is guaranteed `Entity.Null` for at least one tick after
registration. `BindCaster` would snapshot that `Entity.Null` and — because nothing ever
calls `BindCaster` again for that actor — every future cast from that actor would send
`Caster = Entity.Null` to `ExternalSpawnRequest`, permanently, not just for one tick.
Storing the owner and reading its live property instead fixes this by construction: no
matter when the proxy actually resolves, the next `Tick()` sees the current value.

## Downstream Consumers Checked

Grepped `.Caster` usage: only `ExternalSpawnGateSystem.TrySpendMana` (mana-gate check)
and the spawn-rejection notification path read the `Caster` field downstream. Both
already tolerate an `Entity.Null` caster today (as a fallback case — see index.md Open
Questions re: `TrySpendMana`'s "missing caster = spend succeeds" behavior), so a
lazily-resolving value that's `Entity.Null` for the first tick and then correct
afterward is a strict improvement over "wrong forever," not a new failure mode.

## Acceptance Criteria

- `SkillDriver.BindCaster` no longer takes an `Entity`; both call sites (`PlayerRoot`,
  `MobRoot`, updated in 006) pass `this`.
- A skill cast fired several ticks after registration (i.e. after the proxy has
  resolved) sends the correct, non-null caster entity — regression check against
  current behavior.
- A skill cast fired in the same tick as registration (before the proxy resolves) does
  not throw and does not permanently poison future casts once the proxy resolves.

## Dependencies

No code dependency on 001-004. Sequenced before 006 only because 006 updates the
`PlayerRoot`/`MobRoot` call sites that must match this new signature.

## Scope

Small. Three touch points in one file.
