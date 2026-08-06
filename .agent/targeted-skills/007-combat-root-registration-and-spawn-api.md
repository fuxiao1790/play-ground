# 007 — `CombatRoot` registration, spawn API, gate routing

**Depends on:** 002, 003. **Scope:** medium. **Risk:** low.

## Why

Closes the managed→ECS boundary so compiled skills can register templates and cast. Mirrors the
existing AOE members on `CombatRoot` one-for-one (C4).

## Changes

`Assets/Scripts/System/Core/CombatRoot.cs`

Mirroring the AOE trio (`RegisterSpawnTemplate`, `RegisterTimedSpawnTemplate`,
`SpawnRegisteredAoe`):

- `Hash128 RegisterSpawnTemplate(in TargetedSpawnCommand template)` — content-hash via
  `SpawnTemplateHash.Of`, insert only if absent. Identical behaviour shares one entry; changing
  `count`, damage, or radii yields a different key.
- `Hash128 RegisterTimedSpawnTemplate(in TargetedSpawnCommand template)` — same registry, used by
  interval-triggered children.
- `int SpawnRegisteredTargeted(Hash128 key, Vector2 origin, Vector2 acquireAnchor, int count,
  CombatFaction faction, IntervalChildKind kind, Entity caster = default, float manaCost = 0f,
  int castToken = 0)` — appends an `ExternalSpawnRequest` carrying **both** positions. This is the
  one API-shape divergence from AOE, and it exists because a root cast draws from the caster but
  searches around the cursor (requirements §3.1).
- `int RegisterTargetedType(TargetedTypeDefinition definition)` — dedupes by reference and returns
  a `TypeId`, mirroring `RegisterType(AoeTypeDefinition)`.
- Sprite registration reuses the existing `CombatRenderResourceRegistry.Register` path and returns
  `0` when no sprite is authored, which is already the "no render" value.
- **Templates are written only here, from managed pre-tick code.** They are read `[ReadOnly]`
  during the tick (C4).

`Assets/Scripts/System/Spawning/ExternalSpawnGateSystem.cs`

- `ExternalSpawnRequest` gains `float2 AcquireAnchor`. Existing producers leave it default; the
  gate copies `Position` into it when it is default so projectile and AOE behaviour is unchanged.
- `AppendInternalSpawn` routes `IntervalChildKind.Targeted` and `LingeringTargeted` to the two new
  scope buffers, carrying the anchor through.
- Mana deduction, rejection, and `SkillDriver` cooldown refund are untouched — requirements
  decision 1: a cast that acquires nothing has already spent, exactly like every other root cast.

`Assets/Scripts/System/Spawning/TimedSpawnSystem.cs`

- Route the two new `ChildKind` values into the targeted lanes. `TimedSpawnComponent` already
  carries a single `ChildKind` + `TemplateKey`, so this is a routing branch and **no component
  change** — one source still supports one timed child kind.
- Stamp per-instance fields the same way the existing kinds do, including origin and anchor (both
  the source entity's position for an interval-spawned child).

## Acceptance criteria

- EditMode: registering the same `TargetedSpawnCommand` twice returns the same `Hash128`;
  changing `Count` returns a different one.
- EditMode: `SpawnRegisteredTargeted` with sufficient mana deducts and produces a spawn event
  carrying both origin and anchor.
- EditMode: with insufficient mana it produces a `SpawnRejectedEvent` and no spawn event.
- EditMode: an `ExternalSpawnRequest` with a default `AcquireAnchor` resolves the anchor to
  `Position` — proves existing projectile/AOE producers are unaffected.
- EditMode: a `TimedSpawnComponent` with `ChildKind = Targeted` emits into the targeted lane on
  energy accrual; with `LingeringTargeted`, into the lingering lane.
- EditMode: registering a targeted type twice by the same reference returns the same `TypeId`.
- All existing spawn-pipeline tests pass unchanged.

## Notes

The `AcquireAnchor` addition to `ExternalSpawnRequest` widens a struct every domain uses. It is one
`float2` with a default-means-`Position` rule, so no existing call site changes — the same
compatibility shape task 001 uses for `DamageScale`.
