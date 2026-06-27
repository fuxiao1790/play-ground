---
name: pre-planning
description: Generic exploration template for agents to map architecture before planning
user-invocable: true
---

# Pre-Planning & Exploration

## Purpose
**Explore agent ONLY:** Gather architectural context for implementation planning by reading structured documentation first. Only resort to code exploration if documentation is incomplete.

**CRITICAL:** This agent stops after producing `info.md`. Do NOT create work breakdowns, task lists, deployment orders, or plan files. That is the job of the Plan agent in a separate step.

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
From docs, identify and LINK (don't summarize):
- Where `[FEATURE/SYSTEM]` is described → link to that section
- Current design and rationale → link to design doc
- Which systems interact with `[FEATURE/SYSTEM]` → link to those files
- File locations and component names → link to specific lines
- Performance considerations or constraints → link to perf docs

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
- Report findings back as links: `[ClassName](path/file.cs#L123)` — one sentence why it matters
- Don't copy/paste code; link to the actual line and briefly note what it shows

### Output Structure

**ONLY produce ONE file:** `./.agent/<task_name>/info.md`

Do NOT create index.md, task lists, work breakdowns, or deployment plans. The Explore agent's job ends with info.md.

Create `info.md` with **links, not summaries**:

```markdown
---
name: [TASK]-exploration
description: Exploration findings for [TASK]
---

# Exploration Findings

## Current Design
- Design doc: [Docs/architecture/...md](path)
- Related systems: [Docs/reference/...md](path), [Docs/reference/...md](path)

## Key Files & Components
- `ClassName` ([path/file.cs:line](path/file.cs#L123)) — brief note
- `ComponentType` ([path/file.cs:line](path/file.cs#L456)) — brief note

## System Integration
- Links to systems that interact with [FEATURE/SYSTEM]
- [SystemName.cs](path) reads/writes [ComponentName]

## Performance & Constraints
- [Docs/reference/performance.md#section](path) — constraint or budget
- Threading model: [Docs/architecture/ecs-notes.md#threading](path)

## Documentation Gaps
- Missing: [what's missing] — should be documented in [Docs/section]
- Incomplete: [what needs clarification] — reference to [file.cs:line](path) shows actual behavior differs from doc

## Recommended Next Step
[One sentence: is documentation sufficient for planning, or what needs documenting first?]
```

**Key principle:** Link to sources rather than summarize them. The Plan agent follows the links to understand context. This keeps info.md focused and maintainable.

## Key Resources
- `./Docs/project-overview.md` — doc index
- `./Docs/folder-structure.md` — file organization
- `./Docs/architecture/` — architecture and design
- `./Docs/reference/` — reference material and patterns

