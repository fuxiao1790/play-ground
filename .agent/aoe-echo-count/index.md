# AOE Echo Count

## Summary

Give AOE skills **echo** — the AOE analog of projectile count. An AOE skill with
echo repeats itself N times, scattering the copies across a **random disk**
(seeded) around the target point, the same way projectile count fans copies
across a spread angle.

**Echo replaces the AOE `count` concept.** Today `AoeSpawnCommand.Count` fans N
copies but writes them all at the same center (`AoeExpansionJob`, degenerate
stacking). That old logic is removed. `count` becomes `echoCount`, gains a
`scatterRadius`, and the expansion always fans + scatters. Zero scatter just
means the copies overlap — it is not a separate code path.

The spawn path is made to mirror projectiles exactly:

```
thin AoeSpawnEvent  ── (already carries no multiplicity, like ProjectileSpawnEvent)
      │
      ▼  AoeSpawnExpansionSystem  (dereference template, stamp per-instance fields)
      │  fan by command.EchoCount, offset each copy by scatterRadius (seeded)
      │  recompute BoundsMin/Max per copy
      ▼
   N AoeSpawnCommand ── AoeSpawnApplySystem (reuse / cold-create)
```

This is the same shape as `ProjectileSpawnEvent → ProjectileSpawnExpansionSystem
→ N ProjectileSpawnCommand`. The only difference is the per-copy transform: an
AOE offsets **position** (a 2D point); a projectile offsets **velocity
direction** (an angle), because an AOE is stationary.

Scatter model (chosen by user): **random disk, seeded** —
`angle = rand(0,2π)`, `dist = scatterRadius * sqrt(rand())`, seeded from
`command.JitterSeed` (identical mechanism to projectile jitter).

## Rationale

- The user directed that AOE spawn "follow the same spawn event → spawn command
  expansion" as projectiles. The AOE doc records the current asymmetry to fix:
  "AOE expansion is currently simple because AOE multiplicity is mostly resolved
  before the event reaches ECS" and, under Known Gaps, "future scatter/pattern
  work should live in `AoeSpawnExpansionSystem`." Echo makes the ECS expansion the
  authority for AOE multiplicity + scatter, matching projectiles.
- Echo "is intended to serve the same function as projectile count" (user), so it
  is the same domain concept — we refactor the existing `count`, not add a second
  field.

## Constraints & invariants the change must respect

