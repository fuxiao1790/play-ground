# 003 — Update Compiler (PlayerSkillDriver)

## File
`Assets/Scripts/Skills/PlayerSkillDriver.cs`

## Dependency
Requires 001 and 002 (registry API + `CombatRoot.RenderRegistry` must exist).

## Context

After 002, `CombatRoot.RegisterRenderResource` no longer exists. The compiler
currently calls:

```csharp
projDef.TypeId  = combatRoot.RegisterTemplate(projDef.Prefab);
projDef.RenderId = combatRoot.ProjectileRenderId(projDef.TypeId);
```

and

```csharp
aoeDef.TypeId  = combatRoot.RegisterType(definition);
aoeDef.RenderId = combatRoot.AoeRenderId(aoeDef.TypeId);
```

After this step, the compiler registers render resources itself via the registry.

## Changes

**Projectile registration (`RegisterProjectileTypesRecursive`)**

Replace:
```csharp
projDef.TypeId   = combatRoot.RegisterTemplate(projDef.Prefab);
projDef.RenderId = combatRoot.ProjectileRenderId(projDef.TypeId);
```
With:
```csharp
projDef.TypeId   = combatRoot.RegisterTemplate(projDef.Prefab);
projDef.RenderId = combatRoot.RenderRegistry?.Register(
    projDef.Prefab.Sprite,
    new Vector2(projDef.Prefab.VisualScale, projDef.Prefab.VisualScale),
    projDef.Prefab.VisualRotationDegrees,
    projDef.Prefab.Material,
    CombatRenderResourceRegistry.ProjectileMeshName,
    combatRoot.Faction,
    combatRoot.Layer) ?? 0;
```

Note: `RegisterTemplate` stays — it still mints the behavior typeId and populates
`projectileRenderIdByType` in CombatRoot (used by the `Spawn(Request)` path). But the
compiler now also directly mints the render resource from the registry, skipping the
CombatRoot intermediary for its own `RenderId` stamp.

**Wait — this causes a double-registration.** `RegisterTemplate` calls
`projectileRenderIdByType[typeId] = RegisterRenderResource(...)` (pre-002 behavior). After
002, `RegisterTemplate` no longer calls `RegisterRenderResource` — it only mints typeId
and calls `_renderRegistry.Register(...)` to populate the local typeId→renderId map and the
registry. So the compiler calling `registry.Register` directly would allocate a *second*
GPU resource for the same sprite.

**Resolution**: `RegisterTemplate` must NOT call `registry.Register` internally after 002.
It must only mint the behavior typeId. The caller (compiler or `BuildProjectileRenderResources`)
is responsible for calling `registry.Register` and then informing CombatRoot of the
renderId via a new `SetProjectileRenderId(typeId, renderId)` call, or `RegisterTemplate`
returns the typeId and the caller stamps both typeId + renderId.

**Revised approach — two-call pattern for the compiler path:**

```csharp
// 1. Mint behavior typeId (no render)
projDef.TypeId = combatRoot.RegisterTemplate(projDef.Prefab);

// 2. Register render resource directly into registry
projDef.RenderId = combatRoot.RenderRegistry.Register(
    projDef.Prefab.Sprite,
    new Vector2(scale, scale),
    projDef.Prefab.VisualRotationDegrees,
    projDef.Prefab.Material,
    CombatRenderResourceRegistry.ProjectileMeshName,
    combatRoot.Faction,
    combatRoot.Layer);

// 3. Bind renderId into CombatRoot's local map for the Spawn(Request) path
combatRoot.SetProjectileRenderId(projDef.TypeId, projDef.RenderId);
```

This requires `RegisterTemplate` to be made render-agnostic (only mints typeId) and
`CombatRoot.SetProjectileRenderId(int typeId, int renderId)` to be added as a
narrow setter.

**Alternatively** — keep `RegisterTemplate` doing both behavior+render registration
internally (calling `_renderRegistry.Register`), and the compiler just reads
`combatRoot.ProjectileRenderId(typeId)` after the fact. This is the simpler path: the
compiler doesn't call the registry directly; it calls `RegisterTemplate` which registers
into the registry internally and populates the local map. Then compiler reads back
the renderId.

**Decision for this task**: The user's stated direction is "compiler calls separate
register render method" and "compiler layer → register → ECS". The two-call + setter
pattern achieves this cleanly. However it requires making `RegisterTemplate` render-agnostic
which is a slightly larger change to CombatRoot than described in 002.

## Revised scope for 002 + 003 combined:

### 002 addition:
Make `RegisterTemplate` render-agnostic: remove the `projectileRenderIdByType` population
from within it. Add `internal void SetProjectileRenderId(int typeId, int renderId)` and
`internal void SetAoeRenderId(int typeId, int renderId)`.

Update `BuildProjectileRenderResources` (the Awake authored-config path) to call
`registry.Register` explicitly and then `SetProjectileRenderId`.

### 003:
Compiler calls:
1. `combatRoot.RegisterTemplate(prefab)` → typeId (behavior only)
2. `combatRoot.RenderRegistry.Register(...)` → renderId
3. `combatRoot.SetProjectileRenderId(typeId, renderId)`

AOE equivalent:
1. `combatRoot.RegisterType(definition)` → typeId (behavior only)
2. `combatRoot.RenderRegistry.Register(...)` → renderId
3. `combatRoot.SetAoeRenderId(typeId, renderId)`

## Acceptance Criteria
- No call to `combatRoot.RegisterRenderResource`, `combatRoot.ProjectileRenderId`,
  `combatRoot.AoeRenderId` remains in `PlayerSkillDriver`.
- Compiler stamps correct `RenderId` on runtime defs.
- No double-registration of GPU resources (each sprite → exactly one `Register` call).
- Mob path (`MobProjectileAttack`, `MobRoot`) unaffected — they call `RegisterTemplate`/
  `RegisterConfig` which now internally call `registry.Register` in the Awake authored path
  (unchanged for Mob because Mob doesn't go through the compiler).

## Scope
Small. ~10 lines changed in PlayerSkillDriver; ~10 lines added to CombatRoot in step 002.
