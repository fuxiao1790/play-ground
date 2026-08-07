# 002 — Targeted components and spawn contracts

**Depends on:** nothing. **Scope:** medium, data only — no systems. **Risk:** the enum audit.

## Why

Establishes the domain's data before any system exists, so tasks 003–007 have a stable target.
Nothing here executes; the task lands types, tags, and the two new `IntervalChildKind` values.

## New files

`Assets/Scripts/System/Targeted/TargetedEcsComponents.cs`

Every declaration carries an `ECS Lifecycle:` comment (C12).

```csharp
// ECS Lifecycle: base targeted tag; added at entity creation; kept until root teardown;
// gates targeted systems from common combat components.
public struct TargetedTag : IComponentData { }

// ECS Lifecycle: interval-variant discriminator; added at entity creation; kept until root
// teardown; present only on interval targeted skills. Absence marks a single-hit chain.
public struct LingeringTargetedTag : IComponentData { }

// ECS Lifecycle: base targeted component; added by spawn materialization; reset on reuse.
public struct TargetedIdentityComponent : IComponentData
{
    public CombatFaction Faction;
    public int TargetedId;
    public int TypeId;
    public int InstanceIndex;   // fork index within one cast; drives rank-offset acquisition
}

// ECS Lifecycle: base targeted component; added by spawn materialization; reset on reuse and on
// interval walk restart. Authoritative walk state; CombatKinematicsComponent is a derived mirror.
public struct TargetedChainComponent : IComponentData
{
    public float2 Origin;          // where link 0's segment starts; restart returns here
    public float2 AcquireAnchor;   // where link 0 searches; equals Origin except for root casts
    public float2 LinkSource;      // current link's tail
    public float2 LinkTarget;      // current link's head — the target just hit
    public int LastTargetKey;      // whole exclusion state; survives interval walk restarts
    public int LinkIndex;
    public float LinkGateRemaining;
}

// ECS Lifecycle: base targeted component; added by spawn materialization; reset on reuse.
public struct TargetedResolveConfig : IComponentData
{
    public float AcquireRadius;
    public float ChainRadius;
    public float ChainDamageFalloff;
    public float ChainDelaySeconds;
    public int MaxTargets;
}

// ECS Lifecycle: base targeted component; added by spawn materialization; reset on reuse.
// Mirrors AoeVfxIds field-for-field in role: ids only, no scalars. LinkId takes the slot
// PulseId occupies on AOEs — both are the domain's repeating in-flight effect.
// Targeted does not reuse AoeVfxIds itself: that struct has no slot for a line segment.
public struct TargetedVfxIds : IComponentData
{
    public int SpawnId;    // circular, emitted by expansion when ArmSeconds == 0
    public int HitId;      // circular, optional flash on each hit target
    public int ExpireId;   // circular, emitted by the lifetime job
    public int LinkId;     // LineSegment, one per landed link — the shipped visual
    public int ArmingId;   // circular, emitted by expansion when ArmSeconds > 0
}

// ECS Lifecycle: base targeted component; added by spawn materialization; reset on reuse;
// carries fire-time VFX dispatch sizes. Mirrors AoeAreaComponent's role — the scalar the VFX
// emit is sized by — with two fields because this domain dispatches two shapes.
// Unlike AoeAreaComponent this is NOT a gameplay value: a chain has no area, so these are
// authored, visual-only, and do not fold through the AreaSize stat.
public struct TargetedVfxSizeComponent : IComponentData
{
    public float EffectSize;   // radius for the circular spawn / arming / expire / hit effects
    public float LinkWidth;    // width for the LineSegment link effect
}

// ECS Lifecycle: interval-only targeted component; added at entity creation; reset on reuse.
// Remaining counts down to the next walk restart; seeded to 0 so the first walk starts immediately.
public struct TargetedTickGateComponent : IComponentData
{
    public float TickIntervalSeconds;
    public float Remaining;
}
```

`Assets/Scripts/System/Targeted/TargetedSpawnPipeline.cs`

- `TargetedSpawnEvent` and `LingeringTargetedSpawnEvent` — `IBufferElementData`, matching the AOE
  event field set **plus** `float2 AcquireAnchor` (requirements §3.1). Both carry `Kind`,
  `TemplateKey`, `Position` (the origin), `AcquireAnchor`, `AimDirection`, `Faction`, `SourceId`,
  `JitterSeed`, `DeterministicIdTickIndex`, `ContactGateSeedTargetId`.
