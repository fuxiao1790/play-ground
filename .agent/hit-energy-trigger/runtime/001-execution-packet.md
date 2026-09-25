# Task Execution Packet

## Task
001-author-skill-energy.md

## Goal
Add one positive `TriggerEnergy` value to every Skill authoring asset and every independently compiled runtime node.

## Files Allowed To Modify
- `Assets/Scripts/Skills/Skill.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeSkillDefinition.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- Directly affected EditMode test file(s)
- `.agent/hit-energy-trigger/implementation-log.md`

## Files Allowed To Create
- One focused EditMode compiler test file and its Unity meta file if needed.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- Existing Skill compiler EditMode tests and Skill/SkillSet test setup helpers.

## Behavior To Preserve
- Projectile, AOE, lingering AOE, and targeted compilation.
- Support/stat modifier behavior.
- Interval energy and mana behavior.
- Independent runtime object creation for repeated authored assets.

## Behavior To Change
- Author and expose Skill `TriggerEnergy`, default `1f`.
- Copy clamped finite positive energy into every runtime definition immediately after `BuildRuntime` and before outgoing-edge compilation.

## Relevant Global Context
- Skill owns base trigger energy for both outgoing contribution and incoming requirement roles.
- Use shared positive floor `1e-3f`; do not route through `StatModifierAccumulator`.
- Runtime definition stays plain managed runtime data with no authoring reference.
- This task does not replace legacy stack runtime yet.

## Dependencies Confirmed
- None required.
- Existing compiler builds a new runtime definition for each recursive node.

## Step-By-Step Instructions
1. Add serialized `triggerEnergy = 1f`, read-only `TriggerEnergy`, and dual-role documentation to `Skill`.
2. Add plain float `TriggerEnergy` to `RuntimeSkillDefinition`.
3. Copy finite positive clamped authored value after runtime construction and before outgoing-edge compilation, using all skill kinds through common path.
4. Add EditMode tests for fractional copy, repeated-asset independent runtime objects with equal energy, and non-positive values resolving to `1e-3f`.
5. Do not modify interval energy or stat-modifier logic.

## Acceptance Criteria
- Every compiled runtime skill has finite positive `TriggerEnergy`.
- No generic `Energy` property.
- Compiler snapshots authoring before recursive outgoing-edge work.
- Interval energy behavior unchanged.

## Validation Required
- Static search and code inspection.
- Do not run Unity tests. Name focused EditMode class/methods and require result XML at `Logs/TestResults-EditMode-HitEnergy.xml`.

## Hard Boundaries
- Do not modify files outside allowed list except imports/namespaces directly required by this task.
- Do not change architecture.
- Do not introduce abstractions not described by task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
