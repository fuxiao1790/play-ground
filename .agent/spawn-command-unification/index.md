# Spawn Command Unification

## Summary

Collapse every follow-up / triggered spawn — projectile impact AOE, projectile
impact projectile, AOE on-hit projectile burst, AOE on-hit AOE, stacking
detonation, and interval child — onto one uniform mechanism:

- a source entity carries `(spawn kind, Hash128 template key)` per follow-up slot;
- the **registry holds the spawn command template** (the resolved per-entity data
  plus volley params), keyed by `Hash128`, written only by external pre-tick code;
- the **runtime spawn event is a slim link** (`kind + key + per-instance frame`)
  with no spawn data;
- **expansion** dereferences the key against the frozen registry, stamps the
  per-instance frame, explodes template-level multiplicity, and emits commands
  through the existing apply / materialize path.

This deletes the bespoke embedded snapshot structs
(`ProjectileImpactAoeSnapshot`, `ProjectileImpactProjectileSnapshot`,
`AoeProjectileBurstSnapshot`, `AoeOnHitSpawnSnapshot`,
`AoeOnHitSpawnTailSnapshot`) and the value-type cycle they forced.

## Rationale

The current model embeds each follow-up's full data, by value, on the source. That
caused three defects:

1. **A value-type cycle.** `StackEffectSnapshot -> DetonationSnapshot ->
   AoeProjectileBurstSnapshot`. Adding a stack effect to the burst (so an
   AOE-spawned projectile can be a stacking applicator) closes the loop and the
   compiler rejects it.
2. **Role overloading / lossiness.** `AoeProjectileBurstSnapshot` serves both
   "stacking detonation nova" (must be terminal) and "AOE on-hit applicator burst"
   (wants to be a full applicator). It was flattened to break recursion, so it
   silently drops the burst projectile's own impact-AOE / impact-projectile / stack
   effect ([SkillSetCompiler.cs:83](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L83)
   already warns about this).
3. **Managed-reference dead end.** Carrying the detonation as a managed
   `RuntimeStackingDetonation` reference breaks Burst (CS8377) and the plain-data
   boundary.

A `Hash128` key into the registry cannot form a struct cycle, cannot be lossy, and
is plain data. The project already uses exactly this mechanism for interval child
spawns; the other follow-ups simply never adopted it.

## Constraints & invariants (with sources)

- **Registry is write-only-external and immutable during the tick.** All writes
  happen in managed `Update()` / compile, before the ECS tick; entries are never
  removed in v1. Therefore no rehash/realloc mid-tick and every job reads it
  `[ReadOnly]`. Source: [spawn-template-registry.md](../../Docs/reference/simulation/spawn-template-registry.md)
  §Registry Concurrency Contract.
- **Plain-data snapshot boundary.** No managed references in any struct that enters
  a `NativeQueue` / `NativeStream` / `NativeHashMap` / `DynamicBuffer` / chunk.
  Source: [adr-002](../../Docs/decisions/adr-002-plain-data-snapshot-boundary.md);
  enforced by code (CS8377 on `AoeSpawnEvent` / `AoeSpawnCommand` when a managed
  field was added).
- **Bounded nesting: 3 levels** (initial cast, first trigger, second trigger).
  Enforced at registration. Mirrors existing
  `AoeOnHitSpawnSnapshot.MaxStackChainLinks = 2`.
- **Events are intent, commands are allocation, expansion owns spawn math.** Source:
  [spawn-events-and-commands.md](../../Docs/contracts/spawn-events-and-commands.md).
- **`sizeof(AoeSpawnCommand) < 4096`** (native stream block limit). Source:
  spawn-template-registry.md testing checklist.
- **Deterministic per-instance ids/seeds/tick** must be preserved (matches current
  `TimedSpawnSystem` stamping).

## Mechanisms reused vs. introduced

**Reused (conform, do not reinvent):**
- The spawn-template registry already on `CombatScope`
  ([SpawnTemplateComponents.cs](../../Assets/Scripts/System/Common/SpawnTemplateComponents.cs):
  `ProjectileSpawnTemplate` / `AoeSpawnTemplate`, `NativeHashMap<Hash128, *SpawnEvent>`).
- `CombatRoot.RegisterTimedSpawnTemplate` content-hash dedup — generalized to all
  follow-ups.
- The existing expansion -> apply -> materialize path
  (`ProjectileSpawnExpansionSystem` / `ProjectileSpawnApplySystem`,
  `AoeSpawnExpansionSystem` / `AoeSpawnApplySystem`) — unchanged downstream of
  expansion.
