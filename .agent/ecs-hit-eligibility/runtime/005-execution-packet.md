# Task Execution Packet

## Task
005-document-contracts.md

## Goal
Make the expanded `TargetFaction` policy (hostile-only vs. allowed-faction),
the shared `TargetFaction.CanHit` eligibility rule, and the new
`CombatTickResult` semantics (`HitCount` including status-only hits,
`TickDeltaSeconds`) authoritative in `Docs/`. Fix passages that currently
assert or imply the old "collision/acquisition always use unconditional
same-faction inequality" invariant. This is a documentation-only task — do not
touch any code (all production/test code for tasks 001-004 is already
complete).

## Scope Discipline (read first)
Several of the reference docs below contain **pre-existing, unrelated
staleness** (e.g. references to a `DamageDispatchBridge`/`DamageReplayEvent`/
`DamageFinalizeSystem`/`CombatApplyFinalizeSystem` naming that predates the
current `CombatApplyBridge`/`CombatApplyFinalizeSingleSystem` names). **Do not
fix that** — it is out of scope for this task and not something tasks 001-004
touched. Only fix passages that this plan's actual changes (faction policy,
`CanHit`, `HitCount`, `TickDeltaSeconds`) made newly inaccurate or misleading.
If you find additional stale passages while reading, leave them alone unless
they specifically assert the old same-faction-only or old-HitCount-meaning
invariant.

## Files Allowed To Modify
- `Docs/contracts/target-proxy.md`
- `Docs/contracts/combat-hit-and-tick-results.md`
- `Docs/flows/collision-to-combat-result.md`
- `Docs/layers/ecs-simulation.md`
- `Docs/layers/presentation-and-feedback.md`
- `Docs/flows/runtime-frame.md`
- `Docs/reference/simulation/projectile-system.md`
- `Docs/reference/simulation/targeted-system.md`
- `Docs/reference/simulation/project-ecs-implementation.md`
- `Docs/reference/simulation/spawn-template-registry.md`
- `Docs/contracts/spawn-events-and-commands.md`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Verified Current Production Shapes (ground truth for this task — cite these,
do not re-derive them from docs, which may be stale)
- `TargetFaction` (`Assets/Scripts/System/Targets/CombatTargetProxy.cs`):
  `Value : CombatFaction` (own allegiance), `FilterMode :
  TargetFactionFilterMode` (`HostileOnly = 0`, `AllowedFactionOnly = 1`),
  `AllowedAttackerFaction : CombatFaction`. Static factories `Hostile(...)`/
  `AllowedFrom(...)`. Stamped once at proxy creation by
  `TargetProxyCreateApplySystem`; immutable for the proxy's lifetime in this
  scope (no runtime mutation path exists).
- `TargetFaction.CanHit(CombatFaction attacker, in TargetFaction target)`:
  rejects `attacker == CombatFaction.None` first; `HostileOnly` →
  `attacker != target.Value`; `AllowedFactionOnly` →
  `attacker == target.AllowedAttackerFaction`; unknown mode → rejects. This is
  the single shared predicate called (directly or via
  `CombatTargetAcquisition.TrySelectNthNearest`/`TryNearestEligible`) by:
  `ProjectileDiscreteCollisionSystem`, `ProjectileContinuousCollisionSystem`,
  `AoeCollisionCore` (impact + lingering AOE), `CombatTargetAcquisition`
  (targeted root/chain via `ExternalSpawnGateSystem`/`TargetedResolveSystem`,
  launch aim via `ProjectileSpawnExpansionSystem`), and
  `ProjectileTrackingSystem` (homing acquire + cached/mapped-index refresh).
- `CombatTargetAcquisition.TryNearestHostile` was renamed to
  `TryNearestEligible` (same behavior, same delegation to
  `TrySelectNthNearest` with rank 0). `ProjectileLaunchAimMode.NearestHostile`
  (the serialized authoring enum) was deliberately NOT renamed — it still
  means "nearest target eligible under the target's policy," just under its
  original authored name, to avoid a content migration.
