# Mob Skills — Simple First Implementation

## Summary

Give mobs offense by reusing the **exact same skill machinery the player uses**. A mob
becomes "a `SkillDriver` with `faction = Mob` + an aim source pointing at an enemy
target." `MobRoot` drives `skillDriver.Tick(...)` each frame (mirroring
`PlayerRoot.Update`), acquiring any valid enemy-faction target and firing at it.

Chosen behavior (confirmed with user, all "simplest"):
- **Target**: any valid enemy-faction target, cheapest logic (first-match, no distance).
- **Fire gating**: fire whenever a valid target exists; the skill cooldown alone gates rate.
- **Movement**: unchanged — pure wander.

**Loadouts are owner-agnostic.** A `SkillLoadout` carries no player/mob identity; the
equipping unit's `SkillDriver.faction` (owner-side) stamps the faction at spawn. So **any
unit can equip any loadout** — a mob can run a "player" loadout and vice versa. Mobs
therefore equip an **existing** loadout; **no new skill/set/loadout assets are authored**.

Scope is one code file (`MobRoot.cs`) plus prefab wiring that points a mob's `SkillDriver`
at an existing loadout asset. **No engine/ECS changes and no new content** — the spawn path
is already faction-parameterized and the loadout layer is already owner-neutral.

## Constraints & Invariants (with sources)

1. **`CombatRoot` is unified; faction is a per-spawn argument, not a property of the root.**
   Source: `Assets/Scripts/System/Core/CombatRoot.cs` header (lines 26–33) and every
   `Spawn*` overload taking `CombatFaction`. → Reuse the single root already in the scene;
   pass `CombatFaction.Mob`. Do **not** activate the unused `MobProjectileRoot` prefab.
2. **`SkillDriver` has no `Update()`; the owner calls
   `Tick(fireHeld, aimDir, aimWorldPos)` each frame.** Source: `SkillDriver.cs:56-79`;
   reference caller `PlayerRoot.cs:173-174`. → `MobRoot` drives `Tick`. Do not add a second
   driving mechanism.
3. **Friendly-fire gate is a single `self.Faction != target.Faction`; `TargetFaction` is
   the target's *own* allegiance.** Source: `Docs/contracts/target-proxy.md:26,67-71`;
   narrow-phase gates in `ProjectileCollisionSystem.cs:229`, `AoeCollisionCore.cs:76`. →
   `Mob`-faction projectiles hit `Player`-faction proxies (the player, future summons) and
   skip other mobs automatically.
4. **Both roots register into the same `CombatRoot.TargetRegistry`; the player is a
   `Player`-faction proxy.** Source: `GameRoot.cs:76,88`, `MobSpawnerRoot.cs:97,201`,
   `MobRoot.Register` (`MobRoot.cs:142`). → Acquire targets by scanning the registry the
   mob is already registered into. No new registration path.
5. **The ECS target spatial hash (`TargetSpatialHashSingleton.TrackingCells`) is
   job-handle-guarded and not safely readable from the main thread.** Source:
   `TargetSpatialHashSystem.cs`, `ProjectileTrackingSystem.cs:30,43-46`. → Main-thread mob
   acquisition uses the managed registry list, **not** the hash. (Perf note below.)
6. **`SkillDriver.CompileAndRegister` runs in `Start`; `BindCombatRoot` re-registers and is
   idempotent (`if (combatRoot == root) return;`).** Source: `SkillDriver.cs:51-53,81-88`.
   `MobSpawnerRoot.RegisterMob` calls `BindCombatRoot` right after `Instantiate` (before
   `Start`). → Binding order is safe either way; also `SkillDriver.Awake` resolves the root
   via the `PlayerProjectileRoot` tag as a fallback, so scene-placed mobs work too.
7. **Mob offense stats live on the shared `UnitStatSheet`.** Source:
   `SkillStatAggregator.Aggregate(loadout, statSheet)` used by `SkillDriver.cs:103`;
   `UnitStatSheet` offense fields currently unused by mobs. → Assign `MobStatSheet.asset`
   to the mob's `SkillDriver` so those fields finally drive mob skills.
8. **Inspector-over-Awake wiring is preferred for edit-time-known values.** Source: user
   feedback memory. → `SkillDriver` is added to mob prefabs and wired in the Inspector.
