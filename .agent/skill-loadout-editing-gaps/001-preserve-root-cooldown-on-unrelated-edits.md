# 001 — Preserve per-root cooldown state across unrelated loadout edits

## Goal

`SkillDriver.CompileAndRegister` currently rebuilds every root's `SkillSlotState`
from scratch (`new SkillSlotState()`) on every call, so editing node A's skill
silently resets node B's in-progress cooldown too. Fix so only the *edited* root
resets; every other root's `elapsedSinceLastFire` survives the recompile, per
[skill-loadout-editing.md:100-103](../../Docs/contracts/skill-loadout-editing.md).

## Constraint (read before changing the signature)

`CompileAndRegister` **must stay a genuine zero-parameter private method.**
`Assets/Tests/PlayMode/AoePlayModeTests.cs:894-901` does:

```csharp
MethodInfo method = typeof(SkillDriver).GetMethod(
    "CompileAndRegister", BindingFlags.Instance | BindingFlags.NonPublic);
method.Invoke(driver, null);
```

`GetMethod(string, BindingFlags)` throws `AmbiguousMatchException` if a second
overload exists; `Invoke(driver, null)` throws `TargetParameterCountException` if
the method has any parameters, optional or not (`Invoke` never auto-supplies C#
default values). Pass edit context through private fields instead.

## Changes

1. **`Assets/Scripts/Skills/SkillDriver.cs`** — add two fields near the existing
   pending-edit fields (~line 48):
   ```csharp
   private bool preserveCooldownState;
   private int cooldownResetNodeIndex = -1;
   ```

2. **`SkillDriver.cs`** — factor the kind-check out of `IsCooldownBlocked`
   (lines 372-381) into a static helper, and use it from both call sites:
   ```csharp
   private static bool AffectsRootCooldown(SkillLoadoutEditKind kind) =>
       kind is SkillLoadoutEditKind.SetSkill or SkillLoadoutEditKind.ClearSkill
           or SkillLoadoutEditKind.SetSupport or SkillLoadoutEditKind.ClearSupport
           or SkillLoadoutEditKind.IncreaseSupportCap or SkillLoadoutEditKind.DecreaseSupportCap;

   private bool IsCooldownBlocked(SkillLoadoutEditCommand command) =>
       AffectsRootCooldown(command.Kind) && IsRootNodeOnCooldown(command.NodeIndex);

   private bool IsRootNodeOnCooldown(int nodeIndex)
   {
       int rootIndex = FindRootSlotForNode(nodeIndex);
       return rootIndex >= 0 && slotStates[rootIndex] != null && !slotStates[rootIndex].IsReady;
   }
   ```
   (`IsRootNodeOnCooldown` is also consumed directly by task 002 — add it here so
   002 doesn't duplicate the lookup.)

3. **`SkillDriver.cs`** — in `CompileAndRegister()` (lines 180-230), capture and
   immediately clear the preservation request at the top, then use it in the slot
   loop:
   ```csharp
   private void CompileAndRegister()
   {
       if (runtimeLoadout == null) return;

       SkillSlotState[] previousStates = preserveCooldownState ? slotStates : null;
       int[] previousNodeIndices = preserveCooldownState ? this.rootNodeIndices : null;
       int resetNodeIndex = cooldownResetNodeIndex;
       preserveCooldownState = false;
       cooldownResetNodeIndex = -1;

       // ...unchanged validation/snapshot/rootNodeIndices-candidate-list setup...

       for (int i = 0; i < maxSlots; i++)
       {
           RuntimeSkillDefinition def = SkillSetCompiler.Compile(nodes, rootNodeIndices[i], snapshot);
           if (def == null) continue;

           compiledSlots[activeSlotCount] = def;
           int nodeIndex = rootNodeIndices[i];
           SkillSlotState state = nodeIndex != resetNodeIndex
               ? FindPreservedState(previousStates, previousNodeIndices, nodeIndex)
               : null;
           state ??= new SkillSlotState();
           state.SetRecoveryTime(def.RecoveryTime);
           slotStates[activeSlotCount] = state;
           this.rootNodeIndices[activeSlotCount] = nodeIndex;
           activeSlotCount++;
       }

       // ...unchanged RegisterProjectileTypes/RegisterAoeTypes/RegisterSpawnTemplates/validationWarnings...
   }

   private static SkillSlotState FindPreservedState(SkillSlotState[] states, int[] nodeIndices, int nodeIndex)
   {
       if (states == null || nodeIndices == null) return null;
       for (int i = 0; i < nodeIndices.Length; i++)
           if (nodeIndices[i] == nodeIndex) return states[i];
       return null;
   }
   ```
   Note the local variable named `rootNodeIndices` (the `List<int>` candidate list
   built earlier in the method) shadows the `this.rootNodeIndices` field exactly as
   it does today — keep using `this.rootNodeIndices` for the field, same as the
   existing code.

4. **`SkillDriver.cs`** — in `ProcessPendingEdit()` (lines 340-370), set the flags
   immediately before the existing `CompileAndRegister()` call on the success path:
   ```csharp
   runtimeLoadout = candidate;
   preserveCooldownState = true;
   cooldownResetNodeIndex = AffectsRootCooldown(command.Kind) ? command.NodeIndex : -1;
   CompileAndRegister();
   revision++;
   LoadoutChanged?.Invoke(revision);
   EditResolved?.Invoke(new SkillLoadoutEditResult(true, revision, null));
   ```

5. **No change** to the `Start()` call or either `CompileAndRegister()` call in
   `TryRestoreRuntimeLoadout` (lines 75, 324, 329) — they must keep compiling fresh
   cooldown state, since a restore is not an edit and must not leak stale session
   cooldowns via coincidental node-index overlap.

## Acceptance Criteria

- Equip skills at nodes 0 and 1 (both roots). Let node 0 partially cool down
  (fire it, wait less than its recovery time). Edit node 1's support. Node 0's
  `GetCooldownProgressForNode(0)` must be unchanged by the edit (same value
  before/after, modulo the tick that elapsed during the edit frame).
  Node 1 (the edited root) must show progress reset to 0.
- A `SetTrigger`/`ClearTrigger` command must never reset any root's progress
  (`AffectsRootCooldown` returns false for these kinds).
- `TryRestoreRuntimeLoadout` still produces fresh (zero-progress-or-ready)
  `SkillSlotState` for every restored root, unaffected by this change.
- `AoePlayModeTests.CompileAndRegister(driver)` (the reflection helper) still
  compiles and passes unmodified — confirms the signature constraint held.

## Dependencies

None. Task 002 reads `IsRootNodeOnCooldown` introduced here (via `SkillDriver`),
but 002 does not require the preservation fix itself to function correctly.

## Scope

Medium — one method restructured, one shared helper extracted, two new fields.
No public API change.
