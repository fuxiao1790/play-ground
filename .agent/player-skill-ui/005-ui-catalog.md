# 005 - Add stable picker catalog

## Change

Add one explicit `SkillUiCatalog` ScriptableObject with typed lists for Skill,
Support, and Trigger choices. Each entry owns:

- stable non-empty ID, unique within its category;
- typed immutable definition reference;
- display name and short description;
- optional Sprite icon;
- deterministic authored order.

Catalog is presentation/save metadata only. It does not own unlock state,
equipment state, validation, or compiled data. Runtime never discovers assets
with `AssetDatabase` or Resources scanning.

Add editor validation for duplicate IDs/references, missing definitions, blank
labels, and optional-icon fallback. Create sample entries from current assets.
Skill entries may use existing skill sprites; supports/triggers use colored
initial fallback when icon is absent.

## Acceptance criteria

- Picker enumerates catalog without editor-only APIs.
- Stable IDs do not depend on asset name or array index.
- Duplicate/missing IDs fail authoring validation with actionable errors.
- Catalog order is stable across runs.
- Save DTO documented in task 001 can resolve all sample selections by ID.
- No save file is written in this delivery.

## Dependencies

- 001 defines ID/save semantics.
- 003 exposes shared compatibility predicates used to evaluate entries.

## Scope / complexity

Medium.

