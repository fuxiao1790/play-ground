---
name: plan-changes
description: Create an implementation plan after discussing an issue or bug
user-invocable: true
---

# Plan Changes

## Purpose
Produce a complete, unambiguous implementation plan after discussing a bug, issue, or feature request.

## Behavior
- **Grounded**: Before planning, read the relevant design docs and code and extract the contracts the change must respect — concurrency/threading guarantees, data-flow and ownership boundaries, lifecycle/allocation rules, performance budgets. Capture these yourself; do not wait for the user to volunteer them.
- **Reuse-first**: Prefer conforming to a mechanism the system already has over inventing a parallel one. If the change reinvents something that already exists, say so and justify the divergence.
- **Decisive**: Make clear architectural decisions without ambiguity
- **Clarifying**: When a load-bearing invariant is not stated in code or docs (who writes a structure and when, what is safe to read concurrently, what owns a lifetime), ask one explicit question rather than assuming.
- **Pressure-tested**: Validate the design against each identified invariant before finalizing. A plan not checked against the system's constraints is not done.
- **Persistent**: Store all plans and subtasks under `./.agent/<task_name>/...`

## Before Planning: Ground the Design
The plan is only as good as the constraints it respects, and that context must end
up in `index.md` — not only in conversation. Before writing tasks:
1. Read the design docs and code for the affected systems.
2. Extract the load-bearing contracts the change must honor — threading/concurrency
   guarantees, data-flow and ownership boundaries, lifecycle and allocation rules,
   performance budgets.
3. Identify existing mechanisms that already solve part of the problem; default to
   conforming to them instead of adding a parallel path.
4. Name any invariant that is load-bearing but unwritten. If you cannot confirm it
   from code or docs, ask one explicit question before proceeding.
5. Pressure-test the proposed approach against each constraint, and record the result.

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
- **Constraints & invariants the change must respect** — concurrency model, data-flow and ownership boundaries, lifecycle/allocation rules, performance budgets — each with its source (doc or code reference)
- **Mechanisms reused vs. introduced** — the existing system the change conforms to, plus justification for anything new
- **Design validation** — how the proposed model holds against each stated invariant
- Complete task list with references to subtask files
- Open questions, dependencies, or considerations

### Subtask Files (001-XXX.md)
- One discrete, reviewable change per file
- Clear acceptance criteria
- Dependencies on other subtasks (if any)
- Estimated scope/complexity
