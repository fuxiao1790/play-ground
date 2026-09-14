# Task Execution Packet

## Task
002-author-trigger-launch-aim.md

## Goal
Add a shared `ProjectileLaunchAimMode` enum (`None = 0`, `NearestHostile = 1`) and
authoring fields on the abstract `TriggerLink` base class (`Assets/Scripts/Skills/Trigger/TriggerLink.cs`)
for projectile launch-aim mode and acquisition range. Fields belong on `TriggerLink`,
NOT `SkillDefinition`/`ProjectileDefinition` — every trigger kind (interval, on-hit,
stack, on-expire) inherits them from the shared base. Existing assets must
deserialize to `None` with unchanged behavior. Expose a resolve method that clamps
range to non-negative, mirroring the existing `ResolveManaCostFactor()` pattern
already on `TriggerLink`.

This task does NOT touch the compiler, `RuntimeProjectileDefinition`, or any
ECS/runtime template code — that is task 003. This task only adds the enum type and
the authoring fields/accessor on `TriggerLink`.

## Files Allowed To Modify
- Assets/Scripts/Skills/Trigger/TriggerLink.cs

## Files Allowed To Create
- New enum file, e.g. `Assets/Scripts/System/Projectiles/ProjectileLaunchAimMode.cs`
  (namespace `PlayGround.System.Combat.Projectiles` — same folder/namespace pattern as
  the existing shared `ProjectileTrackingConfig` in
  `Assets/Scripts/System/Projectiles/ProjectileTrackingConfig.cs`, which is likewise
  authored from `Assets/Scripts/Skills` and consumed by ECS runtime code. Do not put
  the enum inside `Assets/Scripts/Skills` — it must be the shared/common type per
  index decision 2.)

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- Assets/Scripts/Skills/Trigger/TriggerLink.cs (current authoring base class)
- Assets/Scripts/System/Projectiles/ProjectileTrackingConfig.cs (naming/location/style precedent for a shared authoring-and-runtime type)
- Assets/Scripts/System/Core/CombatScope.cs (enum style precedent: `public enum CombatFaction : byte { None = 0, ... }`)
- Assets/Scripts/Skills/SkillDefinition.cs (precedent: `ProjectileDefinition` fields + `GetTrackingConfig()` resolve-method pattern — same shape you should mirror on TriggerLink, but do NOT modify this file; it's for pattern reference only)
- Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs, OnHitTrigger.cs, StackTrigger.cs, OnExpireTrigger.cs (all four subclass `TriggerLink` and will inherit the new fields automatically — read only to confirm none of them already declare conflicting members with the same name; do not modify them in this task)

## Behavior To Preserve
- Existing trigger assets deserialize to `None` (enum zero) and keep current behavior exactly.
- Projectile skill definition (`SkillDefinition.cs` / `ProjectileDefinition`) gains no launch-aim field — do not touch that file.
- Homing/tracking and continuous-collision authoring fields unchanged (do not touch `ProjectileTrackingConfig.cs` or `ProjectileDefinition`'s tracking fields).
- `TriggerLink` remains an abstract base with no behavior change beyond the two new fields + one resolve method.

## Behavior To Change
- `TriggerLink` gains:
  - `[SerializeField] private ProjectileLaunchAimMode projectileLaunchAimMode;` (default `None` since enum zero) with a public read accessor (mirror the existing private-serialized-field + public-property style already used for `displayName`/`description`/`icon` in this file, e.g. `public ProjectileLaunchAimMode ProjectileLaunchAimMode => projectileLaunchAimMode;`).
  - `[SerializeField] private float projectileLaunchAimRange;` with a public resolve method that clamps to non-negative, e.g. `public float ResolveProjectileLaunchAimRange() => Mathf.Max(0f, projectileLaunchAimRange);` (mirrors `ResolveManaCostFactor()` already in this file — negative serialized range resolves to zero per acceptance criteria).
  - Inspector tooltip on these fields stating the policy affects projectile effects spawned through this trigger only; root/player cast aim is unaffected (mirror existing `[Tooltip(...)]` usage on `manaCostIncreased` in this same file for style).
- New shared enum type `ProjectileLaunchAimMode` with exactly two members: `None = 0`, `NearestHostile = 1`.

## Relevant Global Context
- Root/player casts never enable this policy — this task only adds the authoring surface on the trigger edge; wiring to runtime/compiler happens in task 003, which will only stamp non-root, trigger-reached `RuntimeProjectileDefinition` instances with these values.
- One authored trigger may target projectile, AOE, or targeted skill — these fields are inert unless the compiled target ends up being a projectile (enforced in task 003, not here).
- Enum zero (`None`) must preserve current serialized asset behavior — this is why `None = 0` is mandatory and must be the unset/default value.
- No ECS/Burst/job constraints apply to this task — `TriggerLink` and the enum are plain, non-ECS, editor/game-logic-side authoring types (ScriptableObject-adjacent). `ProjectileLaunchAimMode` will later also be read from Burst-compatible ECS code once copied into command/template structs (task 003/004), so keep it a plain top-level `enum` with an explicit integer backing type if that matches the `CombatFaction : byte` precedent — using `byte` or default `int` backing is a local style choice; prefer matching `CombatFaction`'s `: byte` style since both are small trigger-adjacent enums copied into ECS-consumed structs.

## Dependencies Confirmed
- None required (task 002 has no dependencies, confirmed by index.md's dependency table: "001 and 002 independent").

## Step-By-Step Instructions
1. Create `Assets/Scripts/System/Projectiles/ProjectileLaunchAimMode.cs` with namespace `PlayGround.System.Combat.Projectiles`, declaring `public enum ProjectileLaunchAimMode : byte { None = 0, NearestHostile = 1 }` (match `CombatFaction`'s doc-comment style if a short comment explaining the sentinel value adds clarity, otherwise keep it minimal — no comment is also fine per default-no-comments guidance unless something non-obvious needs explaining).
2. Open `Assets/Scripts/Skills/Trigger/TriggerLink.cs`.
3. Add `using PlayGround.System.Combat.Projectiles;` to its using directives (matching how `SkillDefinition.cs`/`SkillSetCompiler.cs` already reference this namespace).
4. Add the two serialized fields (`projectileLaunchAimMode`, `projectileLaunchAimRange`) plus their public accessor/resolve-method, placed near the other per-link authored fields (e.g. after `manaCostIncreased`/`ResolveManaCostFactor()`), with a `[Header(...)]` grouping if that matches the file's existing `[Header("UI")]` convention, and an inspector `[Tooltip(...)]` per the acceptance criteria wording.
5. Do not modify `TriggerChain`, `LoadoutSlot`, `SkillSetSlot`, or `TriggerLinkSlot` in this file — those are unrelated to this task.
6. Do not touch any `TriggerLink` subclass file.

## Acceptance Criteria
- Every trigger link can author `None` or `NearestHostile` plus range.
- Existing trigger assets deserialize to `None` and keep current behavior.
- Negative serialized range resolves to zero (via the resolve method — do not clamp at serialization time with `[Min(0f)]` alone if that would change the Unity Inspector's ability to type a negative value that's then clamped in code; using `[Min(0f)]` on the field AND a defensive `Mathf.Max(0f, ...)` in the resolve method is acceptable and mirrors `manaCostMultiplier`'s `[Min(0f)]` pattern in this same file — pick whichever combination guarantees a negative *serialized* value, e.g. from an old asset or external edit, still resolves to zero at read time).
- Projectile skill definition gains no launch-aim field.
- Homing/tracking and continuous-collision authoring unchanged.
- Inspector tooltip states policy affects projectile effects spawned through this trigger only; root/player cast aim is unaffected.

## Validation Required
- Static/search-based verification: confirm the enum has exactly the two specified members with the specified values, and that `TriggerLink` compiles conceptually (no missing usings, no duplicate member names in the four subclasses).
- Report whether a Unity compile check is possible from this environment; if not runnable, state so explicitly. Do not run Unity tests.

## Hard Boundaries
- Do not modify files outside the allowed list except for imports/namespaces directly required by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by the task (no new interfaces, no new base classes).
- Do not combine this task with task 003 — do not touch `SkillSetCompiler.cs`, `RuntimeProjectileDefinition.cs`, `ProjectileSpawnCommand`, or any template/registry code in this task.
- Do not reopen index-level decisions (fields stay on `TriggerLink`, not `SkillDefinition` — this is settled).
- Stop on architectural ambiguity; enum backing type and exact tooltip wording are local/naming ambiguity, resolve with smallest reasonable choice.
- Do not hand-edit Unity `.meta` files.