- `CombatTickResult` (`Assets/Scripts/System/Application/CombatApplyResults.cs`):
  `TargetProxy`, `Health`, `DamageTaken` (direct-damage-only aggregate),
  `HitCount` (accepted `CombatHitEvent` count for the target in one finalizer
  update, including non-damaging/status-only hits), `CritCount`
  (direct-damage-only aggregate), `StatusStart`, `StatusCount`,
  `TickDeltaSeconds` (the finalizer's `SystemAPI.Time.DeltaTime` for the
  update that produced this result).
- `CombatApplyBridge.ReplayCombat` skips a result only when
  `result.HitCount <= 0 && result.StatusCount <= 0`; since `HitCount` now
  counts every accepted hit (not just direct-damage ones), a non-damaging/
  status-only accepted contact now also reaches
  `ICombatTarget.ReceiveCombatTick` (previously such a contact could produce
  `HitCount == 0` and be skipped unless it also changed status). One callback
  per changed target per finalizer update, never per-hit — unchanged.

## Exact Passages Verified Stale (fix these; use as anchors, re-locate by
content if line numbers drift)

1. `Docs/contracts/target-proxy.md` — Fields/Shape bullet:
   > `TargetFaction` — the target's **own** allegiance (`Player`, `Mob`, etc.);
   > collision and tracking skip same-faction candidates
   > (`self.Faction == target.Faction`)

   Replace with wording that: (a) keeps "own allegiance" for `Value`, (b)
   describes `FilterMode` (`HostileOnly`/`AllowedFactionOnly`) and
   `AllowedAttackerFaction`, (c) states collision/acquisition/tracking all
   evaluate the same `TargetFaction.CanHit(attacker, target)` rule rather than
   a raw inequality, (d) notes an `AllowedFactionOnly` target can accept an
   attacker of its own faction.

2. `Docs/contracts/target-proxy.md` — Notes/TODOs section:
   > `TargetFaction` is set once at proxy creation to the target's own
   > allegiance (not the firing faction). The friendly-fire gate uses a single
   > inequality test in the narrow phase rather than per-faction spatial-hash
   > buckets.

   Update to describe the two-mode policy and the shared `CanHit` predicate
   (still evaluated per-candidate rather than via per-faction hash buckets —
   that part of the sentence stays true and should be preserved). Add: policy
   is stamped at creation and immutable for the proxy's lifetime in this
   scope; selecting a faction chooses the whole faction, not an individual
   source actor.

3. `Docs/contracts/combat-hit-and-tick-results.md` — Fields/Shape:
   > - `CombatTickResult`
   > - hit count, crit count, aggregate damage, final health, and changed
   >   status ranges

   Replace the second bullet with explicit per-field semantics: `HitCount` =
   accepted-hit count including non-damaging/status-only hits; `CritCount`/
   `DamageTaken` = direct-damage-only aggregates; `TickDeltaSeconds` = the
   finalizer's `SystemAPI.Time.DeltaTime` for that update; `Health` = final
   health snapshot; `StatusStart`/`StatusCount` = changed status range.

4. `Docs/contracts/combat-hit-and-tick-results.md` — Guarantees section: add a
   statement that presentation callbacks fire only for targets whose result
   has hit or status content (`HitCount > 0 || StatusCount > 0`), not for
   every simulation tick or every target — and that because `HitCount` now
   counts every accepted hit, a non-damaging/status-only accepted contact also
   reaches the callback (previously it could be skipped). Keep the existing
   "one compact result per target, not one callback per raw hit" guarantee
   intact alongside this addition.

5. `Docs/flows/collision-to-combat-result.md` — Sequence step 2:
   > Collision systems qualify hits with broad phase, narrow phase, faction,
   > and repeat-hit gates.

   Reword "faction" to reflect that it's a target-policy eligibility check
   (`TargetFaction.CanHit`), not a raw same-faction test — e.g. "broad phase,
   narrow phase, target-policy eligibility, and repeat-hit gates."

