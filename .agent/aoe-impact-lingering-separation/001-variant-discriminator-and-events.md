# 001 — Variant discriminator + event types

## Goal
Introduce the two variant-typed AOE spawn events and extend the two `Kind` enums so the rest
of the pipeline can route by variant. This task only *adds* types/cases; behavior is
unchanged until later tasks consume them.

## Changes
1. **Extend `IntervalChildKind`** ([IntervalChildTemplates.cs:3](../../Assets/Scripts/System/Common/IntervalChildTemplates.cs#L3)):
   ```
   enum IntervalChildKind { Projectile = 0, ImpactAoe = 1, LingeringAoe = 2 }
   ```
   Keep `Projectile = 0`. Replace the single `Aoe = 1`. (Grep every `IntervalChildKind.Aoe`
   use — they become one of the two, handled in tasks 002/003; this task just defines the
   enum and leaves a compile error surface for those to resolve, or temporarily map `Aoe`
   as an alias if a single atomic commit is preferred.)
2. **Extend `StackDetonationKind`** ([StackEffectSnapshot.cs:3](../../Assets/Scripts/System/Status/StackEffectSnapshot.cs#L3)):
   ```
   enum StackDetonationKind { None = 0, ImpactAoe, LingeringAoe, Projectile }
   ```
   Replace the single `Aoe` case.
3. **Add two event structs** in [AoeSpawnPipeline.cs](../../Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs),
   each an exact field copy of today's `AoeSpawnEvent`, both `IBufferElementData`:
   `ImpactAoeSpawnEvent`, `LingeringAoeSpawnEvent`. Remove `AoeSpawnEvent`.
   - Rationale for two distinct structs (not one reused type): a scope entity cannot hold two
     `DynamicBuffer<T>` of the same element type, and both main-thread spawn paths append to
     scope buffers.
4. **Register both scope buffers** on the shared combat scope entity wherever the
   `AoeSpawnEvent` buffer is added today (audit: the `GetBuffer<AoeSpawnEvent>` calls in
   `CombatRoot` at lines 260/364/698 require it to be in the scope archetype — find the
   archetype/`AddBuffer` site and add `ImpactAoeSpawnEvent` + `LingeringAoeSpawnEvent`,
   removing the old one).
5. **Add a classifier helper** (small static, e.g. on `AoeSpawnPipeline` or a new
   `AoeVariant` helper):
   ```
   static IntervalChildKind AoeChildKindFor(float lifetimeSeconds)
       => lifetimeSeconds > 0f ? IntervalChildKind.LingeringAoe : IntervalChildKind.ImpactAoe;
   static StackDetonationKind AoeDetonationKindFor(float lifetimeSeconds)
       => lifetimeSeconds > 0f ? StackDetonationKind.LingeringAoe : StackDetonationKind.ImpactAoe;
   ```
   Single source of the `Lifetime > 0` rule, reused by every authoring site in task 002.

## Acceptance criteria
- Two enums extended; `AoeSpawnEvent` replaced by the two structs; both buffers registered.
- Classifier helper exists and is the only place `Lifetime > 0` maps to a variant.
- Project compiles once tasks 002/003/004 land (this task may be committed together with 002–004
  if a single atomic change is preferred — see commit-safety note below).

## Commit safety
Because `IntervalChildKind.Aoe` and `AoeSpawnEvent` are removed, the tree does not compile
between 001 and 004. Land 001→004 as one reviewable unit (or a short-lived branch), not as
four independently-shipped commits. Subtask files stay separate for review clarity.

## Scope: small–medium (type/enum surface + scope-archetype edit).
