# Implementation Context

## Architectural Decisions
- Folded skill `manaCost` and interval-link mana-to-energy conversion are complete.
- Health and mana use one managed `Resource` class, but retain direct typed ECS components: `Health` and `Mana`.
- GameObjects seed initial current value and own Max/regen; ECS owns runtime Current, damage, spending, and regen.
- Root casts enter ECS as external requests and are accepted or rejected by a serial mana gate. Internal interval/impact child spawns bypass it.

## Global Invariants
- Preserve serialized skill values with `FormerlySerializedAs`.
- Fold skill stats only at compile time; timed jobs read baked thresholds.
- Direct hit application must retain `ComponentLookup<Health>` access; no keyed resource buffers.
- Resource max updates may clamp Current down but must not reset it.

## Ownership Boundaries
- `UnitStatSheet` authors max and regen values.
- `CombatTargetProxy` creates neutral resource components and pushes only Max/regen changes.
- ECS owns runtime Current. Roots mirror it for reactions and presentation.
- `Resource` owns no ECS writes; roots own death/hurt reactions.

## Data Flow
- Authoring mana -> folded runtime mana -> interval trigger energy threshold.
- Unit sheet -> root resource -> target proxy seed/push -> ECS current/damage/spend/regen -> root mirror.
- Root cast -> external request -> serial gate -> internal spawn or rejection -> driver cooldown refund.

## ECS / Job / Threading Constraints
- Timed spawn jobs never write shared resources.
- Regen runs after combat apply and must not revive depleted health.
- Spawn gating is serial before expansion to avoid concurrent writes to one caster's Mana.
- Use existing scope-buffer and singleton lane ownership patterns; complete producer handles before draining.

## Reused Mechanisms
- Combat target proxy lifecycle, combat apply/result bridge, scope spawn buffers, template registries, and `TargetCompanion`.

## Introduced Mechanisms
- `Health`/`Mana` typed ECS resources, resource regen system, shared managed `Resource`, external spawn gate, rejection result lane/bridge.

## Validation Requirements
- Task-focused ECS/PlayMode tests where possible; static searches and `git diff --check` always.
- If Unity is locked or package compilation fails, record exact blocker without claiming tests pass.

## Files / Systems Mentioned By The Plan
- Target proxy/interface, combat apply, player/mob roots, unit stats, skill driver/translator, combat root, spawn expansion, presentation bridges, tests, and architecture docs.
