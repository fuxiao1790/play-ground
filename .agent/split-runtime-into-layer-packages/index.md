# Split Runtime Into Layer Packages

## Summary

Today the runtime is **two** assemblies: `PlayGround.SkillUi` (UI, a package) and
`PlayGround.Runtime` (everything else — game logic, ECS simulation, the managed
combat bridge, presentation, and shared `Common`, all fused in `Assets/Scripts/`).
There is no compiler-enforced boundary between game logic and ECS simulation.

Target: **four assemblies, all as `.asmdef`s under `Assets/Scripts/`** (no embedded
packages) — a strict one-way chain plus an exempt debug leaf.

```
UI  ──►  Game Logic  ──►  ECS Simulation        Debugging ──► (any layer)
```

| Assembly | asmdef location | was |
|---|---|---|
| `PlayGround.Sim`       | `Assets/Scripts/System/PlayGround.Sim.asmdef` (new) | part of Runtime |
| `PlayGround.GameLogic` | `Assets/Scripts/PlayGround.GameLogic.asmdef` (rename of Runtime) | `PlayGround.Runtime` |
| `PlayGround.SkillUi`   | `Assets/Scripts/SkillUi/PlayGround.SkillUi.asmdef` (relocated) | `Packages/com.playground.skill-ui/` |
| `PlayGround.Debugging` | `Assets/Scripts/Debugging/PlayGround.Debugging.asmdef` (new) | part of Runtime |

**Location decision (2026-07-24, user):** assemblies live under `Assets/Scripts/`,
not `Packages/`. The existing `com.playground.skill-ui` package moves back into
`Assets/Scripts/SkillUi/` and is removed from `Packages/manifest.json`. Because the
sim and game-logic code already lives in `Assets/Scripts/`, this is mostly *adding
asmdef files in place* — almost nothing physically moves.

Layer ownership follows the user's rule: **managed objects that exist purely to
serve the ECS — combat root, VFX root, ECS sprite renderer, target registry,
presentation bridge — belong to the simulation package**, even though they are
MonoBehaviours. Authoring ScriptableObjects and player-facing gameplay meaning
belong to game logic.

## Key finding: the whole cross-layer tangle is one misfiled file

`AoeConfig` is a `[CreateAssetMenu]` authoring ScriptableObject that currently
lives in the sim folder (`Assets/Scripts/System/Aoes/`, namespace
`PlayGround.System.Combat.Aoes`). It is the sole cause of **both** bad edges:

- It reaches **up** into `PlayGround.Skills` (`BasicAoePrefab`).
- `Common/StatusEffects/StackingTriggerDef` reaches **into the sim** only to hold
  an `AoeConfig` field.

Relocating `AoeConfig` to game-logic authoring makes both edges legal in one move
(game-logic→game-logic, and game-logic→sim). No new types, no shim.

The remaining sim→game-logic references are dead code:
- `CombatRoot.cs` `using PlayGround.Skills;` — unused (`grep` shows no Skills type used).
- `CombatRoot.RegisterConfig(AoeConfig)` + `configTypeIds` — **no callers** anywhere
  in the repo. The live registration path is game-logic-side:
  `SkillDriver.cs:843-847` → `CombatRoot.RegisterType(AoeTypeDefinition)`.

## Constraints & invariants the change must respect

- **One-way dependency** (`Docs/architecture/layer-rules.md`): a layer may not
  reach around a contract. After the split, `PlayGround.Sim` must reference **no**
  game-logic/UI assembly; `PlayGround.GameLogic` references only `PlayGround.Sim`;
  `PlayGround.SkillUi` references only `PlayGround.GameLogic` (+ sim iff it names
  sim types).
- **Sim purity** (`Docs/layers/ecs-simulation.md`): jobs/systems read only
  unmanaged ECS/native data. This change does not touch runtime data shapes; it
  only moves files and deletes dead code, so simulation behavior is unchanged.
- **Asset-reference stability** (Unity): scenes, prefabs and `.asset` instances
  reference scripts by **MonoScript GUID**, which is independent of assembly and
  folder. Moving a `.cs` together with its `.cs.meta` preserves the GUID, so no
  scene/prefab/SO reference breaks. Every move MUST carry the `.meta`.
