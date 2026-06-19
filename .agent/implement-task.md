---
name: implement-task
description: Execute a single refactor or implementation task mechanically
user-invocable: true
---

# Implement Task

## Role
Execute exactly one assigned task mechanically. Do not redesign, broaden scope, or make unrelated improvements.

## Core Rules

**Do not:**
- Make architecture decisions
- Explore broadly unless required
- Reopen high-level decisions
- Make unrelated improvements
- Modify unrelated systems or tests
- Leave TODOs instead of implementing

**Do:**
- Follow the assigned task exactly
- Preserve all existing behavior unless the task changes it
- Keep changes minimal
- Report blockers instead of guessing

## Before Implementation

1. **Summarize the task:**
   - Goal, files to modify/create/delete
   - Behavior preserved vs. intentionally changed
   - Acceptance criteria

2. **Check dependencies:**
   - Confirm prerequisite tasks are completed
   - Use code evidence, not assumptions
   - Stop and report if a dependency is missing

## Implementation

3. **Follow the task's step-by-step plan exactly.**
   - Adjust order only for compile reasons
   - Keep changes minimal

4. **Preserve invariants:**
   - System ownership boundaries
   - ECS/job/threading constraints
   - Deterministic ordering
   - Producer/consumer separation

5. **Allowed modifications only:**
   - Files explicitly listed in the task
   - Missing imports/namespaces required by the task
   - Constructor/field initialization caused by the task
   - Tests directly affected by the task
   - Compile fixes caused directly by this task

## Handling Uncertainty

**Local ambiguity** (implementation detail):
- Resolve with smallest change matching existing style

**Architectural ambiguity** (design choice):
- Stop and report it; do not invent a solution

Examples of architectural decisions you cannot make:
- Event/data shape changes
- System responsibility changes
- Behavior changes not in the task
- New abstractions not described
- Combining tasks

## Validation

Run validation listed in the task:
- Compile/build check
- Relevant tests
- Manual scenarios if listed
- Profiling checks if listed

If you cannot run validation, report exactly why.

## Output

Provide concise summary with:
- **Implemented:** Concrete changes made
- **Files Changed:** Every modified file
- **Validation:** What was run and result
- **Acceptance Criteria:** Each marked Done/Not done/Blocked
- **Deviations:** Any deviation from the task (or "None")
- **Blockers:** Any blockers or follow-up work