| Invariant | Source | Impact on design |
|---|---|---|
| `AoeExpansionJob` is a plain `IJob` with no ECS component access, so job-safety cannot auto-chain it behind the `IJobEntity` VFX producers; the system feeds `vfx.ProducerHandle` in as an explicit input dependency and writes a single-threaded `NativeStream`. | `AoeSpawnExpansionSystem.cs:104-129` | Scatter math stays inside the existing `IJob`; do not change the job type, its scheduling, or the single-threaded `Stream.Write`. Add no new component reads. |
| Thin spawn event carries no multiplicity; the command template holds Count/spread and the expansion fans it (projectile contract). `AoeSpawnEvent` already has no count field. | `AoeSpawnPipeline.cs:9-20`, `ProjectileSpawnExpansionSystem.cs:163-217` | Keep `AoeSpawnEvent` thin. `EchoCount`/`ScatterRadius` live on `AoeSpawnCommand` (the template) and are read in expansion — the projectile-parallel shape. |
| Randomness must be deterministic per spawn (pooled reuse / replays). Projectile jitter uses `new Random(command.JitterSeed != 0 ? command.JitterSeed : 1u)`; `JitterSeed` is stamped onto the command from `evt.JitterSeed`. | `ProjectileSpawnExpansionSystem.cs:203`, `AoeSpawnExpansionSystem.cs:193-200` | Echo scatter reuses `command.JitterSeed` with the identical guarded seed. Id hashing (`AoeIdFor`) stays independent of the scatter RNG so ids remain deterministic whether or not scatter is active. |
| Broad-phase collision reads `BoundsMin/BoundsMax`; today they are computed once from `command.Position` and shared across all copies. | `AoeSpawnExpansionSystem.cs:156-186` | `ComputeWorldBounds` moves inside the loop, called per scattered copy, or collision uses wrong bounds (projectile path already computes bounds per shot in `WriteCommand`, `ProjectileSpawnExpansionSystem.cs:236`). |
| Templates are keyed by `xxHash3` over the whole command struct; per-instance fields are zeroed by `SpawnTemplateFor` before hashing. | `SpawnTemplateComponents.cs:29-34`, `CombatRoot.cs:513-533` | `EchoCount`/`ScatterRadius` are behavioral (not per-instance), so they stay in the struct and correctly vary the key. Do **not** zero them in `SpawnTemplateFor`. No hash code change. |
| `SpawnRegisteredAoe` already reserves `count` sequential ids (`nextAoeId += aoeCount - 1`). | `CombatRoot.cs:242-256` | Id reservation already echo-count-aware; keep it, pass `echoCount`. |
| Impact / on-hit / interval AOEs enqueue into the same `AoeSpawnExpansionSystem.EventQueue`. | `AoeCollisionCore.cs:237` | Echo applies uniformly to every AOE whose command template carries `EchoCount`/`ScatterRadius` — no per-trigger special-casing. |
| Behavior mods flow through `IAoeBehaviorModifier` / `AoeBehaviorContext`; supports only touch their own set. | `BehaviorContexts.cs:47-65`, `skill-system.md` (Set Isolation) | Echo is authored as `StatModifierSupport : IAoeBehaviorModifier`, mirroring `MultipleProjectilesSupport`. |
| Projectile `spreadDegrees`/`jitterDegrees` are raw-copied in `BuildRuntime`, not stat-folded. | `SkillSetCompiler.cs:219-220` | `ScatterRadius` is raw-copied too. `echoCount` is raw (like projectile `count`, `SkillSetCompiler.cs:218`). Avoids surprise interactions with `AreaSize`. |
| AOE is a high-count domain; expansion runs on the sim tick. | `project-overview.md`, `performance.md` | The fan loop already exists. Net new cost is one `ComputeWorldBounds` per copy (previously hoisted) + one seeded `Random` per command — same per-copy shape the projectile path already pays. No new job, no new allocation. |

## Mechanisms reused vs. introduced

**Reused (conform to existing):**
- The existing `AoeSpawnEvent → AoeSpawnExpansionSystem → AoeSpawnCommand`
  pipeline and its fan loop — enhanced, not replaced.
- `MultipleProjectilesSupport` shape → new `MultipleAoesSupport`.
- `AoeBehaviorContext` / `IAoeBehaviorModifier` path.
- `command.JitterSeed` seeded `Unity.Mathematics.Random` (same as projectile jitter).
- Content-hash template registry (auto-covers new fields).
- `SpawnRegisteredAoe` id reservation (already count-aware).

**Introduced (new):**
- `scatterRadius` authored field + its `RuntimeAoeDefinition` / `AoeSpawnCommand`
  carriers. Minimal mirror of projectile `spreadDegrees`; no AOE position-scatter
  geometry exists yet.
- `MultipleAoesSupport` asset type (one-for-one mirror of `MultipleProjectilesSupport`).

**Removed:**
- The old "N copies at the same center" behavior in `AoeExpansionJob`, and the
  `Count` name/semantics that implied plain stacking.

## Design validation

- **Same expansion shape as projectiles** — thin event in, template dereferenced,
  N commands fanned + transformed per copy, out to the apply system. ✅
- **Job safety** — pure arithmetic inside the existing `IJob`; no new component
  reads; VFX producer chaining untouched. ✅
- **Determinism** — RNG seeded from `command.JitterSeed` with the projectile
  guard; ids hashed independently. ✅
- **Bounds correctness** — `ComputeWorldBounds` per scattered copy. ✅
- **Registry** — behavioral fields stay hashed; distinct echo/scatter configs mint
  distinct keys automatically. ✅
