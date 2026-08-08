---
name: add-skill-loadout-compiler
description: Add the new pure SkillLoadoutCompiler class (additive; not yet called)
---

# 001 - Add SkillLoadoutCompiler

## Scope

Add `Assets/Scripts/Skills/SkillLoadoutCompiler.cs`, namespace `PlayGround.Skills`. Purely additive — nothing
calls it yet, `SkillDriver` is untouched by this task. Must compile standalone and hold no state.

## Source Material (what moves, verbatim behavior)

From [SkillDriver.cs](../../Assets/Scripts/Skills/SkillDriver.cs), move these (do not reimplement from scratch —
copy the logic, only changing what's needed to drop the `this`/instance context):

- Root-node detection loop: [SkillDriver.cs:206-222](../../Assets/Scripts/Skills/SkillDriver.cs#L206-L222).
- `HasTriggeredOnlyConversionSupport`: [SkillDriver.cs:554-570](../../Assets/Scripts/Skills/SkillDriver.cs#L554-L570)
  (already `static`, just changes owning class).
- Per-root compile loop calling `SkillSetCompiler.Compile`:
  [SkillDriver.cs:224-247](../../Assets/Scripts/Skills/SkillDriver.cs#L224-L247) — take only the parts that build
  `compiledSlots`/`rootNodeIndices`/warnings. **Do not** move the `SkillSlotState` creation/preservation
  (`FindPreservedState`, `state.SetRecoveryTime`, `firedCastTokens` sizing) — that stays in `SkillDriver` (task 002
  keeps it there).
- `AppendCompilerWarnings`: [SkillDriver.cs:267-317](../../Assets/Scripts/Skills/SkillDriver.cs#L267-L317)
  (already `static`, just changes owning class).
- The `SkillLoadoutValidator.Validate(runtimeLoadout)` call currently at
  [SkillDriver.cs:201](../../Assets/Scripts/Skills/SkillDriver.cs#L201) — becomes the seed list for
  `CompiledLoadout.Warnings`.

## Target Shape

```csharp
namespace PlayGround.Skills
{
    public static class SkillLoadoutCompiler
    {
        public static CompiledLoadout Compile(SkillLoadout loadout, SkillStatSnapshot snapshot)
        {
            var warnings = new List<SkillValidationWarning>(SkillLoadoutValidator.Validate(loadout));
            IReadOnlyList<SkillLoadoutNode> nodes = loadout.Nodes;

            var rootNodeIndices = new List<int>();
            // ... root detection loop (moved), using HasTriggeredOnlyConversionSupport ...

            int maxSlots = Mathf.Min(rootNodeIndices.Count, loadout.MaxRootSets);
            var roots = new RuntimeSkillDefinition[maxSlots];
            var resolvedRootNodeIndices = new int[maxSlots];
            int count = 0;
            for (int i = 0; i < maxSlots; i++)
            {
                RuntimeSkillDefinition def = SkillSetCompiler.Compile(nodes, rootNodeIndices[i], snapshot);
                if (def == null) continue;
                roots[count] = def;
                resolvedRootNodeIndices[count] = rootNodeIndices[i];
                count++;
                AppendCompilerWarnings(def, rootNodeIndices[i], warnings);
            }

            // trim roots/resolvedRootNodeIndices to `count` if compile skipped any slot
            return new CompiledLoadout(roots, resolvedRootNodeIndices, count, warnings);
        }

        private static bool HasTriggeredOnlyConversionSupport(SkillSet set) { /* moved verbatim */ }
        private static void AppendCompilerWarnings(RuntimeSkillDefinition def, int slotIndex,
            List<SkillValidationWarning> warnings) { /* moved verbatim */ }
    }

    public readonly struct CompiledLoadout
    {
        public readonly RuntimeSkillDefinition[] Roots;
        public readonly int[] RootNodeIndices;
        public readonly int Count;
        public readonly List<SkillValidationWarning> Warnings;

        public CompiledLoadout(RuntimeSkillDefinition[] roots, int[] rootNodeIndices, int count,
            List<SkillValidationWarning> warnings)
        {
            Roots = roots;
            RootNodeIndices = rootNodeIndices;
            Count = count;
            Warnings = warnings;
        }
    }
}
```

Note on `Count`: today's inline loop ([SkillDriver.cs:231-247](../../Assets/Scripts/Skills/SkillDriver.cs#L231-L247))
sizes `compiledSlots`/`slotStates`/`this.rootNodeIndices` at `maxSlots` but only fills up to `activeSlotCount`,
which can be less than `maxSlots` when a compile returns `null` for a slot. `CompiledLoadout` must preserve that
same "sized-but-partially-filled, tracked by count" shape so task 002 can reproduce `activeSlotCount` exactly —
don't silently compact the arrays in a way that changes which index maps to which node.

## Acceptance Criteria

- New file compiles in isolation (no `SkillDriver` reference).
- `SkillLoadoutCompiler` has zero fields, zero static mutable state, no `CombatRoot`/`CombatVfxRoot`/`SkillDriver`
  parameter or reference anywhere.
- `Compile(loadout, snapshot)` never mutates `loadout` or any `SkillLoadoutNode`/`SkillSet` reachable from it —
  read-only traversal only (this matches current behavior; the existing inline loop is already read-only over the
  loadout).
- Given the same `(loadout, snapshot)` inputs, two calls produce structurally equivalent `CompiledLoadout` values
  (same root count, same node indices, same warnings) — i.e. actually pure, no hidden dependency on prior calls.
- `SkillDriver.cs` is unmodified by this task (verified by diff scoped to the new file only).

## Dependencies

None — first task.

## Scope/Complexity

Small. Mechanical extraction of already-written, already-correct logic; the only new code is the `CompiledLoadout`
struct and the method signature gluing the moved pieces together.
