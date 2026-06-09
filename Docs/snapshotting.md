# Hit-Spawn Fire-Time Snapshot — Design and Implementation

Status: final decision for Phase 3 (permanent doc)

Summary
- Cross-domain hit-spawn behavior (projectile -> AOE, AOE -> projectile, domain-to-domain effects) must be safe, deterministic, and compatible with Burst/Jobs-driven ECS simulation. To ensure lifetime/version safety and simple, fast collision-time code, we use fat fire-time snapshot payloads authored at the moment an attack is fired. This document records the decision, rationale, data model, implementation guidance, and test requirements.

Decision
- Use Option A: fat fire-time snapshot payloads. All data required later to emit cross-domain spawn commands is copied into plain-data payload fields on the spawn command and carried through projectile entities and hit elements. No live managed object lookups (no `GameObject`, `Transform`, `Collider2D`, or managed attack callbacks) are invoked during ECS collision; replay is performed from copied snapshot data in `LateUpdate()` on the managed root.

Why this choice
- Lifetime safety: in-flight projectiles must not depend on the continued existence or unmutated state of authored attack objects.
- Determinism: collision systems operate using plain, immutable data; no pointer-chasing or version semantics to reason about in jobs.
- Performance: collision and spawn-path code stays allocation-free and cache-friendly; replay/emit is a small, managed step off the hot path.
- Implementation complexity: easiest to reason about and test; matches existing direct-damage snapshotting behavior.

Options considered (short)
- Option A — fat snapshot payloads: chosen. Tradeoffs: larger memory and copying vs speed and safety.
- Option B — runtime lookup / pointer chase: smaller payloads, but introduces cache misses, lifetime/version complexity, and poor Job/Burst compatibility.
- Option C — managed authoring callback: flexible and editor-friendly but unsafe for in-flight entities, not Burst-compatible, and can reference mutated/dead managed objects.

Required rules and constraints
- Snapshot payloads must contain only blittable, plain-data fields (integers, floats, enums, small fixed-size value arrays, ids) — NO managed references, strings, or GC handles.
- Cross-domain snapshot data may contain type/template ids and parameter primitives (damage, count, spread, lifetime, routing/team id, position, direction, source id, target_mask, effect flags).
- Projectile/AOE ECS systems never call into authoring objects. They only read snapshot data and emit `SpawnRequest` records to scope buffers.
- Replay (managed mapping to typed spawn APIs and live GameObject-based prefabs) happens in `LateUpdate()` on the owning root (e.g., `ProjectileRoot.LateUpdate()`, `AoeRoot.LateUpdate()`), which reads the hit buffers and maps snapshot payloads into managed `SpawnCommand` calls.
- Cross-domain spawn commands must be non-chaining by shape: authored payloads may attach *one* specific cross-domain spawn payload. Do not allow replayed cross-domain spawns to carry additional cross-domain payloads.

Suggested data model (examples)
Note: these are documentation examples — adapt names to existing types and keep them blittable for ECS/Burst.

ProjectileSpawnCommand (authoring side — written once at fire time)
```csharp
public struct ProjectileSpawnCommand
{
  public int TemplateId;      // type/template lookup on managed side
  public int SourceEntityId;  // authoring/source actor id
  public int TeamId;          // routing/team
  public uint TargetMask;     // bitmask for target filtering
  public float Damage;        // base damage
  public float Lifetime;      // seconds
  public float2 Position;
  public float2 Direction;
  public byte EffectKind;     // enum: 0=None,1=ImpactAoe,2=BurstProjectiles, ...
  public int EffectTypeId;    // sub-type id for effect routing
  public int EffectCount;     // for bursts
  public float EffectSpread;  // degrees or radians
  // keep payload compact — prefer ints/floats/enums; avoid nested managed structures
}
```

Projectile ECS components / hit element (carried through runtime)
```csharp
public struct CombatHitComponent : IComponentData
{
  public int SourceId;
  public float Damage;
  public uint TargetMask;
  public byte EffectKind;
  public int EffectTypeId;
  public int EffectCount;
  public float EffectSpread;
}

public struct ProjectileHitElement : IBufferElementData
{
  public int HitEntityId;    // id mapped to managed target in LateUpdate
  public float2 HitPosition;
  public CombatHitComponent SnapshotPayload; // plain data copied at fire-time
}
```

Replay / Managed routing
- `ProjectileRoot.LateUpdate()` drains `ProjectileHitElement` buffer and for each element:
  - Map `HitEntityId` back to the managed `IAttackTarget` (via scope target registry).
  - Read `SnapshotPayload` and construct a typed spawn command for the target scope (e.g., `AoeRoot.Spawn(AoeSpawnCommand)` or `ProjectileRoot.Spawn(...)`).
  - Enforce cross-domain non-chaining rules (do not attach further cross-domain payloads when re-emitting).

Memory and performance guidance
- Pack payloads tightly: use enums/bitfields and small integer ids where possible.
- Prefer template/type ids over string names — resolve templates during the managed replay step.
- Avoid per-hit allocations: hit buffers should be preallocated and recycled per scope entity to avoid GC.
- Be conservative with large arrays in payloads; prefer parameterized templates (type id + small parameter set).
- If a payload contains optional sub-parameters, use small discriminator bytes and compact union-like layout.

Compatibility & migration notes
- Existing direct-damage snapshot behavior (damage + enable flags) should be extended to include any impact-AOE or burst-projectile parameters at fire-time.
- Update authoring code (`ProjectileAttack` and similar) to write any required effect parameters into `ProjectileSpawnCommand` when firing.
- Ensure all code that currently expects to query authoring objects at hit-time instead reads snapshot fields instead.

API and implementation checklist
- Authoring: `ProjectileAttack` writes `ProjectileSpawnCommand` with all cross-domain payloads at fire time.
- Spawn path: scene-facing API `ProjectileRoot.Spawn(ProjectileSpawnCommand)` must copy the command payload into projectile entity components/buffers (blittable data only).
- Collision: `ProjectileCollisionSystem` reads `CombatCollisionComponent` and when a hit occurs, appends a `ProjectileHitElement` containing snapshot payload to the scope `HitBuffer`.
- Replay: `ProjectileRoot.LateUpdate()` drains hits and replays managed callbacks (mapping ids to managed targets and calling target APIs). Replay emits typed spawn commands into other roots via their public `Spawn(...)` API.
- Root routing: implement a managed `CombatSpawnRouter` (wired by `GameRoot`) that maps effect/template ids and team routing to destination roots (player→mob roots, mob→player roots).

Testing checklist
- Unit/PlayMode tests:
  - Fire projectile with impact-AOE snapshot; confirm hit produces AOE via replay on next frame.
  - Mutate the authoring `ProjectileAttack` after firing; verify in-flight projectiles still use original snapshot.
  - Ensure cross-domain spawned projectiles/AOEs do not carry further cross-domain payloads (non-chaining behavior).
  - Stress test: high volume projectile hits produce stable memory and reasonable performance counters.

Documentation & authoring guidance
- Document the exact fields available in spawn commands in `player-attacks.md` and `projectile-system.md` (authoring reference). Keep examples for common effect patterns (impact-AOE, burst, spread).
- Educate designers/content authors: attaching large effect payloads increases per-projectile memory; prefer reusable templates + parameter slots when possible.

Notes and future work
- If future features require deep, content-driven chaining, add an explicit authored combo system rather than enabling unrestricted runtime chaining.
- Consider a compact binary payload encoding for extremely hot paths (e.g., 16-byte payloads) if memory proves limiting.

End of document
