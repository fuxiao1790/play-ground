# Task Execution Packet

## Task
004-docs.md

## Goal
Document sound-event boundary, ownership, authoring chain, frame timing, culling/selection contract, listener invariant, and deferred Burst extension. Update five existing documentation indexes/maps so no maintained doc references removed audio paths/types.

## Files Allowed To Modify
- `Docs/layers/presentation-and-feedback.md`
- `Docs/coding-standards.md`
- `Docs/reference/game-logic/skill-system.md`
- `Docs/folder-structure.md`
- `Docs/project-overview.md`
- `.agent/skill-sound-events/implementation-log.md`

## Files Allowed To Create
- `Docs/contracts/sound-events.md`

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Docs/contracts/combat-hit-and-tick-results.md`
- `Docs/contracts/vfx-requests.md`
- `Docs/architecture/layer-rules.md`
- `Docs/layers/presentation-and-feedback.md`
- `Assets/Scripts/System/Audio/SoundEvent.cs`
- `Assets/Scripts/System/Audio/AudioRoot.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- Four authored prefab source files and runtime/compiler chain from task 003.

## Behavior To Preserve
- Documentation authority structure and existing layer/event separations.
- No sound entry added to canonical ECS combat-lane list because no lane exists.
- Historical unrelated docs unchanged.

## Behavior To Change
- New maintained contract explains current sound data/API/lifetime/ordering/extension.
- Presentation layer owns audio root/budget and lists pending input plus pooled output.
- Coding standards explicitly keep sound payload separate from damage/spawn/VFX.
- Skill docs point designers to prefab sound slots, not Skill assets, and replace removed `AudioManager` reference.
- Folder/project indexes include Sim audio folder and sound contract.

## Relevant Global Context
- `AudioRoot` lives in Sim because `Ui -> GameLogic -> Sim` is one-way and Sim is callable by current GameLogic producer plus future ECS presentation.
- One managed pending list is deliberate: no Burst producer exists. Future native lane drains into existing `AudioRoot.Enqueue` before `LateUpdate` without changing current path.
- `CombatApplyBridge` can produce future hit sounds on main thread.
- Payload admission: world occurrence facts belong in `SoundEvent`; response policy belongs on `AudioRoot`.
- Listener object must carry actual scene `AudioListener`; missing/destroyed listener means one-time error/silence and distance culls.
- Async spawn rejection may occur after cast sound; accepted behavior.

## Dependencies Confirmed
- Tasks 001-003 complete; settled source shape exists and can be documented.
- Maintained docs still contain one `AudioManager` reference in `skill-system.md`; no maintained `Assets/Scripts/Audio/` or `PlayGround.Audio` path found elsewhere.
- Planned historical cross-reference `.agent/burst-onupdate-work/index.md` does not exist in workspace. Do not create or link a broken path; make safety-contract reasoning self-contained in `Docs/contracts/sound-events.md` and record this local documentation deviation.

## Step-By-Step Instructions
1. Add `Docs/contracts/sound-events.md` using sibling contract section shape: Purpose, Produced By, Authored By, Consumed By, Fields/Shape, Guarantees, Spatial Model, Restrictions, Lifetime, Ordering, Known Behavior, Extension Point, and related links.
2. Document `SoundEvent`, `SoundCategory`, `SkillSoundIds`, current-vs-reserved fields, id/radius conventions, frame drain/clear, and payload admission rule.
3. Document authoring on four basic prefabs and prefab -> definition -> compiler -> runtime ids path; clips never live on Skill SO; projectile/VFX asymmetry and only spawn slot today.
4. Document listener co-location invariant, BindListener, one-time setup error/silence, squared-distance per-event cull.
5. Document no per-call play API, Update-or-earlier producer restriction, no gameplay reads, insertion order meaningless, async rejection behavior.
6. Document deferred native lane and why absent, including main-thread hit bridge option and existing unmanaged/additive extension shape. Keep reasoning self-contained because planned `.agent` source is absent.
7. Update presentation layer owns/inputs/outputs/modules/related contracts.
8. Update coding standards Combat Event Separation only; do not touch canonical lane list.
9. Update skill system authoring chain/design guidance and replace old manager reference.
10. Update folder structure with contract and `Assets/Scripts/System/Audio/` runtime map; remove any old folder mention if found.
11. Update project overview contract index.
12. Search all `Docs/` for removed `Assets/Scripts/Audio/`, `PlayGround.Audio`, and `AudioManager`; must be zero. Validate links/paths and diff.

## Acceptance Criteria
- New contract follows sibling shape and contains all required sections/invariants.
- Presentation and folder indexes mention current audio root/folder.
- Maintained docs contain no removed path/namespace/type references.
- Docs alone explain both Sim ownership and deliberate lack of ECS/native lane.
- No sound entry appears in coding-standard canonical combat-lane list.

## Validation Required
- Search all Docs for removed identifiers and all required new terms/links.
- Inspect exact five existing doc diffs plus new contract.
- `git diff --check`; ensure only allowed docs/log changed for task 004.
- No Unity tests needed for docs; do not run Unity tests or Unity runner.
- Record missing historical `.agent/burst-onupdate-work/index.md` cross-reference as deviation handled by self-contained maintained-doc explanation.

## Hard Boundaries
- Do not modify code, scenes, prefabs, Unity YAML, or docs beyond allowed list.
- Do not add a sound ECS/native lane to docs as current behavior.
- Do not change architecture or reopen plan decisions.
- Do not invent more authored sound slots/producers.
- Stop on architectural ambiguity.
