# Phase Order

This project has meaningful runtime phases split across Unity actor callbacks,
ECS simulation groups, and ECS presentation systems.

## Inferred Frame Order

1. Scene roots and actor roots validate setup and bind services.
2. Input and low-count actor code update on GameObjects.
3. Player and mob roots push target proxy position and shape in `Update()`.
4. ECS simulation expires lifetime and emits timed child spawn events.
5. Projectile tracking, movement, contact gates, and collision run.
6. AOE pulse VFX and collision run.
7. Targeted single-hit and lingering resolve systems walk target proxies and emit hits.
8. Combat apply/finalize consumes hit events, updates ECS health/status, and
   freezes presentation-ready results.
9. Status processing may emit stack detonation projectile, AOE, or targeted spawn events.
10. Projectile, AOE, and targeted expansion systems drain managed scope buffers and ECS
   event queues into commands.
11. Projectile, AOE, and targeted apply systems reuse disabled slots or cold-create
   overflow entities.
12. Render preparation writes batched sprite matrices.
13. Actor roots delete queued dead target proxies in `LateUpdate()`.
14. Presentation systems dispatch compact combat results, VFX requests, and
    render batches.

TODO: verify exact ordering between actor `LateUpdate()` proxy deletion and all
`PresentationSystemGroup` systems in the current Unity player loop.

## Phase Ownership

Actor `Update()` may read Unity input, Transforms, Rigidbody2D state, authored
runtime objects, and target proxy handles. It may write actor state, movement
intent, and target proxy position/shape.

ECS simulation may read ECS component data, buffers, native containers, and
unmanaged target proxy data. It may write projectile, AOE, targeted, status, hit, spawn,
render-prep, and VFX request data.

ECS simulation may not read managed target companions or live Unity objects.

Presentation may resolve managed companions, dispatch VFX, submit render
batches, update actor feedback, and clear presentation buffers.

## Spawn Timing Rule

Apply systems run after movement and collision. Newly spawned or reused
projectiles, AOEs, and targeted chains do not move, collide, resolve, or emit timed spawns until the next
simulation update.

## Structural Change Timing

Hot despawn disables enableable state. Structural creation happens in apply
fallbacks and teardown paths. Target proxy deletion is actor-owned and delayed
until after proxy data is no longer needed for the current frame.