9. **The loadout layer is owner-agnostic; faction is owner-side, not loadout-side.**
   `SkillLoadout`/`SkillSet`/`Skill` carry no player/mob identity (skill-system doc,
   "Authoring" table); faction is the `SkillDriver.faction` field stamped per-spawn
   (`SkillDriver.cs:30,69-75`). → **Requirement: both mobs and players can equip any
   loadout.** Nothing in the plan may add owner/faction typing to loadout assets; the same
   asset must be equippable on a `PlayerRoot` and a `MobRoot` unchanged, with each unit's
   own `SkillDriver.faction` deciding the spawn faction. This is already true in code — the
   plan only preserves it and verifies it.

## Mechanisms Reused vs. Introduced

- **Reused (no divergence):** `SkillDriver` (driving, faction, compile/register),
  `SkillSpawnTranslator`, `CombatRoot` spawn path, `CombatTargetRegistry`, target proxies,
  the faction collision gate, `UnitStatSheet`. The player's casting flow is copied verbatim
  onto the mob owner.
- **Introduced:** only `MobRoot`'s per-frame call into `Tick` + a first-match
  enemy-target helper. No new runtime type for any existing concept.

## Minimal/additive vs. Refactor Comparison

- **Additive (chosen) approach:**
  - resulting data flow: `MobRoot.Update → SkillDriver.Tick → SkillSpawnTranslator →
    CombatRoot.Spawn*(…, CombatFaction.Mob)` — the *same* single path the player uses.
  - new concepts/types introduced: none (a helper method + a serialized `SkillDriver`
    reference; `SkillDriver` already exists and is faction-generic).
  - copies/translations added: none.
  - long-term cost: none structural. The only smell is an O(targets) first-match scan per
    mob per frame (see perf note); trivially swappable later.
- **Refactor alternative (rejected):** extract a shared `SkillCaster`/FSM base for
  player+mob, or split player/mob into separate combat worlds via `MobProjectileRoot`.
  - changes/removes: would restructure `PlayerRoot`/`MobRoot` ownership and CombatRoot
    wiring for no current benefit.
  - removes copies/translations: none — there are none to remove; the path is already
    unified and faction-generic.
  - long-term benefit: only if player/mob combat must diverge (separate worlds, distinct
    AI). Not required now.
- **Decision:** **choose additive.** Reason: the engine already exposes exactly one
  faction-generic data path; the additive change *conforms* to it and introduces zero
  parallel types/paths/shims. No structural warning triggers. The refactor would add
  abstraction without collapsing any existing duplication.

## Default Decision Rule Check

No two representations of the same concept are introduced. Faction stays a single per-spawn
value; targets stay in the single `CombatTargetRegistry`; skills stay in the single
`SkillDriver`/loadout model. Source of truth is unchanged.

## Design Validation (against invariants)

- (1)(3) Passing `CombatFaction.Mob` through the unchanged spawn path yields correct
  friendly-fire behavior with no new code — validated by the gate being a pure inequality.
- (2) `MobRoot` calling `Tick` every `Update` is the sanctioned driving pattern; ticking
  with `fireHeld=false` when no target keeps cooldowns advancing (`SkillDriver.cs:60-63`).
- (4)(5) Registry scan avoids the job-guarded hash; correctness relies only on
  `IsCombatTargetActive` + faction inequality, both plain managed reads.
- (6) Whether `BindCombatRoot` runs before or after `Start`, registration ends up bound to
  the shared root (idempotent re-register + tag fallback). Dead mobs `return` early in
  `Update` (`MobRoot.cs:83-87`), so no firing post-death.
- (7) `SkillStatAggregator` consuming `MobStatSheet` is the intended stat source.

## Task List

- **001** — `MobRoot` drives a `SkillDriver` (code). See `001-mobroot-drive-skilldriver.md`.
- **002** — Choose an EXISTING loadout to equip (no new authoring). See
  `002-author-mob-loadout.md`.
- **003** — Add + wire `SkillDriver` on mob prefabs. See `003-wire-mob-prefabs.md`.

Dependencies: 003 depends on 001 (component) and 002 (asset). 001 and 002 are independent.

## Open Questions / Considerations

- **Perf (deferred):** target acquisition is an O(targets) first-match scan per mob per
  frame. Enemy-faction targets are few, so this is fine for v1. If profiling flags it,
  cache the acquired target and re-acquire on an interval, or add a main-thread-safe read
  of `TargetSpatialHashSingleton.TrackingCells`. Not in scope.
- **Future AI:** the design-only `MobBehaviour` FSM
  (`Docs/reference/game-logic/mob-behaviour.md`) can later take over driving `Tick`
  (chase/range gating) from `MobRoot` without touching the skill path.
