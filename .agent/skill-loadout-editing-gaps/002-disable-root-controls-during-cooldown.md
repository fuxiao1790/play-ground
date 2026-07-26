# 002 — Disable a root's skill/support/cap controls while it's on cooldown

## Goal

`SkillDriver` already rejects skill/support/cap edits on a direct-cast root while
its cooldown is running (`IsCooldownBlocked`,
[skill-loadout-editing.md:133-134](../../Docs/contracts/skill-loadout-editing.md)),
but the bar never reflects this — the buttons stay clickable and the rejection is
silent (compounded by task 003 fixing the silent part). Per user direction, make
this visible: disable the node's skill button, every support button, and both cap
buttons while that node's root is on cooldown.

## Changes

1. **`Assets/Scripts/Skills/SkillDriver.cs`** — add a public read accessor next to
   `GetCooldownProgressForNode` (line ~928), reusing `IsRootNodeOnCooldown` from
   task 001:
   ```csharp
   public bool IsRootOnCooldown(int nodeIndex) => IsRootNodeOnCooldown(nodeIndex);
   ```

2. **`Assets/Scripts/SkillUi/SkillLoadoutUi.cs`** — add per-node arrays alongside
   the existing `cooldownLabels` field (line ~30):
   ```csharp
   private Label[] cooldownLabels;
   private Button[] skillButtons;
   private Button[] increaseButtons;
   private Button[] decreaseButtons;
   private List<Button>[] supportButtonsByNode;
   ```
   Size them alongside `cooldownLabels` in `Awake()` (line 55):
   ```csharp
   cooldownLabels = new Label[initialNodeCount];
   skillButtons = new Button[initialNodeCount];
   increaseButtons = new Button[initialNodeCount];
   decreaseButtons = new Button[initialNodeCount];
   supportButtonsByNode = new List<Button>[initialNodeCount];
   ```
   (Needs `using System.Collections.Generic;` — check it's not already imported;
   if missing, add it.)

3. **`SkillLoadoutUi.cs`** — in `AddNodeColumn` (lines 115-154), replace the
   inline cap-enabled computation with two small helpers (reused by `Update()`
   below), and populate the new arrays:
   ```csharp
   private bool CanIncreaseSupportCap(int nodeIndex)
   {
       SkillSet set = GetSkillSet(nodeIndex);
       return set != null && set.SupportSlotCount < set.MaxSupportCount;
   }

   private bool CanDecreaseSupportCap(int nodeIndex)
   {
       SkillSet set = GetSkillSet(nodeIndex);
       return set != null && set.SupportSlotCount > 0;
   }
   ```
   In `AddNodeColumn`, replace:
   ```csharp
   decrease.SetEnabled(skillSet.SupportSlotCount > 0);
   ...
   increase.SetEnabled(skillSet.SupportSlotCount < skillSet.MaxSupportCount);
   ```
   with:
   ```csharp
   decrease.SetEnabled(CanDecreaseSupportCap(nodeIndex));
   ...
   increase.SetEnabled(CanIncreaseSupportCap(nodeIndex));
   ```
   and record references (mirroring the existing `cooldownLabels[nodeIndex] = cooldown;`
   at the bottom of the method):
   ```csharp
   skillButtons[nodeIndex] = skill;
   if (skillSet != null)
   {
       increaseButtons[nodeIndex] = increase;
       decreaseButtons[nodeIndex] = decrease;
       supportButtonsByNode[nodeIndex] = new List<Button>(supportSlotCount);
   }
   else
   {
       increaseButtons[nodeIndex] = null;
       decreaseButtons[nodeIndex] = null;
       supportButtonsByNode[nodeIndex] = null;
   }
   ```
   Inside the existing support-slot loop (line ~133-140), append each created
   `support` button to the list: `supportButtonsByNode[nodeIndex].Add(support);`
   after `support.clicked += ...`. These arrays are fully repopulated on every
   `RefreshBar()` call (index-by-index), so no explicit clearing is needed between
   rebuilds.

4. **`SkillLoadoutUi.cs`** — extend `Update()` (lines 83-91) to lock controls
   whose root is on cooldown:
   ```csharp
   private void Update()
   {
       for (int i = 0; i < initialNodeCount; i++)
       {
           if (cooldownLabels[i] == null) continue;
           float progress = skillDriver.GetCooldownProgressForNode(i);
           cooldownLabels[i].text = progress > 0f && progress < 1f
               ? $"{Mathf.CeilToInt((1f - progress) * 10f) / 10f:0.0}" : string.Empty;

           bool locked = skillDriver.IsRootOnCooldown(i);
           skillButtons[i]?.SetEnabled(!locked);
           increaseButtons[i]?.SetEnabled(!locked && CanIncreaseSupportCap(i));
           decreaseButtons[i]?.SetEnabled(!locked && CanDecreaseSupportCap(i));

           List<Button> supports = supportButtonsByNode[i];
           if (supports == null) continue;
           for (int s = 0; s < supports.Count; s++)
               supports[s].SetEnabled(!locked);
       }
   }
   ```

## Notes

- Trigger buttons are intentionally untouched — the contract keeps trigger edits
  allowed during a root's cooldown.
- `SetEnabled` blocks both `clicked` firing and (for buttons) applies the built-in
  disabled visual state — no new USS needed.
- This is a read-only UI projection of driver state (`IsRootOnCooldown`), matching
  the existing `GetCooldownProgressForNode` pattern — no ownership violation per
  [ui.md](../../Docs/ui.md)'s "State And Commands" section.

## Acceptance Criteria

- Fire a root skill (starts its cooldown). While on cooldown: its skill button,
  every support button under it, and both cap buttons are disabled (clicking does
  nothing, no picker opens). Its trigger button (if any) stays enabled.
- Once the root becomes ready again (`IsReady == true`), all controls re-enable
  within one frame.
- A triggered-only node's controls are never disabled by this change (it has no
  root cooldown; `IsRootOnCooldown` returns false for it).
- An empty node's `increaseButtons[i]`/`decreaseButtons[i]`/`supportButtonsByNode[i]`
  stay `null` and are skipped without throwing.

## Dependencies

Uses `IsRootNodeOnCooldown` introduced in task 001. Does not require 001's
preservation fix to be correct — cooldown *readiness* is already computed
correctly today; 001 only fixes readiness being wiped by unrelated edits.

## Scope

Small — additive fields + one extended loop, no new control flow.
