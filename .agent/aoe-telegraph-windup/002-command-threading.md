# 002 — Thread `InitialDelaySeconds` through command + request + builders

## Goal
Carry an authored `InitialDelaySeconds` from the skill/request layer to `AoeSpawnCommand`, mirroring
how `Lifetime` / `RepeatHitCooldownSeconds` already thread. Default `0` everywhere ⇒ no behavior change
until materialization (003) reads it.

## Changes

1. **[AoeSpawnPipeline.cs](../../Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs)** — add
   `public float InitialDelaySeconds;` to `AoeSpawnCommand` (near `Lifetime`,
   [:56](../../Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs#L56)).

2. **`AoeSpawnRequest`** — add `InitialDelaySeconds` alongside `LifetimeSeconds`/`TickIntervalSeconds`
   (same file/type that exposes those; used by `CombatRoot.AoeCommandFor`).

3. **[CombatRoot.cs](../../Assets/Scripts/System/Common/CombatRoot.cs)** — set it in every
   `AoeSpawnCommand` constructed:
   - `AoeCommandFor` ([:466](../../Assets/Scripts/System/Common/CombatRoot.cs#L466)):
     `InitialDelaySeconds = request.InitialDelaySeconds,`.
   - The template build at [:415-416](../../Assets/Scripts/System/Common/CombatRoot.cs#L415)
     (`RepeatHitCooldownSeconds`/`Lifetime = request...`): add `InitialDelaySeconds = request...,`.

4. **[PlayerSkillDriver.cs](../../Assets/Scripts/Skills/PlayerSkillDriver.cs)** — every
   `AoeSpawnCommand` build:
   - `BuildAoeTemplate` ([:695-700](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L695)):
     `InitialDelaySeconds = child.InitialDelaySeconds,` (source from the child definition — see 004).
   - The on-hit child template at [:638-639](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L638):
     add `InitialDelaySeconds = child.InitialDelaySeconds,`.
   - Stacking-detonation commands at [:827](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L827)/[:855](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L855):
     set `InitialDelaySeconds = 0f` (stacking debuff AOEs get no windup unless a source is added later);
     leave a comment noting the intentional zero.

5. **Template hashing / determinism** — if `AoeSpawnCommand` fields feed a template hash
   (`TemplateKey`), include `InitialDelaySeconds` so two templates differing only by windup do not
   collide. Verify against the registry/template-key builder; add if present.

## Acceptance criteria
- Compiles. `InitialDelaySeconds` defaults to `0` on every path; existing tests unchanged.
- Grep: every `new AoeSpawnCommand { ... }` site sets `InitialDelaySeconds` (explicit, even if `0`).

## Scope / complexity
Low–medium. Mechanical field-threading across ~5 construction sites + request type.

## Dependencies
None hard; pairs with 003 (which consumes the field) and 004 (which authors the value).
