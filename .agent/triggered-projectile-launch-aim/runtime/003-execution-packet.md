# Task Execution Packet

## Task
003-compile-and-template-launch-aim.md

## Goal
Wire the trigger-authored launch-aim policy (added in task 002 on `TriggerLink`) through
compile-time snapshot and into the registered projectile spawn template, WITHOUT
touching expansion/ECS runtime behavior (that is task 004) and WITHOUT adding any
field to `ProjectileSpawnEvent` (must stay slim).

Concretely:
1. Add `ProjectileLaunchAimMode ProjectileLaunchAimMode { get; set; }` and
   `float ProjectileLaunchAimRange { get; set; }` properties to
   `RuntimeProjectileDefinition` (defaults: enum zero / `0f`, so root/self compiles
   never carry policy unless explicitly stamped).
2. In `SkillSetCompiler.cs`, stamp these two properties onto the compiled
   `RuntimeProjectileDefinition` **only** at the three points where an incoming
   trigger's target/child was just compiled and turned out to be a
   `RuntimeProjectileDefinition`:
   - Interval trigger's compiled child (`ApplyIntervalSpawn`, the
     `case RuntimeProjectileDefinition childDef:` branch).
   - On-hit trigger's compiled target (`CompileInternal`'s
     `else if (link is OnHitTrigger)` branch, only when `compiledTarget is
     RuntimeProjectileDefinition`).
   - Stack trigger's compiled detonation target (`CompileInternal`'s
     `else if (link is StackTrigger stackTrigger)` branch, only when
     `compiledTarget is RuntimeProjectileDefinition`, i.e. the definition that
     becomes `stackingDetonation.Detonation`).
   Do **not** stamp the top-level/root runtime projectile (the one returned to the
   player-cast path) — it simply never passes through these three call sites, so it
   naturally keeps the enum-zero/`0f` default.
3. Add `ProjectileLaunchAimMode LaunchAimMode;` and `float LaunchAimRange;` fields to
   the `ProjectileSpawnCommand` struct in
   `Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs`.
4. In `SkillIntervalTemplateBuilder.BuildProjectileTemplate` (in `SkillDriver.cs`),
   copy `child.ProjectileLaunchAimMode` / `child.ProjectileLaunchAimRange` into the
   returned `ProjectileSpawnCommand`'s new `LaunchAimMode`/`LaunchAimRange` fields.
   Since this one builder method is used for every `RuntimeProjectileDefinition` node
   (root and every triggered child/detonation — confirmed by reading
   `RegisterSpawnTemplatesRecursive` call sites in `SkillDriver.cs`), no branching by
   "is this the root" is needed here: whatever value is already on `child` (None for
   root, possibly NearestHostile for a trigger-stamped child) flows straight through.
5. Do NOT modify `CombatRoot.SpawnTemplateFor` (the per-template hash-normalization
   method) — it already leaves fields like `ContinuousCollision`/`Speed`/`Tracking`/
   `BaseDirection` untouched, so the new `LaunchAimMode`/`LaunchAimRange` fields will
   automatically be included, unmodified, in the whole-struct hash computed by
   `SpawnTemplateHash.Of`. Confirmed by reading `CombatRoot.cs` lines ~822-847.
6. Do NOT modify `ProjectileSpawnEvent`, `ProjectileCommandFor` (the *root/manual*
   spawn-request path in `CombatRoot.cs`, used for direct `ProjectileSpawnRequest`
   casts — separate from `BuildProjectileTemplate`), or any apply/expansion system.

## Files Allowed To Modify
- Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs
- Assets/Scripts/Skills/SkillSetCompiler.cs
- Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs
- Assets/Scripts/Skills/SkillDriver.cs (only the `SkillIntervalTemplateBuilder.BuildProjectileTemplate` method)

## Files Allowed To Create
- None.

## Files Likely Needed For Reading
- Assets/Scripts/Skills/Trigger/TriggerLink.cs (task 002 result: `ProjectileLaunchAimMode` property, `ResolveProjectileLaunchAimRange()` method)
- Assets/Scripts/System/Projectiles/ProjectileLaunchAimMode.cs (task 002 result: the enum)
- Assets/Scripts/Skills/SkillSetCompiler.cs (full file — `CompileInternal`, `ApplyIntervalSpawn`, `AttachOnHitTarget`)
- Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs
- Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs (full `ProjectileSpawnCommand`/`ProjectileSpawnEvent` struct definitions)
- Assets/Scripts/Skills/SkillDriver.cs — specifically `SkillIntervalTemplateBuilder.BuildProjectileTemplate` (~line 1403) and its callers (`RegisterSpawnTemplatesRecursive` ~line 820-887, `RegisterProjectileIntervalTemplate` ~line 889)
- Assets/Scripts/System/Core/CombatRoot.cs — read-only, to confirm `SpawnTemplateFor` normalization (~line 822) and `ProjectileCommandFor` (~line 653) behavior; do not modify.

