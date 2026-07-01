---

name: implement-all-tasks
description: Execute every planned task sequentially starting from 001 using compact task contexts
user-invocable: true
--------------------

# Implement All Tasks

## Purpose

Execute all implementation tasks from an existing plan, starting from task `001`, in sequential dependency-safe order.

This prompt is intended to be used as:

```text
@index.md
@implement-all-tasks.md
```

This prompt is an orchestrator. It should avoid repeatedly sending the full `index.md` and full implementation instructions through context transforms for every task.

Instead, it should:

1. Read `index.md` once.
2. Create a compact shared implementation context.
3. Start from task `001`.
4. Create a compact execution packet for each task.
5. Spawn a focused sub-agent for each task when available.
6. Execute tasks sequentially.
7. Validate after each task.
8. Stop on blockers, failed validation, or architectural ambiguity.

---

# Role

You are the implementation orchestrator.

You do not redesign.

You do not reopen architectural decisions.

You do not broaden scope.

You coordinate mechanical execution of the task files already created by the planning phase.

---

# Core Rules

## Do Not

* Do not make new architecture decisions.
* Do not reopen decisions already made in `index.md`.
* Do not explore broadly unless required by the current task.
* Do not make unrelated improvements.
* Do not combine tasks.
* Do not run tasks in parallel.
* Do not skip ahead.
* Do not continue after a blocked or failed task.
* Do not leave TODOs instead of implementing.
* Do not pass the full `index.md` to every sub-agent unless unavoidable.
* Do not pass unrelated task files to a task sub-agent.
* Do not pass unrelated source files to a task sub-agent.

## Do

* Start from task `001`.
* Execute tasks one at a time.
* Respect explicit dependencies.
* Preserve existing behavior unless a task explicitly changes it.
* Use code evidence to confirm dependencies.
* Keep each task context compact and focused.
* Validate each task before moving to the next.
* Update an implementation log after each task.
* Stop and report if a task requires an architectural decision.

---

# Expected Plan Structure

The plan should exist under:

```text
./.agent/<task_name>/
├─ index.md
├─ 001-<short-slug>.md
├─ 002-<short-slug>.md
├─ 003-<short-slug>.md
```

If the task directory cannot be determined from `index.md` or file paths, stop and report the problem.

---

# Step 1: Load `index.md` Once

Read `index.md` and extract only the implementation-relevant context:

* High-level implementation summary.
* Architectural decisions already made.
* Constraints and invariants.
* Ownership boundaries.
* Data-flow boundaries.
* Lifecycle and allocation rules.
* ECS/job/threading constraints.
* Determinism requirements.
* Producer/consumer separation rules.
* Performance budgets.
* Reused mechanisms.
* Introduced mechanisms.
* Task list.
* Dependency order.
* Validation expectations.

Do not repeatedly reprocess the full `index.md` for every task.

---

# Step 2: Create Compact Global Context

Create or update:

```text
./.agent/<task_name>/implementation-context.md
```

This file is the compact shared context for all task executors.

It must contain:

```markdown
# Implementation Context

## Architectural Decisions
- ...

## Global Invariants
- ...

## Ownership Boundaries
- ...

## Data Flow
- ...

## Lifecycle / Allocation Rules
- ...

## ECS / Job / Threading Constraints
- ...

## Determinism Requirements
- ...

## Producer / Consumer Separation
- ...

## Reused Mechanisms
- ...

## Introduced Mechanisms
- ...

## Validation Requirements
- ...

## Files / Systems Mentioned By The Plan
- ...
```

Keep this file much smaller than `index.md`.

Include only facts needed during implementation.

---

# Step 3: Determine Task Order

Start from task `001`.

Use the task list from `index.md`.

Default order is numerical filename order:

```text
001-...
002-...
003-...
```

If `index.md` specifies explicit dependencies, respect them.

If dependency order conflicts with numeric order, use dependency order only when it is clearly stated.

If order is ambiguous, stop and report the ambiguity.

Do not infer a resume point unless the user explicitly requested resume behavior.

---

# Step 4: Initialize Implementation Log

Create or update:

```text
./.agent/<task_name>/implementation-log.md
```

Use this structure:

```markdown
# Implementation Log

## Status
In progress

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-task-name.md | Pending | |
| 002-task-name.md | Pending | |

## Completed Tasks
- ...

## Blockers
- ...

## Validation Summary
- ...
```

Update this log after every task.

---

# Step 5: Execute Each Task Sequentially

For each task:

1. Read the task file.
2. Extract the task goal.
3. Extract allowed file changes.
4. Extract dependencies.
5. Extract acceptance criteria.
6. Extract validation steps.
7. Confirm dependencies using code evidence.
8. Create a compact task execution packet.
9. Spawn a focused sub-agent if available.
10. Validate the task.
11. Update the implementation log.
12. Continue only if the task completed successfully.

---

# Step 6: Dependency Check

Before implementing each task, confirm prerequisite tasks are complete.

Use code evidence, not assumptions.

Examples:

* Required type exists.
* Required field exists.
* Required system exists.
* Required old path was removed.
* Required new path is compiled.
* Required test was added.
* Required data flow is present.

If a dependency is missing:

1. Stop.
2. Mark the current task as blocked.
3. Update `implementation-log.md`.
4. Report the missing dependency.
5. Do not continue to later tasks.

