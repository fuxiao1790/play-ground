# Unit Stat Sheet

## Summary

Introduce one generic, authored `UnitStatSheet` ScriptableObject that both the player and mobs
reference for their combat stats, replacing today's duplicated per-root serialized fields
(`maxHealth`, movement speed) and filling the stubbed offensive-stat source in the skill system.

The sheet holds **only stats that have a live consumer today**:

- **Vitals:** `MaxHealth`
- **Movement:** `MoveSpeed`
- **Offense:** `IncreasedRatePercent`, `DamageMultiplier`, `CritChance`, `CritMultiplier`,
  `AreaSizeMultiplier` — i.e. exactly the existing `SkillStatSnapshot` surface.

No defensive stats (armor/resist/mitigation) are added — none exist in the pipeline yet.

This is the foundation for the two dependent `todo.md` items (mob skills, player summons): once
the caster's stats come from a shared sheet, a mob `SkillDriver` reads a mob sheet the same way
the player does.

## Rationale for major decisions

- **One authored type, not per-root fields (refactor toward one source of truth).** "A unit's base
  combat stats" is one domain concept currently smeared across serialized fields on both
  `PlayerRoot` and `MobRoot`. Collapsing them into `UnitStatSheet` removes duplicate ownership.
- **ScriptableObject-only authoring** (user decision). Matches the project ScriptableObject rule
  for reusable authored data (like `Skill`, `AoeConfig`, `MobSpawnPool`) and lets mob archetypes
  (Slime/Skeleton/Bat) each own a preset.
- **Fields map 1:1 onto `SkillStatSnapshot`**, so the compiler / accumulator fold are untouched —
  the sheet feeds the *existing* offensive path rather than creating a second one.
- **Health ownership is unchanged** (user decision): GameObject owns max health and seeds ECS at
  registration; ECS owns current health and pushes results back. The sheet adds no runtime authority.

## Data ownership (load-bearing — see 003/004 for enforcement)

One owner per datum; everyone else holds a read-copy or a mirror. The sheet is **immutable authored
base data**, never mutated at runtime (code-built mobs get their own `CreateInstance` copy, 004).

| Data | Sole owner | Readers / mirrors | Flow |
|---|---|---|---|
| Base stats (max health, move speed, offense) | **`UnitStatSheet` asset** | Player/Mob roots, `SkillDriver` | read-only at setup/compile |
| **Max** health | GameObject root (value from sheet) | ECS `TargetHealth.Max` | root → ECS, seeded once at proxy registration |
| **Current** health | **ECS** `TargetHealth.Current` | `PlayerHealth` / `MobRoot` (mirror) | ECS decrements → `ReceiveCombatTick` push-back |
| Move speed | GameObject root (value from sheet) | `PlayerMovement` / mob wander | copied into mover at construct |
| Offensive snapshot | `UnitStatSheet` → `SkillStatSnapshot` | `SkillSetCompiler` fold | rebaked on equip/stat change, **not** per-frame |
| Runtime HP / hurt / death behavior | `PlayerHealth` / `MobRoot` | — | unchanged; not folded into the sheet |
| Collision shape / `targetRadius` | GameObject root (hurtbox collider) | ECS target proxy | unchanged; not a combat stat |

**Rule of thumb (general, project-wide):** **ECS owns data that changes constantly tick-to-tick;
the GameObject owns data that is relatively static.** Current health/stacks mutate every combat tick
→ ECS. Max health, move speed, and offense are authored and static → GameObject (sourced from the
sheet), handed to ECS once at registration. The `UnitStatSheet` owns the authored numbers both start
from and introduces **no new runtime source of truth** — it only unifies the static authoring source.

## Constraints & invariants the change must respect

| Invariant | Source |
|---|---|
| Max health is seeded into `TargetHealth` **once** at proxy creation; ECS owns it thereafter. | `CombatTargetProxy.Create` + `TargetHealth` lifecycle comment (`System/Targets/CombatTargetProxy.cs`) |
| Current health flows ECS → GameObject via `ReceiveCombatTick`; the root only mirrors it. | `PlayerRoot.ReceiveCombatTick` / `MobRoot`, `ICombatTarget` default impl |
| `SkillStatSnapshot` is the offensive stat surface; it is folded via `StatModifierAccumulator` and rebaked only on equip/stat change, never per-frame. | `SkillSetCompiler.SnapshotModifiers.Contribute`, `SkillDriver.CompileAndRegister`; `Docs/reference/game-logic/skill-system.md` Layer 1.5 |
| ScriptableObjects hold authored data, not per-instance mutable state — unless they are intentionally runtime-created clones. | `Docs/coding-standards.md` ScriptableObject Rule |
| Required serialized references are validated once at setup (fail-fast); no hot-path null checks. | `Docs/coding-standards.md` Fail Fast Validation |
| Combat/setup paths are allocation-light; stat reads happen at setup/compile, not per-frame. | `Docs/coding-standards.md` Allocation Rule |
| All runtime code is one assembly (`PlayGround.Runtime.asmdef`) — no asmdef reference wiring needed. | `Assets/Scripts/PlayGround.Runtime.asmdef` |

