# 001 - Author And Compile Trigger Energy

## Goal

Add one clearly named positive trigger-energy value to every Skill asset and
every independently compiled runtime node.

## Changes

1. `Assets/Scripts/Skills/Skill.cs`:
   - Add private serialized `triggerEnergy = 1f`.
   - Expose read-only `TriggerEnergy`.
   - Document dual base role: outgoing EnergyPerHit and incoming
     EnergyRequired.
2. `Assets/Scripts/Skills/Runtime/RuntimeSkillDefinition.cs`:
   - Add plain `float TriggerEnergy`.
   - Add no Skill/ScriptableObject reference.
3. `Assets/Scripts/Skills/SkillSetCompiler.cs`:
   - Copy clamped `set.Skill.TriggerEnergy` immediately after BuildRuntime and
     before compiling outgoing edge.
   - Use same path for projectile, AOE, lingering AOE, and targeted skills.
   - Do not feed TriggerEnergy through StatModifierAccumulator.
4. EditMode tests:
   - Fractional authored value reaches runtime.
   - Same Skill asset in several nodes creates distinct runtime objects with
     same TriggerEnergy.
   - Non-positive input resolves to shared positive floor.

## Acceptance Criteria

- Every runtime skill has finite positive TriggerEnergy.
- No generic `Energy` property is added; name stays distinct from interval
  energy system.
- Compiler never reads authoring after snapshot creation.
- Interval energy behavior remains unchanged.

## Dependencies

None.

## Scope / Complexity

Small.