6. `Docs/reference/simulation/projectile-system.md`:
   - "Target Proxy Bridge" section already says `TargetFaction` generically —
     no change needed there.
   - "Tracking And Movement" section:
     > `ProjectileTrackingSystem` builds target lookup data from
     > `TargetProxyTag`, `TargetPosition`, and `TargetFaction`, then runs a
     > single fused acquisition-and-steering job so both phases share one
     > query pass over `CombatKinematicsComponent`/`ProjectileTrackingComponent`
     > instead of two. It filters by projectile faction so a player-faction
     > projectile sees only targets registered to the player-faction combat
     > root.

     The last sentence describes same-faction filtering as if it were about
     which combat root a target is "registered to" — replace with: it
     evaluates `TargetFaction.CanHit(identity.Faction, targetFaction)` per
     candidate, so a hostile-default target still behaves as before, but an
     `AllowedFactionOnly` target can accept an attacker of its own registered
     faction.
   - "Collision And Consequences" section bullet:
     > read the shared `TargetSpatialHashSingleton` broad phase and skip cells
     > whose targets match the projectile's own `TargetFaction`

     This description is stale even structurally (it was never "match" a
     whole cell, but a per-candidate check) — reword to: "read the shared
     `TargetSpatialHashSingleton` broad phase and reject any candidate that
     fails `TargetFaction.CanHit` against the projectile's own faction."
   - "Launch Aim" section: replace "nearest hostile" wording with "nearest
     eligible target under target policy" in the prose (keep
     `LaunchAimMode == NearestHostile` and `ProjectileLaunchAimMode` as
     literal identifiers unchanged — only the enum's own doc-comment/prose
     description changes, never the member name). Specifically:
     - "one-time initial-velocity redirect toward the nearest hostile" →
       "...toward the nearest eligible target under target policy"
     - "the same nearest-hostile selection targeted chains use" → "the same
       nearest-eligible-target selection targeted chains use"
     - "no hostile in range" (in the fallback-conditions list) → "no eligible
       target in range"

7. `Docs/reference/simulation/targeted-system.md` — "Spawn And Resolve"
   section:
   > For a root cast, `ExternalSpawnGateSystem` acquires the nearest hostile
   > proxy to the aim point within `ChainDistance`

   → "acquires the nearest policy-eligible proxy to the aim point..."

   > The resolve system runs after target spatial-hash build and arming,
   > before hit finalize and spawn expansion. It searches hostile target
   > proxies only:

   → "...It searches policy-eligible target proxies only:" (the numbered list
   below this already says "nearest eligible target" in item 2 — leave that
   line as-is, it's already correct).

8. `Docs/contracts/spawn-events-and-commands.md` — launch-aim paragraph:
   > Expansion resolves nearest-hostile acquisition once per event/wave from
   > the dereferenced template

   → "Expansion resolves nearest-eligible-target acquisition (under target
   policy) once per event/wave from the dereferenced template"

## Behavior To Preserve
- Do not alter any code fence/identifier that names an actual type, field, or
  enum member (`TargetFaction`, `CombatFaction`, `ProjectileLaunchAimMode`,
  `NearestHostile`, `CombatTickResult`, etc.) — only prose describing their
  *behavior* changes.
- Do not remove or rewrite unrelated pre-existing content, TODOs, or legacy
  naming callouts (per Scope Discipline above).
- Keep each doc's existing structure/section headings; this is a wording pass,
  not a restructure.
- Preserve every doc's cross-reference links exactly (do not break markdown
  link paths).

## Behavior To Change
Apply the 8 passage fixes listed above. Additionally:
- `Docs/layers/ecs-simulation.md`: under "Owns", the existing bullet
  "`CombatHitEvent` production and ECS-owned `CombatTickResult` finalization"
  may get a short clarifying addition that finalization now stamps
  `TickDeltaSeconds` and an accepted-hit-inclusive `HitCount` — keep this
  light (one clause), do not restructure the bullet list.
- `Docs/layers/presentation-and-feedback.md`: under "Inputs", the existing
  bullet "`CombatTickResult` presentation data" may note it now includes
  `TickDeltaSeconds` for actors that need tick duration — one clause, no
  restructure.