- `TimedSpawnSystem`'s fetch-by-key -> copy -> stamp -> enqueue pattern, generalized.

**Introduced (justified):**
- No new event type. `ProjectileSpawnEvent` / `AoeSpawnEvent` are **refactored thin**
  (registry link + per-instance frame); the queues stop carrying fat events.
- The registry value moves from event-shaped to **command-shaped** so expansion is
  copy + stamp + explode with no event→command remap (no duplicate fat
  representation per spawn).
- A `(kind, Hash128)` on-hit-spawn slot on the source components, replacing four
  embedded snapshot structs.
- Key-based stacking detonation: `StackEffectSnapshot` carries
  `(detonationKind, detonationKey)` instead of an embedded `DetonationSnapshot`.

New mechanism is justified because the embedded snapshots are the direct cause of
the cycle, lossiness, and CS8377; the registry is the documented bound.

## Design validation (against each invariant)

- **Concurrency** — registration is compile-time / managed (pre-tick); collision,
  status, expansion, and timed-spawn only *read* the registry `[ReadOnly]`. Safe by
  the frozen-during-tick contract. ✓
- **Plain data** — the thin event and the command are blittable (kind byte,
  `Hash128`, floats, ids); no managed refs in any native container. Fixes CS8377. ✓
- **No duplicate fat representation** — registry template is command-shaped, so a
  spawn exists as exactly one fat layout (template == output); expansion stamps and
  explodes without remapping. ✓
- **Cycle** — follow-up is a `Hash128` key, not an embedded value → no struct cycle
  is expressible. ✓
- **Bound** — depth cap enforced at registration; templates fully enumerable at
  compile time → registry complete before the first tick. ✓
- **Command size** — the slim event shrinks queue payloads; the command shape is
  unchanged and stays < 4096. ✓

## Tasks

| # | Task | File |
|---|---|---|
| 001 | Registry: command-template storage + unified registration API + concurrency annotation | [001-registry-command-templates.md](001-registry-command-templates.md) |
| 002 | Refactor `*SpawnEvent` to a thin registry link + queue/scope-buffer plumbing | [002-slim-spawn-invocation.md](002-slim-spawn-invocation.md) |
| 003 | Expansion rewrite: dereference key + stamp instance frame + explode volley | [003-expansion-dereference.md](003-expansion-dereference.md) |
| 004 | Source follow-up slots become `(kind, key)`; drop embedded snapshots from components | [004-keyed-followup-slots.md](004-keyed-followup-slots.md) |
| 005 | Collision / status / timed emit `SpawnInvocation` | [005-emit-invocations.md](005-emit-invocations.md) |
| 006 | Compiler registration walk + translator builds invocations; enforce depth cap | [006-compiler-registration.md](006-compiler-registration.md) |
| 007 | Delete superseded snapshot structs + dead pipeline builders | [007-delete-snapshots.md](007-delete-snapshots.md) |
| 008 | Tests: cross-domain spawn, 3-deep stacking chain, frozen registry, stamping, dedup | [008-tests.md](008-tests.md) |
| 009 | Docs reconciliation (drop "being unified" caveats, describe as-built) | [009-docs.md](009-docs.md) |

Dependency order: 001 → 002 → 003; 004 in parallel with 001–003; 005 needs 003 + 004;
006 needs 001 + 004; 007 after 005 + 006; 008 after 007; 009 last.

## Open questions

1. **Registry value shape — RESOLVED to command-shaped.** The template is stored in
   command layout (not event layout) so expansion does not remap. Remaining
   micro-decision: extend `ProjectileSpawnCommand` / `AoeSpawnCommand` with the ~5
   volley fields (zero new types; command carries a few pre-explosion-only fields)
   vs. a dedicated command-template struct (clean field validity; +1 type). Lean:
   extend the command.
2. **One unified queue or two domain queues.** `SpawnInvocation` carries `kind`, so a
   single queue + expansion routing is possible. Lean: one invocation type and one
   submission path, routed to the existing two command/apply paths at expansion
   (keeps materialization untouched).
3. **Aim / pattern semantics.** Impact projectile aims back; AOE burst fans around
   impact; nova fans 360°; interval uses side-spray/radial. Lean: pattern is
   template-level; the invocation carries origin + one reference direction, and
   expansion applies the pattern from the template.
4. **Root cast as a template.** Registering the root cast too makes firing a pure
   invocation. Confirm there's no per-cast datum beyond origin/aim/seed that would
   force per-cast template variation (damage/count/etc. are compile-time → already
   baked).