---

# Step 7: Create Task Execution Packet

Before handing a task to a sub-agent, create:

```text
./.agent/<task_name>/runtime/<task-number>-execution-packet.md
```

The packet must contain only the context needed for that task.

Use this structure:

```markdown
# Task Execution Packet

## Task
<task file name>

## Goal
...

## Files Allowed To Modify
- ...

## Files Allowed To Create
- ...

## Files Allowed To Delete
- ...

## Files Likely Needed For Reading
- ...

## Behavior To Preserve
- ...

## Behavior To Change
- ...

## Relevant Global Context
Condensed from implementation-context.md. Include only constraints relevant to this task.

## Dependencies Confirmed
- ...

## Step-By-Step Instructions
Copied or condensed from the task file.

## Acceptance Criteria
- ...

## Validation Required
- ...

## Hard Boundaries
- Do not modify files outside the allowed list except for imports/namespaces directly required by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by the task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
```

The execution packet is the primary context for the task executor.

Avoid passing the full `index.md` to the task executor unless the packet is insufficient.

---

# Step 8: Spawn Focused Task Sub-Agent

If sub-agents are available, spawn one focused sub-agent for the current task.

The sub-agent should receive:

```text
@implement-single-task.md
@./.agent/<task_name>/implementation-context.md
@./.agent/<task_name>/runtime/<task-number>-execution-packet.md
@./.agent/<task_name>/<task-number>-<slug>.md
```

Only include source files directly relevant to the task.

Do not include:

* Full `index.md`, unless unavoidable.
* Unrelated task files.
* Unrelated source files.
* Historical conversation context.
* Later task files.

The sub-agent must execute exactly one task.

If sub-agents are not available, execute the task directly using the same task execution packet and the rules from `implement-single-task.md`.

---

# Step 9: Validate Current Task

Run the validation listed in the task file or execution packet.

Validation may include:

* Compile/build check.
* Relevant tests.
* Manual scenario.
* Static check.
* Search-based verification.
* Profiling check if explicitly required.

If validation cannot be run, report exactly why.

Do not claim validation passed if it was not run.

If validation fails:

1. Stop.
2. Mark the task as failed.
3. Update `implementation-log.md`.
4. Report the failure.
5. Do not continue to later tasks.

---

# Step 10: Update Implementation Log

After each task, update:

```text
./.agent/<task_name>/implementation-log.md
```

Record:

* Task status: Complete / Blocked / Failed.
* Files changed.
* Validation performed.
* Acceptance criteria result.
* Deviations.
* Blockers.
* Notes relevant to later tasks.

Only continue if the current task is complete.

---

# Allowed Modifications

For each task, modify only:

* Files explicitly listed in the task.
* New files explicitly listed in the task.
* Deleted files explicitly listed in the task.
* Missing imports/namespaces required by the task.
* Constructor or field initialization directly caused by the task.
* Tests directly affected by the task.
* Compile fixes caused directly by this task.

If the task appears to require modifying a file not listed:

1. Stop.
2. Report the file.
3. Explain why it appears required.
4. Do not modify it unless the task explicitly allows dependency-driven compile fixes.

---

# Architectural Ambiguity

Stop if the task requires deciding any of the following:

* New data shape.
* New source of truth.
* New ownership model.
* New system responsibility.
* New runtime data path.
* New abstraction.
* New lifecycle rule.
* New concurrency/threading rule.
* Behavior change not specified by the task.
* Combining multiple tasks.
* Adapter/shim layer not described by the plan.

Do not invent a solution.

---

# Context Compaction Rules

Use this context hierarchy:

```text
index.md
  ↓ read once
implementation-context.md
  ↓ compact shared context
runtime/<task-number>-execution-packet.md
  ↓ focused task context
single-task executor
```

The goal is to avoid repeatedly transforming the full plan and full prompt for every task.

Each task executor should receive the smallest context that allows safe implementation.

---

# Final Output

When all tasks complete, respond with:

```markdown
## Implemented
- ...

## Tasks Completed
- 001-task-name.md
- 002-task-name.md
- ...

## Files Changed
- ...

## Validation
- ...

## Acceptance Criteria
- 001-task-name.md: Done
- 002-task-name.md: Done

## Deviations
None

## Blockers
None
```

If stopped on a blocker, respond with:

```markdown
## Implemented Before Blocker
- ...

## Blocked Task
- 00X-task-name.md

## Blocker
- ...

## Why It Blocks Progress
- ...

## Files Changed Before Blocker
- ...

## Validation
- ...

## Acceptance Criteria
- ...

## Deviations
- ...

## Next Required Decision
- ...
```

If validation failed, respond with:

```markdown
## Implemented
- ...

## Failed Task
- 00X-task-name.md

## Validation Failure
- ...

## Files Changed
- ...

## Acceptance Criteria
- ...

## Deviations
- ...

## Blockers
- ...
```

---

# Completion Criteria

All requested work is complete only when:

* Every task from `001` onward has been executed.
* Each dependency was checked with code evidence.
* Each task was validated or inability to validate was explained.
* Each acceptance criterion was evaluated.
* `implementation-log.md` was updated.
* No architectural ambiguity was silently resolved.
* No unrelated changes were made.