- `Docs/flows/runtime-frame.md`: read fully for any wording that assumes
  unconditional hostile-only selection (the orchestrator's own read found
  none, but re-verify) — if none exists, make no change to this file and say
  so in your report rather than inventing an edit.
- `Docs/reference/simulation/project-ecs-implementation.md`: read fully for
  any same-faction-only or old-HitCount-meaning claims (the orchestrator's own
  read found none directly, but this file does describe
  `CombatApplyFinalizeSystem`/`DamageFinalizeSystem` under old naming — per
  Scope Discipline, leave that alone; only fix a passage here if it explicitly
  asserts the old eligibility or `HitCount` semantics). If none found, make no
  change and say so.
- `Docs/reference/simulation/spawn-template-registry.md`: read fully for the
  same reason — the orchestrator's read found no explicit same-faction claim
  here either. If none found, make no change and say so.

## Relevant Global Context
- No summon/firing-proxy/energy/skill-activation behavior may be described as
  implemented — that remains explicitly out of scope for the whole plan.
- Docs must not claim collision ever used *only* faction inequality going
  forward, and acquisition/collision docs must describe identical eligibility
  semantics (both now point at `TargetFaction.CanHit`).
- Filter mutation after proxy creation is not implemented — do not describe it
  as available; if you mention immutability, phrase it as "in this scope" per
  the plan's own open-questions section (future runtime mutation is an
  explicit non-goal here, not a permanent architectural limit).

## Dependencies Confirmed
- Tasks 001-004 complete and verified by the orchestrator (production code
  and tests both already reflect the shapes documented in "Verified Current
  Production Shapes" above).

## Step-By-Step Instructions
1. Fix the 8 exact passages listed above in their respective files.
2. Make the two light clarifying additions to `ecs-simulation.md` and
   `presentation-and-feedback.md`.
3. Read `runtime-frame.md`, `project-ecs-implementation.md`, and
   `spawn-template-registry.md` fully; fix only if you find an explicit
   same-faction-only or old-`HitCount`-meaning claim; otherwise leave
   unchanged and note that in your report.
4. Grep `Docs/` for any other occurrence of "same faction", "same-faction",
   "different faction" phrased as an unconditional eligibility rule, or a bare
   "hostile" description of selection/collision, that this packet's 8-item
   list may have missed. Fix only passages that assert the *old* unconditional
   invariant — leave generic uses of "faction" alone.

## Acceptance Criteria
(From `005-document-contracts.md` — authoritative; summarized)
- No current doc claims collision always uses only faction inequality.
- `TargetFaction` policy has one documented source of truth
  (`target-proxy.md`).
- Acquisition and collision docs describe identical eligibility semantics.
- Hit count and tick delta semantics match implementation and tests.
- Docs preserve the aggregated-result/no-managed-per-hit invariant.
- Out-of-scope GameObject behavior is not described as implemented.

## Validation Required
- Static/text-review only (agent does not run any tooling that validates
  Markdown or links): grep `Docs/` for "same faction", "same-faction",
  "different faction", "hostile" to confirm every eligibility-relevant match
  was reviewed and either fixed or judged out of scope.
- Confirm no code identifier was accidentally renamed/altered in prose (spot
  check `ProjectileLaunchAimMode.NearestHostile` still reads as the literal
  member name wherever it appears as code, not prose).
- Confirm every markdown link you touched still resolves to the same relative
  path as before (i.e. you only edited prose between links, not the links
  themselves).

## Hard Boundaries
- Do not modify any file outside the 11 explicitly listed.
- Do not modify any `.cs` file — this task is documentation-only.
- Do not perform a general cleanup of unrelated legacy naming (see Scope
  Discipline).
- Do not restructure document sections/headings.
- Do not describe summon/firing-proxy/energy/skill implementation as done.
- Do not reopen index-level decisions.
- Stop and report if a doc's actual current wording differs enough from what
  this packet quotes that the intended fix is ambiguous — quote what you
  actually found and explain the gap rather than guessing.