- `TargetedSpawnCommand` — resolved single-entity allocation intent, written out in full rather
  than described, because the per-instance stamping fields are easy to omit and task 003 cannot
  run without them:

  ```csharp
  // ECS Lifecycle: resolved single-entity allocation intent; produced by expansion, consumed by
  // apply. Registry templates use this same shape with per-instance fields left default.
  public struct TargetedSpawnCommand
  {
      public CombatFaction Faction;
      public int TargetedId;
      public int TypeId;
      public int RenderTypeId;
      public int InstanceIndex;              // fork index; drives rank-offset acquisition

      // Per-instance stamping frame — mirrors AoeSpawnCommand. Expansion writes these from the
      // event, then TargetedIdFor(command, i) reads them back to derive unique per-fork ids.
      public uint JitterSeed;
      public int DeterministicIdTickIndex;

      public float2 Origin;                  // segment 0 start; caster for a root cast
      public float2 AcquireAnchor;           // link 0 search centre; cursor for a root cast

      public int Count;
      public float LifetimeSeconds;
      public float TickIntervalSeconds;
      public float ArmSeconds;

      public CombatHitPayload HitPayload;
      public TargetedResolveConfig Resolve;
      public TargetedVfxIds VfxIds;
      public TargetedVfxSizeComponent VfxSize;
      public CombatRenderComponent Render;
      public CombatRenderAuthoring Authoring;
      public OnHitSpawnRef OnHitSpawn;
      public int HasTimedSpawner;
      public TimedSpawnComponent TimedSpawn;
  }
  ```

  `JitterSeed` and `DeterministicIdTickIndex` are **not optional**. `TargetedIdFor(command, i)`
  mirrors `AoeIdFor`: it returns `TargetedId + i` when the tick index is `<= 0`, and otherwise
  hashes id, seed, tick index, and fork index together. Without them, forks from a
  deterministically-expanded spawn (interval children, stack detonations) collide on the same id.
  There is no scatter seed — targeted has no `ScatterSeedFor` analogue, since it never displaces
  anything.

- `AimDirection` and `ContactGateSeedTargetId` stay on the **events** for shape parity with the
  other four spawn-event structs, but targeted uses neither: there is no aim fan, and exclusion is
  `LastTargetKey` in chain state, not a contact gate. They are deliberately absent from the
  command. Do not wire them up.
- `TargetedVfxUtility.TimingFor(in TargetedSpawnCommand command)` → `VfxTimingData`, mirroring
  `AoeSpawnApplyUtility.VfxTimingFor`: `Duration = LifetimeSeconds`,
  `TickInterval = TickIntervalSeconds`. Both are `0` on the single-hit variant, which is what a
  `Circular`-shaped effect wants. `VfxTimingData` is reused as-is — unlike `AoeVfxIds` it is
  domain-neutral and already carries exactly these two fields.
- `TargetedVariant.ChildKindFor(float lifetimeSeconds)` → `LingeringTargeted` when `> 0`, else
  `Targeted`. Mirrors `AoeVariant.AoeChildKindFor`.

`Assets/Scripts/System/Spawning/SpawnTemplateComponents.cs`

- Add `TargetedSpawnTemplate { NativeHashMap<Hash128, TargetedSpawnCommand> Map; }` with the same
  registry-contract lifecycle comment the other two carry (C4).

## Modified files

`Assets/Scripts/System/Spawning/IntervalChildTemplates.cs`

- `IntervalChildKind` gains `Targeted = 3` and `LingeringTargeted = 4`.

**Audit every switch/branch over `IntervalChildKind`.** A missed site fails silently as a spawn
that never happens:

- `ExternalSpawnGateSystem.AppendInternalSpawn` — route both new kinds to their scope buffers.
- `TimedSpawnSystem` — route both new kinds to the targeted lanes.
- `AoeCollisionCore` — on-hit spawn routing.
- `ProjectileDiscreteCollisionSystem` — impact spawn routing.
- `CombatRoot` — registration and spawn entry points.

Sites that only *store* the kind need no change; sites that *dispatch* on it do. Task 007 supplies
the routing bodies; this task's job is to make every dispatch site fail loudly (throw or assert on
unhandled kind) rather than fall through to a default.

`Assets/Scripts/System/Core/CombatScopeOwner.cs`

- Add `TargetedSpawnEvent` and `LingeringTargetedSpawnEvent` buffers and the
  `TargetedSpawnTemplate` registry to the shared scope entity bootstrap, disposing the map on final
  release (C13).

## Acceptance criteria

- Project compiles; all existing tests pass.
- Every `IntervalChildKind` dispatch site either handles the two new values or throws on them — no
  silent default branch.
- EditMode test: `TargetedVariant.ChildKindFor(0f)` is `Targeted`; `ChildKindFor(0.1f)` is
  `LingeringTargeted`.
- EditMode test: `TargetedVfxUtility.TimingFor` returns `Duration = 0, TickInterval = 0` for a
  single-hit command and the authored lifetime/interval for a lingering one.
- No targeted type references `AoeVfxIds` or `AoeAreaComponent` — grep-verifiable.
- `TargetedVfxIds` contains **only** `int` fields, matching `AoeVfxIds`. Sizes live on
  `TargetedVfxSizeComponent`, the way AOE sizes live on `AoeAreaComponent` and timing lives on
  `VfxTimingData`. A scalar creeping into the ids struct is a review failure.
- `TargetedSpawnCommand` carries `JitterSeed` and `DeterministicIdTickIndex`. Diff its field list
  against `AoeSpawnCommand` and account for every difference — each one should be either a
  targeted-specific addition (`AcquireAnchor`, `InstanceIndex`, `Resolve`, `VfxSize`) or a
  deliberate omission (collision shape, `AreaSize`, `EchoCount`, `ScatterRadius`,
  `ContactGateSeedTargetId`).
- EditMode test: acquiring and releasing a combat scope creates and disposes the
  `TargetedSpawnTemplate` map without leaking (mirrors the existing scope-registry test).
- Every new declaration has an `ECS Lifecycle:` comment.

## Notes

`TargetedSpawnEvent` and `LingeringTargetedSpawnEvent` are the fourth and fifth field-identical
spawn-event structs. This is the deferred structural warning — see
[index.md §6](./index.md#6-additive-vs-refactor-comparison) and the `refactor debt` entry in
`Docs/todo.md`. Do not attempt the collapse here.
