---
name: vfx-faction-scope-removal-exploration
description: Exploration findings for removing faction routing and CombatScope dependency from the VFX system
---

# Exploration Findings

## Current Design

- VFX system overview: [Docs/reference/simulation/vfx-system.md](../../Docs/reference/simulation/vfx-system.md)
- Data flow: simulation jobs → `VfxPendingSpawn` → flush jobs → `DynamicBuffer<VfxSpawnRequestElement>` on `CombatScope` entity → `CombatVfxDispatchSystem` splits by faction → per-faction `CombatVfxRoot` → `CombatVfxDispatcher`

## Key Files & Components

- `VfxPendingSpawn` / `VfxSpawnRequestElement` ([VfxEcsComponents.cs:8-25](../../Assets/Scripts/System/Vfx/VfxEcsComponents.cs#L8)) — both carry `CombatFaction Faction`; needs to be dropped from both
- `VfxFlushJob` / `VfxStreamFlushJob` ([VfxFlushJob.cs:11-75](../../Assets/Scripts/System/Vfx/VfxFlushJob.cs#L11)) — `Entity Scope` + `BufferLookup<VfxSpawnRequestElement>`; faction guard `p.Faction == CombatFaction.None` drops factionless events
- `CombatVfxDispatchSystem` ([CombatVfxDispatchSystem.cs:1-56](../../Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs)) — queries singleton `VfxSpawnRequestElement` (on scope), splits buffer into `playerRequests` / `mobRequests`, routes each to `TryGetByFaction`
- `CombatVfxRoot` ([CombatVfxRoot.cs:1-69](../../Assets/Scripts/System/Vfx/CombatVfxRoot.cs)) — static `ByFaction[3]` registry; `BindFaction` + `TryGetByFaction`; wraps `CombatVfxDispatcher`
- `CombatEcsComponents.cs:92` ([CombatEcsComponents.cs:92](../../Assets/Scripts/System/Common/CombatEcsComponents.cs#L92)) — `AddBuffer<VfxSpawnRequestElement>(ownedScope)` — this is the coupling between VFX staging and the `CombatScope` entity

## Emitter Sites (all set `Faction = identity.Faction`)

- `CombatLifetimeSystem.cs:85,120` — projectile + AOE lifetime expire (trigger 2)
- `ProjectileCollisionSystem.cs:337,376` — hit (trigger 1) + collision despawn (trigger 2)
- `AoePulseVfxSystem.cs:69` — pulse (trigger 3)
- `AoeCollisionCore.cs:255` — AOE hit (trigger 1)
- `AoeSpawnExpansionSystem.cs:127` — spawn (trigger 0); writes directly to `EntityManager.GetBuffer<VfxSpawnRequestElement>(scopes[0])` — also directly couples to scope query

## Flush Job Call Sites (all pass `combatScope` as `Entity Scope`)

- `CombatLifetimeSystem.cs` — `VfxFlushJob`
- `AoePulseVfxSystem.cs:34` — `VfxFlushJob`; gets scope via `scopeQuery.GetSingletonEntity()`
- `ProjectileCollisionSystem.cs:130` — `VfxStreamFlushJob`; `SystemAPI.GetSingletonEntity<CombatScope>()`
- `ImpactAoeCollisionSystem.cs:124` — `VfxStreamFlushJob`
- `LingeringAoeCollisionSystem.cs:124` — `VfxStreamFlushJob`

## Faction Registration

- `PlayerSkillDriver.cs:42,76` — calls `vfxRoot.BindFaction(CombatFaction.Player)` on start and root-reassign
- `MobProjectileAttack.cs:57-59` — calls `vfxRoot.Register(typeId, trigger, asset)` but **never calls `BindFaction`**; mob faction is never registered in `ByFaction[]`
- Consequence: `TryGetByFaction(CombatFaction.Mob, ...)` always returns false today — all active VFX runs through the Player root only

## Proposed New Shape

**Faction removal:**
- Drop `Faction` field from `VfxPendingSpawn` and `VfxSpawnRequestElement`
- Drop faction guard in flush jobs; drop `Faction =` assignment at all 6 emitter sites
- `CombatVfxRoot`: replace `ByFaction[3]` + `BindFaction` + `TryGetByFaction` with `static CombatVfxRoot Instance` (set in `Awake`, cleared in `OnDestroy`)
- `CombatVfxDispatchSystem`: remove faction-split lists; call `CombatVfxRoot.Instance?.DrainAndDispatch(buffer.AsNativeArray())`
- `PlayerSkillDriver`: drop `BindFaction` calls

**Scope removal:**
- Introduce `VfxSingleton : IComponentData` tag (new file or add to `VfxEcsComponents.cs`)
- `CombatVfxDispatchSystem.OnCreate`: create a dedicated entity with `VfxSingleton` + `VfxSpawnRequestElement` buffer; remove `scopeQuery` targeting `VfxSpawnRequestElement`
- Remove `AddBuffer<VfxSpawnRequestElement>` from `CombatEcsComponents.cs` scope setup
- Flush jobs: rename `Entity Scope` → `Entity VfxScope`; all call sites pass `SystemAPI.GetSingletonEntity<VfxSingleton>()` instead of `CombatScope`
- `AoeSpawnExpansionSystem`: get VFX buffer from `VfxSingleton` entity instead of `scopes[0]`
- Tests (×5): remove VFX buffer from scope entity; create separate VFX singleton entity in test setup

## TypeId Collision Note

`CombatRoot.RegisterTemplate` mints typeIds per-root independently. Faction routing today prevents player typeId N and mob typeId N from colliding in one dispatcher. After removal, the second `Register` call with the same key is silently dropped (existing `ContainsKey` guard in `CombatVfxDispatcher.Register`). Acceptable for visual-only effects; aligns with the planned single-CombatRoot unification ([project_faction_overhaul.md](../../../memory/project_faction_overhaul.md)).

## Recommended Next Step

Documentation is sufficient — proceed to plan with the three-step breakdown: strip faction from data + emitters, introduce `VfxSingleton` entity, simplify `CombatVfxRoot` + dispatch.
