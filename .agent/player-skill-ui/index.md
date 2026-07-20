# Player Skill Bar, Runtime Editing, and Picker Sample

## Summary

Build documentation first, then one playable UI Toolkit vertical slice. V1 shows
three global skill positions, two links between adjacent skills, and three
support positions above each skill. Empty skill positions are valid. A skill
with no incoming trigger is a direct-cast root; a skill with an incoming trigger
is triggered-only and appears gray.

The final runtime has one loadout data path. `SkillLoadout` owns an ordered list
of normalized skill nodes. Each node references one reusable `SkillSet` and an
optional `triggerToNext`. `SkillDriver` clones the authored loadout and each
referenced skill set once for the player session; UI edits those runtime clones
through driver-owned commands. No parallel `SkillLoadoutSnapshot` or UI-owned
loadout model remains.

V1 limits live only in the UI policy. Runtime lists, compiler, validator, and
authoring editor remain unbounded.

## Target model and flow

```text
SkillLoadout asset template
  -> SkillDriver deep runtime clone (session source of truth)
  -> SkillLoadoutEditCommand candidate clone
  -> validate + compile + register
  -> atomic runtime-loadout/compiled-root swap at SkillDriver.Tick start
  -> LoadoutChanged/EditResolved events
  -> Skill bar refresh

SkillDriver root cooldown state
  -> three low-cost UI queries per frame
  -> cooldown overlays and edit eligibility
```

Each normalized node has `SkillSet skillSet` and `TriggerLink triggerToNext`.
Incoming trigger state is derived from the previous node. Multiple roots need no
extra representation: every equipped node without an incoming trigger is a
root. A null `skillSet` is an empty position. A trigger is valid only when both
neighbor nodes are equipped and tag-compatible.

## Constraints and invariants (source-checked)

- **Ownership:** skills, supports, triggers, loadouts, validation, cooldowns,
  and compilation belong to Game Logic (`Docs/layers/game-logic.md`). UI may
  issue commands and read presentation state, but must not own gameplay state.
- **Scene/UI ownership:** `UIDocument`, scene wiring, input routing, and authored
  presentation belong to Scene and Authoring (`Docs/layers/scene-and-authoring.md`).
- **Immutable authored definitions:** `Skill`, `SkillSupport`, `TriggerLink`,
  and authored `SkillSet` objects are templates. Runtime work must not mutate
  shared assets (`Docs/reference/game-logic/skill-system.md`; current fields in
  `Skill.cs`, `SkillSet.cs`, and `TriggerLink.cs`). Runtime uses deep-cloned
  `SkillLoadout`/`SkillSet` instances; definition assets remain shared/read-only.
- **One mutable equipment owner:** the skill-system reference says
  `SkillLoadout` is live mutable equipment state, while current code exposes
  read-only serialized slots. This change makes the runtime clone satisfy that
  contract rather than adding another loadout store.
- **Forward-only topology:** current compilation only accepts adjacency links
  and recursively advances to a later slot (`SkillSetCompiler.cs`). Normalized
  `triggerToNext` keeps this invariant and removes parsing ambiguity/cycles.
- **Compilation timing:** authored state compiles before spawn; runtime changes
  recompile only on accepted edits, never per frame (`Docs/flows/skill-to-combat-spawn.md`).
- **Simulation boundary:** ECS and in-flight combat entities read copied plain
  data, IDs, hashes, and template keys, never live ScriptableObjects
  (`Docs/contracts/skill-runtime-snapshots.md`, `Docs/architecture/layer-rules.md`).
  Therefore an edit affects future casts only; existing projectile/AOE/VFX work
  keeps old copied values.
- **Cooldown ownership:** `SkillDriver` owns `SkillSlotState` and ticks it in
  player `Update` (`SkillDriver.cs`, `PlayerRoot.cs`). UI does not tick or mutate
  cooldowns.
- **Validation:** invalid supports/triggers must be rejected before they reach
  compilation. Existing compatibility predicates in `SkillLoadoutValidator`
  remain the sole rule source; picker eligibility reuses them.
- **Spawn depth/resource registration:** accepted candidates still respect
  `CombatRoot.MaxSpawnChainDepth` and register projectile, AOE, VFX, and child
  templates before activation (`SkillDriver.cs`).