## Behavior To Preserve
- `ProjectileSpawnEvent` stays exactly as-is (no new field) — event remains template key plus per-instance frame.
- Root/manual cast path (`ProjectileCommandFor`, `ProjectileSpawnRequest`) is completely untouched.
- `SpawnTemplateFor` normalization list is untouched.
- Existing template hash behavior for all fields other than the two new ones is unchanged.
- Continuous vs discrete `ContinuousCollision`/`Tracking` fields and their existing compile logic are untouched.

## Behavior To Change
- `RuntimeProjectileDefinition` gains two new plain properties (policy carried as data only, no logic).
- `SkillSetCompiler` stamps those two properties from the incoming `TriggerLink` onto exactly the three described compiled-child/target `RuntimeProjectileDefinition` instances, using `link.ProjectileLaunchAimMode` and `link.ResolveProjectileLaunchAimRange()` (the accessor/resolve-method added in task 002).
- `ProjectileSpawnCommand` gains two new fields; `BuildProjectileTemplate` copies them from the source `RuntimeProjectileDefinition`.

## Relevant Global Context
- Decision 3 (index.md): "Copy incoming trigger policy onto fresh compiled `RuntimeProjectileDefinition`, then existing command-shaped projectile template. Do not add trigger marker to spawn event."
- Decision 6: exclude `ContactGateSeedTargetId` from acquisition — not relevant to this task (that's task 004's expansion-system concern), but note the event field is literally named `ContactGateSeedTargetId` and the command field is `SeedContactGateTargetId` — do not rename either as part of this task.
- Game-logic/ECS boundary: `RuntimeProjectileDefinition` and `SkillSetCompiler` are plain C# game-logic types (not ECS/Burst); `ProjectileSpawnCommand` is a plain struct consumed by ECS systems but this task only adds passive data fields to it, no job/Burst code changes.
- This task's fields must be inert (harmless no-op) for AOE/targeted trigger targets — since the stamp only happens inside `if (... is RuntimeProjectileDefinition ...)` branches, non-projectile targets are naturally untouched.

## Dependencies Confirmed
- Task 002 complete: verified `Assets/Scripts/Skills/Trigger/TriggerLink.cs` has `public ProjectileLaunchAimMode ProjectileLaunchAimMode => projectileLaunchAimMode;` and `public float ResolveProjectileLaunchAimRange() => Mathf.Max(0f, projectileLaunchAimRange);`, and `Assets/Scripts/System/Projectiles/ProjectileLaunchAimMode.cs` exists with `None = 0`, `NearestHostile = 1`.

## Step-By-Step Instructions
1. Open `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`. Add, near the other simple properties (e.g. near `ContinuousCollision`/`Tracking`):
   ```csharp
   public ProjectileLaunchAimMode ProjectileLaunchAimMode { get; set; }
   public float ProjectileLaunchAimRange { get; set; }
   ```
   Add `using PlayGround.System.Combat.Projectiles;` if not already present (it already is, per file read — confirm before assuming).
2. Open `Assets/Scripts/Skills/SkillSetCompiler.cs`. Add a small private static helper mirroring the existing `ApplyIncomingTriggerManaCostMultiplier` pattern:
   ```csharp
   private static void ApplyIncomingTriggerLaunchAim(
       RuntimeSkillDefinition triggeredDefinition,
       TriggerLink triggerLink)
   {
       if (triggeredDefinition is not RuntimeProjectileDefinition projectileDefinition || triggerLink == null)
           return;

       projectileDefinition.ProjectileLaunchAimMode = triggerLink.ProjectileLaunchAimMode;
       projectileDefinition.ProjectileLaunchAimRange = triggerLink.ResolveProjectileLaunchAimRange();
   }
   ```
   Place it near `ApplyIncomingTriggerManaCostMultiplier`.