- **Namespaces are independent of assemblies** (C#): we do **not** need to rename
  `PlayGround.System.Combat.*` or `PlayGround.Common` to move files between
  assemblies. Minimal churn — namespace renames are optional cosmetic follow-ups,
  out of scope here except for `AoeConfig` (see task 002).
- **Debugging is an exempt leaf** (user decision): the `Debugging/` overlays get
  their own assembly that may reference any layer; the one-way rule does not apply
  to it. But the *core* must not depend on it — the current sim→`PerformanceText`
  push is a cycle that task 008 removes before Debugging is split out.

## Mechanisms reused vs. introduced

- **Reused:** the existing clean registration contract
  `CombatRoot.RegisterType(AoeTypeDefinition)` and `AoeConfig.CreateTypeDefinition()`.
  Reused: Unity's asmdef assembly-boundary mechanism (already used by the Editor and
  test assemblies) — no package manifests.
- **Introduced:** three new `.asmdef` files (Sim, Debugging, and the relocated
  SkillUi asmdef) + the Runtime→GameLogic rename. No new runtime types, no
  adapter/shim, no duplicated data path.

## Minimal/additive vs. refactor comparison

- **Additive** (keep `AoeConfig` in sim; break the cycle with a new plain
  sim-side AOE-authoring interface that `StackingTriggerDef` targets, plus keep
  `RegisterConfig`):
  - resulting data flow: unchanged, but a second AOE-config abstraction exists.
  - new concepts/types: 1 interface + indirection; dead `RegisterConfig` retained.
  - copies/translations added: authoring↔interface shim.
  - long-term cost: two homes for "AOE authoring", unclear ownership, the cycle is
    only hidden behind an interface in the wrong assembly.
- **Refactor** (relocate `AoeConfig` to its correct layer; delete dead code):
  - resulting data flow: identical at runtime.
  - existing types changed/removed: `AoeConfig` moves assembly; `RegisterConfig`
    + `configTypeIds` deleted; one dead `using` deleted.
  - copies/translations removed: none needed; the cycle disappears structurally.
  - long-term benefit: single source of truth for AOE authoring in game logic;
    sim has zero upward references.
- **Decision: refactor.** The target ownership is unambiguous (`AoeConfig` is
  authoring), and it removes the only cycle with no new concepts.

## Design validation against invariants

- One-way dependency: after tasks 001–002, `grep` for
  `using PlayGround.(Skills|Player|Mob|Spawn|Persistence|Audio|Game|Level|CameraSystem)`
  under the sim file set returns empty → the sim assembly can compile with no
  game-logic reference. ✔ (verified as the acceptance gate of task 001/007)
- Sim purity: no system/job code changes; only file location + dead-code removal. ✔
- Asset stability: all moves preserve `.meta` GUIDs. ✔ (acceptance gate of 004/005)
- `Common` split is clean: sim uses only `DamageSnapshot`, `GameplayTags`,
  `GameplayLayers` from `Common`; it uses **none** of `Common.Stats` /
  `Common.StatusEffects` / `PersistentScriptableObject` (verified by grep). ✔

## Layer assignment

**Sim assembly** (`PlayGround.Sim` — asmdef at `Assets/Scripts/System/`)
- `Assets/Scripts/System/**` (all ECS systems/components + serve-ECS managed:
  CombatRoot, CombatScopeOwner, CombatVfxRoot/dispatcher, batched renderer +
  render components, target registry/proxy, presentation bridge, platform world)
  — **except** `AoeConfig.cs`.
- Shared primitives moved **under the sim asmdef folder**
  (`Assets/Scripts/System/Shared/`): `DamageSnapshot.cs`, `GameplayTags.cs`,
  `GameplayLayers.cs`. They must live under the Sim asmdef — if left in
  `Assets/Scripts/Common/` they'd fall under GameLogic and invert the sim→Common use.
- References: Unity ECS packages only; **no** `PlayGround.*`.