- **Uniform coverage** — top-level, interval, and impact/on-hit AOEs all fan
  through the one expansion loop. ✅
- **No residual old path** — `count`→`echoCount` rename forces every call site to
  be revisited (compile errors surface them); audit task sweeps legacy
  `AoeConfig.count`. ✅

## Minimal/additive vs. refactor comparison

**Minimal/additive** — keep `count` (stack-at-center), add `echoCount` +
`scatterRadius` beside it:
- data flow: two multiplicity fields reach the command; expansion must reconcile
  which loops and how they compose.
- new concepts: a second "number of AOEs" concept.
- long-term cost: two source-of-truth fields; interval `spawnCount` (already
  additive with `count`) becomes ambiguous; drift risk; contradicts the user's
  "old count logic should be removed."

**Refactor (chosen)** — `count` → `echoCount`, add `scatterRadius`, delete the
same-center loop:
- data flow: one multiplicity concept, one fan loop that also scatters.
- concepts changed/removed: `count` semantics replaced by echo; degenerate
  stacking loop deleted.
- long-term benefit: single source of truth; exact structural mirror of
  projectiles; matches the user directive.

**Decision: refactor.** The user confirmed "echo is count, old count logic should
be removed in favor of echo."

## Default decision rule applied

Two representations of "number of AOE copies" describe one domain concept, so we
collapse to the single `echoCount`. No compatibility reason forces a second field:
existing `count`-authored assets migrate via `[FormerlySerializedAs("count")]` and
keep working (with `scatterRadius = 0` → copies overlap, i.e. today's positions).

## Task list

1. [001-command-and-expansion.md](001-command-and-expansion.md) — rename `AoeSpawnCommand.Count`→`EchoCount`, add `ScatterRadius`; rewrite `AoeExpansionJob` to fan + random-disk-scatter per copy with per-copy bounds and VFX position; delete the hoisted same-center bounds.
2. [002-authoring-runtime-compiler.md](002-authoring-runtime-compiler.md) — `AoeDefinitionBase.count`→`echoCount` (+`[FormerlySerializedAs]`) and add `scatterRadius`; `AoeBehaviorContext.EchoCount`/`ScatterRadius`; `RuntimeAoeDefinition.EchoCount`/`ScatterRadius`; wire `SkillSetCompiler.BuildRuntime` and the interval setup.
3. [003-echo-support-and-template.md](003-echo-support-and-template.md) — new `MultipleAoesSupport` (echoCount + scatterRadius, `Aoe` tag); populate `EchoCount`/`ScatterRadius` in `BuildAoeTemplate`; update `SkillSpawnTranslator` and `PlayerSkillDriver` registration call sites.
4. [004-count-removal-audit.md](004-count-removal-audit.md) — sweep every remaining AOE `count`/`Count` reference (incl. legacy `AoeConfig.count`, `SpawnCommandUnificationTests`) so no old stacking semantics survive.
5. [005-tests.md](005-tests.md) — sim tests: scatter determinism given seed, all copies within `scatterRadius`, per-copy bounds, `scatterRadius == 0` overlap, echo id uniqueness.
6. [006-docs.md](006-docs.md) — update `skill-system.md` (AoeDefinition fields, supports table, tags) and `aoe-system.md` (expansion now fans + scatters; remove "same center" note; close the Known Gap).

## Open questions / considerations

- **Primary copy centered?** Default: all copies scatter (i=0 included), matching
  "repeat itself in a scatter zone." One-line flip if playtest prefers a
  guaranteed on-target copy.
- **Scatter scale with `AreaSize` multiplier?** Default: no — raw like projectile
  spread/jitter.
- **Support/menu name.** Proposed `PlayGround/Skills/Supports/Multiple AOEs`
  (symmetry with "Multiple Projectiles"); serialized fields `echoCount` /
  `scatterRadius` to match the user's terminology. Confirm at review.
- **`AoeConfig.count`** (legacy AOE authoring asset, separate from the skill
  path): audit task decides whether it maps to `echoCount` or is unrelated
  preload/pool config.