## Mechanisms reused vs. introduced

**Reused (conform, don't reinvent):**
- `SkillStatAggregator.Aggregate` — the *designed* insertion point (its stub comment names exactly
  this: "until real stat sources … exist"). We change its signature, not the compile flow.
- `SkillStatSnapshot` + `StatModifierAccumulator` fold — unchanged; sheet fields map 1:1.
- `CombatTargetProxy` health seeding via `ICombatTarget.CombatMaxHealth` — unchanged path, new value source.
- ScriptableObject authored-data pattern — same as `Skill`, `AoeConfig`, `MobSpawnPool`.

**Introduced:**
- `UnitStatSheet` (ScriptableObject). Justified: it *removes* duplication (two ad-hoc field sets →
  one type) rather than adding a parallel path. Deliberately **not** named `CombatStats*` — that
  name is taken by the unrelated diagnostics `CombatStatsSingleton` in `System/Stats`.

## Design validation (against each invariant)

- **Seed-once health:** roots still expose `CombatMaxHealth`; only its backing value changes
  (field → `sheet.MaxHealth`). Seeding path in `CombatTargetProxy` untouched. ✅
- **ECS-owned current health:** `PlayerHealth`/`MobRoot` runtime HP and `ReceiveCombatTick` mirror
  are untouched; the sheet supplies only `MaxHealth`. ✅
- **Snapshot rebake cadence:** aggregator still called from `CompileAndRegister` (equip/stat change),
  not per-frame; reading the sheet there adds no per-frame cost. ✅
- **SO mutability:** authored assets are read-only. The one runtime mutation path
  (`MobRoot.ConfigureAuthoring` for code-built mobs/tests) uses `ScriptableObject.CreateInstance` —
  the explicitly-allowed "runtime-created clone" case. ✅
- **Fail-fast:** roots validate `statSheet != null` in `Awake`; `SkillDriver` tolerates null →
  `Identity` (a driver with no sheet is a valid no-augment caster). ✅
- **Allocation:** sheet reads at setup/compile only; no new per-frame allocation. ✅

## Minimal/additive vs. refactor comparison

**Minimal/additive** (add `UnitStatSheet`, but keep per-root serialized `maxHealth`/`speed`,
have the sheet only feed skills):
- Data flow: two authoring sources for the same stat (root fields *and* the sheet) that must stay in sync.
- New concepts/types: `UnitStatSheet` **plus** the surviving duplicate fields.
- Copies/translations added: ongoing risk of root-field vs sheet divergence.
- Long-term cost: two sources of truth for "unit base stats"; mobs and player still diverge.

**Refactor** (chosen — `UnitStatSheet` becomes the single authored source; remove the duplicated
serialized `maxHealth`/`speed` from both roots):
- Data flow: one authored source → roots read `MaxHealth`/`MoveSpeed`; skills read the offense fields.
- Existing types changed: `PlayerRoot`, `MobRoot` drop duplicated fields; `SkillStatAggregator`
  signature widened.
- Copies/translations removed: eliminates the per-root duplicate stat fields.
- Long-term benefit: one source of truth; player and mobs become symmetric, which is the
  precondition for the mob-skills and summons todos.

**Decision:** choose **refactor**. Reason: "unit base combat stats" is a single domain concept;
the additive path leaves two authoring sources for it (a structural warning: duplicate ownership),
while the refactor collapses to one source of truth with no compatibility reason to keep the duplicates.

Sub-decision — **offense mapping stays minimal (1:1 into `SkillStatSnapshot`)**: a refactor to an
increased-% damage model has no consumer today (no increased-damage support exists) and would add a
second representation, so the minimal mapping is correct here — it reuses the existing surface without
creating a parallel path.

## Default decision rule

Two representations of the same concept → refactor to one source of truth unless there is a concrete
compatibility/migration reason. Applied above: unit base stats collapse to `UnitStatSheet`.

## Task list

- **001-unit-stat-sheet-type.md** — create the `UnitStatSheet` ScriptableObject type.
- **002-wire-offensive-snapshot.md** — `SkillStatAggregator` reads the sheet; `SkillDriver` supplies it.
- **003-playerroot-consumes-sheet.md** — `PlayerRoot` reads vitals/movement from the sheet; drop duplicated fields.
- **004-mobroot-consumes-sheet.md** — `MobRoot` reads from the sheet; `ConfigureAuthoring` builds a runtime clone.
- **005-author-assets-and-wiring.md** — author the SO assets and assign them on prefabs (editor step).

Dependencies: 001 precedes all; 002 depends on 001; 003/004 depend on 001 (and pair with 002 for the
player); 005 depends on 001–004 and is manual editor work.

## Open questions / considerations

- Exact archetype numbers for mob sheets follow `Docs/reference/game-logic/mobs.md`
  (Slime hp35/spd2.5, Skeleton hp30, Bat hp20) — confirm at authoring time.
- The `loadout` parameter is kept on `Aggregate` for the future item/buff/level sources named in the
  skill-system doc; it is unused today.
- Harness cannot build/run Unity — verification is editor + PlayMode per the project convention.
