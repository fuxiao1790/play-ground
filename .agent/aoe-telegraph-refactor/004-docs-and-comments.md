# 004 — Docs & comments refresh

## Goal
Make the docs and remaining comments describe the post-refactor shape (named discriminator +
plain lifetime timer) and leave a breadcrumb for the windup follow-up. No behavior change.

## Changes

1. **[AoeEcsComponents.cs](../../Assets/Scripts/System/Aoe/AoeEcsComponents.cs)** — ensure the new
   `LingeringAoeTag` doc-comment (from 001) states it is *the* impact-vs-lingering discriminator.

2. **[CombatEcsComponents.cs](../../Assets/Scripts/System/Common/CombatEcsComponents.cs)** — confirm
   the `CombatLifetimeComponent` comment (from 003) no longer claims presence is the AOE
   discriminator and no longer says "enableable".

3. **[Docs/reference/simulation/ecs-notes.md](../../Docs/reference/simulation/ecs-notes.md)** — if it
   documents the AOE archetypes/discriminator or the "disabled lifetime = pulse one-shot"
   convention, update it: impact vs lingering is now `LingeringAoeTag`; there is no pulse-one-shot
   enable-bit state; `CombatLifetimeComponent` is a plain timer. Add a one-line note that a future
   windup phase attaches via a dedicated `AoeWindupComponent`/`AoeWindupTag`, orthogonal to
   lifetime and the discriminator.

4. Grep the `Docs/` tree for "pulse one-shot", "disabled lifetime", and
   "CombatLifetimeComponent presence" and reconcile any stale references.

## Acceptance criteria
- No doc or comment still describes lifetime presence as the discriminator or references a
  pulse-one-shot enable-bit state.
- ecs-notes reflects the named discriminator and plain lifetime, with the windup breadcrumb.

## Scope / complexity
Trivial. Docs/comments only.

## Dependencies
Depends on 001-003 (documents their end state).