**Game-logic assembly** (`PlayGround.GameLogic` — root asmdef at `Assets/Scripts/`,
rename of `PlayGround.Runtime`; auto-covers every subfolder not carved out by a
nested asmdef)
- `Skills/`, `Mob/`, `Spawn/`, `Player/`, `Camera/`, `Audio/`, `Level/`, `Game/`,
  `Persistence/`.
- `Common/Stats/`, `Common/StatusEffects/`, `Common/PersistentScriptableObject.cs`.
- `AoeConfig.cs` (relocated authoring, e.g. `Assets/Scripts/Skills/Authoring/`).
- References: `PlayGround.Sim` (+ Unity packages it names).

**UI assembly** (`PlayGround.SkillUi` — asmdef relocated to `Assets/Scripts/SkillUi/`)
- `SkillLoadoutUi`, `PlayerSaveController`, `SkillUiCatalog` (moved out of
  `Packages/com.playground.skill-ui/`).
- References: `PlayGround.GameLogic` (+ `Unity.InputSystem`).

**Debugging assembly** (`PlayGround.Debugging` — asmdef at `Assets/Scripts/Debugging/`, exempt leaf)
- `Debugging/` (`PerformanceText`, `DebugOverlay`, `VfxGraphSpawnTester`).
- Exempt from the one-way rule: may reference sim + game-logic + UI freely. The only
  constraint — **no core assembly references it** — already holds (task 008 removed
  the sim's `PerformanceText` push).

## Task list

- [001](001-sever-sim-to-gamelogic-edges.md) — Delete dead sim→game-logic code in CombatRoot.
- [002](002-relocate-aoeconfig-authoring.md) — Move `AoeConfig` to game-logic authoring.
- [003](003-split-common.md) — Split `Common` into shared primitives vs game-logic.
- [004](004-create-sim-package.md) — Add `PlayGround.Sim.asmdef` at `System/`; move the 3 shared `Common` files under it.
- [005](005-create-gamelogic-package.md) — Rename `PlayGround.Runtime.asmdef` → `PlayGround.GameLogic`; set its reference to Sim.
- [006](006-retire-runtime-repoint-asmdefs.md) — Relocate SkillUi into `Assets/Scripts/`; repoint Editor/Test asmdefs; drop the package.
- [007](007-verify-and-document.md) — Verify compile/tests; add guardrails; update docs.
- [008](008-invert-stats-display-coupling.md) — Break the sim↔Debugging cycle (pull, not push). **DONE in code (pending Unity verify).**
- [009](009-create-debugging-package.md) — Add `PlayGround.Debugging.asmdef` at `Debugging/` (exempt leaf). Pending.

Execution order (by dependency, not file number):
- Code fixes first, still inside the single assembly: **001 → 002 → 003** (008 done).
- Assembly boundaries (mostly adding asmdefs in place): **004** (Sim) → **005**
  (Runtime→GameLogic) → **009** (Debugging) → **006** (relocate SkillUi + repoint
  Editor/Test asmdefs, drop the package).
- **007** verifies (Unity compile + tests) and updates docs last.

> **Update (2026-07-24, user):** task **008** is now implemented in code — the
> overlay pulls `CombatStatsSingleton` from the ECS world and the sim no longer
> references `PerformanceText`, so the split is **no longer gated** on it. Task
> **009** (create the Debugging leaf package) remains a later step, not yet
> requested. The pre-split code fixes **001–003** and the physical moves **004–006**
> can proceed when you choose.

## Open questions / decisions taken (adjustable)

- **Location: resolved (2026-07-24, user)** — all assemblies are asmdefs under
  `Assets/Scripts/`, no embedded packages; SkillUi moves back in. Assembly names
  `PlayGround.Sim` / `PlayGround.GameLogic` / `PlayGround.Debugging` are proposals.
- **`Debugging/`**: resolved — its own exempt leaf asmdef; no core assembly
  references it (task 008 removed the last sim→Debugging edge). See tasks 008–009.
- **Namespace alignment**: kept as-is to minimize churn; assemblies don't require
  matching namespaces. A later cosmetic pass could align them.
