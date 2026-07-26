# 003 — Picker waits for `EditResolved` instead of closing on queue

## Goal

`SkillLoadoutUi.AddChoice` closes the picker the instant `TryQueueEdit` returns
`true` (command *accepted into the queue*), not once the driver actually resolves
it. A later rejection has nowhere to surface. Per user direction: make the picker
wait; no new eligibility/validation UI in this task (that's future work once real
validator-driven disabling lands — see index.md open questions).

## Changes

1. **`Assets/Scripts/SkillUi/SkillLoadoutUi.cs`** — replace `AddChoice`'s inline
   handler (line 237-243) with a named submit path:
   ```csharp
   private void AddChoice(VisualElement parent, string label, SkillLoadoutEditCommand command)
   {
       var choice = pickerChoiceTemplate.Instantiate().Q<Button>("choice");
       choice.text = string.IsNullOrWhiteSpace(label) ? "Unnamed" : label;
       choice.clicked += () => SubmitChoice(command);
       parent.Add(choice);
   }

   private void SubmitChoice(SkillLoadoutEditCommand command)
   {
       if (!skillDriver.TryQueueEdit(command, out string rejectionReason))
       {
           ShowPickerStatus(rejectionReason);
           return;
       }

       SetPickerPending(true);
   }
   ```

2. **`SkillLoadoutUi.cs`** — add two small helpers:
   ```csharp
   private void SetPickerPending(bool pending)
   {
       modal?.SetEnabled(!pending);
   }

   private void ShowPickerStatus(string message)
   {
       Label title = modal?.Q<Label>("title");
       if (title != null) title.text = message;
   }
   ```
   `modal.SetEnabled(false)` disables the entire subtree, including `#cancel` —
   deliberate: once an edit is queued it will be processed on the driver's next
   `Tick` regardless, so there's nothing left to legitimately cancel until it
   resolves.

3. **`SkillLoadoutUi.cs`** — replace `OnEditResolved` (line 252):
   ```csharp
   private void OnEditResolved(SkillLoadoutEditResult result)
   {
       if (result.Accepted) return; // LoadoutChanged already closed the picker via RefreshBar
       SetPickerPending(false);
       ShowPickerStatus(result.RejectionReason);
   }
   ```
   The accepted branch is deliberately a no-op: the contract guarantees
   `LoadoutChanged` fires before `EditResolved` on success
   ([skill-loadout-editing.md:112-113](../../Docs/contracts/skill-loadout-editing.md)),
   and `OnLoadoutChanged` already calls `RefreshBar()` → `ClosePicker()` (line
   251-252 today), so the modal is already gone by the time this fires.

## Notes

- No UXML/USS changes needed — reuses the existing `#title` label for status text
  instead of adding a dedicated element, matching the "bare bones is fine for now"
  scope the user set for the modal (gap 4, deferred).
- After a rejection, the title permanently shows the rejection reason instead of
  "Select {Kind}" until the picker is reopened — acceptable for this bare-bones
  pass; a dedicated status element is a natural follow-up alongside real
  eligibility UI, not required now.

## Acceptance Criteria

- Click a choice: picker's choices and cancel button become non-interactive
  immediately (no double-submit possible), picker does **not** close yet.
- Edit accepted: picker closes via the existing `LoadoutChanged` → `RefreshBar`
  path, same as before.
- Edit rejected (e.g. stale revision, cooldown-blocked): picker re-enables, title
  shows `result.RejectionReason`, current equipment is untouched (no optimistic
  mutation — already guaranteed since UI never mutated local state to begin with).
- `TryQueueEdit` itself returns `false` synchronously (e.g. "Another loadout edit
  is pending"): picker stays fully interactive, title shows that reason
  immediately (previously silently swallowed via `out _`).

## Dependencies

None functionally. Touches the same `OpenPicker`/`AddChoice` region of the file as
task 004 — implement this one first (see index.md ordering note).

## Scope

Small — one method split into two, two small helpers, one existing handler
simplified to an early return.
