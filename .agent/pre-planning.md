---
name: pre-planning
description: Generic exploration template for agents to map architecture before planning
user-invocable: true
---

# Pre-Planning & Exploration

## Purpose
Gather architectural context for implementation planning by reading structured documentation first. Only resort to code exploration if documentation is incomplete.

## How to Use This Template

When spawning an Explore agent, tailor the sections below to your task:

1. Replace `[TASK]` with your specific task (e.g., "faction overhaul")
2. Replace `[FEATURE/SYSTEM]` with the main components affected
3. List specific questions documentation should answer
4. Define fallback: what code exploration is needed only if docs don't have it

## Generic Exploration Template

### Documentation-First Approach

**Step 1: Read Structured Docs**
Start with these resources in order:
- `./Docs/project-overview.md` — doc index and navigation
- `./Docs/folder-structure.md` — file organization (find where code lives)
- `./Docs/architecture/index.md` — system architecture and design
- `./Docs/reference/simulation/ecs-notes.md` — ECS patterns and data structures
- Topic-specific design docs (e.g., spawn-system.md, skill-system.md)

**Step 2: Extract What Documentation Answers**
From docs, identify:
- Where `[FEATURE/SYSTEM]` is described
- Current design and rationale
- Which systems interact with `[FEATURE/SYSTEM]`
- File locations and component names
- Performance considerations or constraints

**Step 3: Report Gaps**
If documentation does NOT adequately answer your questions, report:
- What information is missing
- Which doc file(s) should contain it but don't
- Suggest what needs documenting (don't blind-search code)

### Questions Documentation Should Answer

For `[FEATURE/SYSTEM]`:
1. Where is it currently implemented? (files, components, systems)
2. How does it work in the current design?
3. Which systems depend on or interact with it?
4. What are the performance constraints?
5. What would break if the design changed?
6. How does it fit into the data flow?

### Code Exploration (Only If Needed)

If documentation gaps remain, read specific files:
- Use folder-structure.md to locate files
- Read architecture docs to understand expected patterns
- Read only files doc points to, don't blind search
- Report findings back as: "Docs should have this at [filepath] but it's missing"

### Output Structure

Create `./.agent/<task_name>/info.md` with:

```markdown
---
name: [TASK]-exploration
description: Exploration findings for [TASK]
---

# Exploration Findings

## Current Design (from docs)
[What documentation says about current implementation]

## Key Files & Components
[Specific files, components, systems mentioned in docs]

## System Integration
[Which systems interact with [FEATURE/SYSTEM]]

## Performance & Constraints
[Performance budgets, threading models, data-flow boundaries]

## Documentation Gaps
[What information was missing and should be documented]

## Recommended Next Step
[Is documentation sufficient for planning, or what needs documenting first?]
```

This `info.md` becomes input to the planning agent, reducing blind searching and providing grounded context for implementation planning.

## Key Resources
- `./Docs/project-overview.md` — doc index
- `./Docs/folder-structure.md` — file organization
- `./Docs/architecture/` — architecture and design
- `./Docs/reference/` — reference material and patterns

