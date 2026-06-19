---
name: plan-changes
description: Create an implementation plan after discussing an issue or bug
user-invocable: true
---

# Plan Changes

## Purpose
Produce a complete, unambiguous implementation plan after discussing a bug, issue, or feature request.

## Behavior
- **Decisive**: Make clear architectural decisions without ambiguity
- **Clarifying**: If required information is missing, prompt with a single explicit question
- **Persistent**: Store all plans and subtasks under `./.agent/<task_name>/...`

## Output Structure

```
./.agent/<task_name>/
├─ index.md                # authoritative plan + tasks index (required)
├─ 001-<short-slug>.md     # per-task file; three-digit, zero-padded
├─ 002-another-task.md
├─ 003-...                 # continue numbering sequentially
```

### index.md Requirements
- High-level summary of the implementation
- Rationale for major architectural decisions
- Complete task list with references to subtask files
- Any constraints, dependencies, or considerations

### Subtask Files (001-XXX.md)
- One discrete, reviewable change per file
- Clear acceptance criteria
- Dependencies on other subtasks (if any)
- Estimated scope/complexity