- **Main-thread lifecycle:** UI callbacks, runtime loadout mutation, compilation,
  and driver swap run on Unity's main thread. No job reads the mutable loadout.
- **Performance:** no per-frame loadout copies, validation, compilation, LINQ,
  or allocations. Candidate deep clone is allowed only on a player edit. UI polls
  at most three cooldown records per frame. Picker list allocation happens only
  on open/catalog change. High-count ECS paths stay untouched
  (`Docs/performance.md`, `Docs/reference/simulation/ecs-notes.md`).
- **No pause:** picker must not change `Time.timeScale`; CPU simulation and GPU
  VFX continue. While modal is open, player gameplay input is blocked and UI
  input remains active. Hovering/clicking the bar suppresses Attack so the click
  cannot cast.

## Mechanisms reused vs. introduced

### Reused

- `SkillLoadout` as equipment owner and `SkillSet` as skill-plus-support unit.
- `SkillSetCompiler`, `SkillLoadoutValidator`, and existing tag predicates.
- `SkillDriver` as compile/register/cooldown orchestration owner.
- Existing Input System `Player` and `UI` action maps.
- Existing ScriptableObject authoring, editor validation, EditMode tests, and
  PlayMode tests.
- UI Toolkit modules already present in Unity 6000.4.

### Introduced

- Normalized `SkillLoadoutNode` with `skillSet` and `triggerToNext`. This replaces
  three legacy topology types and chain parsing; it does not create a second path.
- `SkillLoadoutEditCommand`/`SkillLoadoutEditResult` as the cross-layer command
  contract. UI needs a mutation boundary and rejection reason; direct field writes
  would violate ownership.
- `SkillUiCatalog` as a presentation/save lookup: stable ID, typed definition,
  label, description, optional icon. It does not own equipment state. Runtime
  asset discovery cannot use editor-only `AssetDatabase`, so explicit catalog is
  required.
- UI Toolkit view/controller assets and a small UI policy with visible counts
  `3/2/3`. Policy never enters compiler/gameplay validation.
- Two input-block flags on `PlayerRoot`: modal gameplay block and pointer-over-UI
  attack block. `PlayerRoot` remains the sole gameplay input reader.

## Minimal/additive vs. refactor comparison

### Minimal/additive approach

- **Resulting data flow:** legacy flat `SkillLoadout` -> new runtime snapshot/store
  -> UI projection -> adapter back to legacy slots -> compiler.
- **New concepts/types introduced:** second loadout snapshot, mutable session
  store, slot adapters, and synchronization/revision logic beside existing types.
- **Copies/translations added:** template-to-snapshot conversion, snapshot-to-flat
  conversion on every compile, and duplicate validation paths.
- **Long-term cost:** two representations describe the same equipped build;
  authoring, save/load, UI, compiler, and tests can drift.

### Refactor approach

- **Resulting data flow:** authored `SkillLoadout` -> runtime clone of same type ->
  existing validator/compiler -> compiled runtime definitions.
- **Existing concepts changed/removed:** `SkillLoadout` becomes normalized nodes;
  `LoadoutSlot`, `SkillSetSlot`, `TriggerLinkSlot`, `TriggerChain`, and
  `ParseChains` are removed after asset migration.
- **Copies/translations removed/avoided:** no persistent snapshot/store and no
  UI-to-legacy adapter. Only necessary authoring-to-session clone and transient
  candidate clone remain.
- **Long-term benefit:** one loadout topology, one validator path, one compiler
  input, clearer root derivation, and direct future save mapping.

### Decision

Choose **refactor**. The additive option triggers the duplicate-representation,
translation-layer, and synchronization warnings. Staged legacy fields may exist
only while assets migrate; final code and assets must contain one path.

Default decision rule: if two representations or paths describe equipped skill
state, remove one unless a concrete serialized migration need requires it
temporarily.

## Edit and cooldown rules

- Commands can set/clear a skill, one support position, or one outgoing trigger.
- UI always shows all catalog choices. Invalid choices are disabled with the
  validator reason; invalid state never commits and conflicts are never
  auto-cleared.