3. Call this helper at the three sites:
   - In `ApplyIntervalSpawn`, inside `case RuntimeProjectileDefinition childDef:`, call
     `ApplyIncomingTriggerLaunchAim(childDef, trigger);` (the `trigger` parameter there
     is the `IntervalSpawnTrigger`, itself a `TriggerLink`). Add this call alongside
     (not replacing) the existing `ApplyIncomingTriggerManaCostMultiplier(childDef, trigger);` call.
   - In `CompileInternal`'s `else if (link is OnHitTrigger)` branch, after
     `if (AttachOnHitTarget(runtime, compiledTarget)) ApplyIncomingTriggerManaCostMultiplier(compiledTarget, link);`,
     add `ApplyIncomingTriggerLaunchAim(compiledTarget, link);` inside the same `if`
     block (only when attach succeeded — mirror existing conditional structure exactly).
   - In `CompileInternal`'s `else if (link is StackTrigger stackTrigger)` branch,
     inside `if (compiledTarget != null)`, after
     `ApplyIncomingTriggerManaCostMultiplier(stackingDetonation, link);`, add
     `ApplyIncomingTriggerLaunchAim(compiledTarget, link);` (note: stamp `compiledTarget`
     itself here, not `stackingDetonation` — `stackingDetonation` is a
     `RuntimeStackingDetonation` wrapper, not a `RuntimeProjectileDefinition`; the
     actual projectile-typed instance is `compiledTarget`, which becomes
     `stackingDetonation.Detonation`).
4. Open `Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs`. Add to
   `ProjectileSpawnCommand` (near `Tracking`/`ContinuousCollision`):
   ```csharp
   public ProjectileLaunchAimMode LaunchAimMode;
   public float LaunchAimRange;
   ```
   Add the necessary `using` for the enum's namespace if not already present in this file.
5. Open `Assets/Scripts/Skills/SkillDriver.cs`, locate
   `SkillIntervalTemplateBuilder.BuildProjectileTemplate`, and add to the
   `return new ProjectileSpawnCommand { ... }` initializer:
   ```csharp
   LaunchAimMode = child.ProjectileLaunchAimMode,
   LaunchAimRange = child.ProjectileLaunchAimRange,
   ```
6. Do not touch `AoeSpawnCommand`, `TargetedSpawnCommand`, or their builders — policy is projectile-only.

## Acceptance Criteria
- Triggered projectile runtime copy carries incoming link's mode and clamped range.
- Root projectile runtime copy remains `None` even when same source asset also appears as triggered effect elsewhere.
- Continuous and discrete runtime projectile definitions carry same policy without changing `ContinuousCollision` or `Tracking`.
- Trigger links targeting non-projectile effects do not alter those definitions.
- Registered projectile template contains policy; normalized template retains it.
- Template hashes differ when only mode or range differs and deduplicate identical policy/content.
- Spawn event size/fields unchanged.

## Validation Required
- Static/search-based: grep for all `ProjectileSpawnCommand` object-initializer sites to confirm only `BuildProjectileTemplate` and `ProjectileCommandFor` construct it, and that `ProjectileCommandFor` (root path) is untouched and thus implicitly zero/None for the new fields.
- Code review: trace all three trigger call sites and confirm the helper is invoked with the correct `TriggerLink` instance and only when the compiled definition is actually a `RuntimeProjectileDefinition`.
- Confirm `ProjectileSpawnEvent` struct is byte-for-byte unchanged (no new field).
- Report whether a Unity compile check is possible; if not runnable, state so. Do not run Unity tests — user runs `SkillValidationEditModeTests` / `SpawnCommandUnificationTests` later (task 005 will add specific new cases; this task only needs to not break compilation).

## Hard Boundaries
- Do not modify files outside the allowed list except for imports/namespaces directly required by this task.
- Do not change architecture.
- Do not introduce new abstractions (no new wrapper types, no new "policy" struct — plain fields/properties only, matching the pattern of every other copied field in this pipeline).
- Do not combine this task with task 004 — no `ProjectileSpawnExpansionSystem` changes, no `CombatTargetAcquisition` calls, no acquisition logic at all in this task.
- Do not reopen index-level decisions (policy lives on `TriggerLink`→`RuntimeProjectileDefinition`→`ProjectileSpawnCommand`; event stays slim — settled).
- Stop on architectural ambiguity.
- Do not hand-edit Unity `.meta` files.