- Clear Skill is disabled while that node has supports or either adjacent trigger
  depends on it. Player clears dependents first.
- Duplicate support selection is a UI-policy rejection. Core model still permits
  arbitrary support lists.
- Skill/support commands against a direct root reject while its cooldown is not
  ready. A triggered-only node has no active cast cooldown and remains editable.
- Trigger commands remain allowed during cooldown. A node newly made into a root
  starts at zero cooldown progress. An unchanged root preserves elapsed cooldown.
- An accepted direct-root skill/support edit resets that root to zero progress.
- One edit may be pending. At start of `SkillDriver.Tick`, driver clones current
  runtime loadout, applies command, rechecks cooldown and compatibility, compiles
  and registers into staging data, then swaps loadout/compiled roots/cooldowns
  together. Failure leaves old state active and emits a rejection.

## UI and sample acceptance

- Bottom-center bar shows three fixed skill buttons, nine support buttons, and
  two trigger buttons/arrows. Empty positions show `+`.
- Incoming trigger adds triggered-only/gray state. Direct roots show cooldown
  overlay. Triggered nodes remain clickable.
- Any position opens top-center modal picker. Picker supports pointer, keyboard
  focus/navigation, Escape/backdrop cancel, scroll, Clear/None, disabled reasons,
  and a pending state until edit resolution.
- Existing skill art may be used. Missing support/trigger icons use consistent
  colored initials; final art and animation polish are outside this delivery.
- Dedicated sample setup uses a valid three-node loadout. It must not bind the UI
  to `BenchmarkHybridLoadout` and silently hide that asset's additional nodes.
- Stable catalog IDs and a versioned save DTO are documented, but persistence is
  not implemented.

## Design validation

- **Single source:** final runtime mutates only cloned `SkillLoadout`/`SkillSet`
  instances; no second loadout model remains. Pass.
- **Authored safety:** every referenced `SkillSet` is cloned per slot, including
  repeated references, before edits. Shared assets never change. Pass.
- **Layer boundary:** UI sends commands; driver/game logic validates and owns
  commit. UI never calls compiler, combat root, or ECS. Pass.
- **Atomicity:** candidate compiles/registers before reference swap. Partial or
  invalid builds never become visible. Pass.
- **In-flight safety:** old entities retain copied snapshots/template keys; new
  definitions affect future casts. Pass.
- **No pause:** input is gated without touching global time; ECS/VFX keep running.
  Pass.
- **Unbounded core:** node/support collections have no 3/2/3 constants; only UI
  policy and view instantiate fixed visible controls. Pass.
- **Budget:** edit-time cloning/compilation is low-frequency; hot simulation paths
  and frame-time allocation behavior stay unchanged. Pass.

## Tasks

- [001-document-contracts.md](./001-document-contracts.md) - authoritative UX,
  ownership, command, data-flow, and sample docs.
- [002-normalize-loadout-and-migrate.md](./002-normalize-loadout-and-migrate.md) -
  normalized node model and serialized asset migration.
- [003-collapse-validator-compiler.md](./003-collapse-validator-compiler.md) - remove
  legacy topology/parser and use one validator/compiler path.
- [004-runtime-edit-transaction.md](./004-runtime-edit-transaction.md) - runtime
  clone ownership, atomic edit commands, cooldown mapping, and events.
- [005-ui-catalog.md](./005-ui-catalog.md) - stable IDs, picker metadata, editor
  validation, and sample catalog.
- [006-ui-toolkit-bar-picker.md](./006-ui-toolkit-bar-picker.md) - bar, picker,
  presenter/controller, and non-pausing input gate.
- [007-playable-sample.md](./007-playable-sample.md) - dedicated three-node sample
  scene/setup and usage guide.
- [008-tests-and-verification.md](./008-tests-and-verification.md) - migration,
  unit, PlayMode/UI, regression, and performance checks.

## Open questions and considerations

- Player root-skill count remains intentionally undecided. V1 derives roots from
  trigger topology and adds no independent cap beyond three visible positions.
- Full gamepad and touch acceptance, final art, save persistence, and production
  rollout remain later work.
- Migration is staged for serialized safety, but temporary legacy fields/types and
  migration command must be deleted before this feature is considered complete.

