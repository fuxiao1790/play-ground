# Mobs

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Summary

Mob behavior:

- spawn from authored spawn points
- idle for one tick, then wander
- detect player by radius
- chase player with selected behavior
- switch into hurt state when damaged
- recover back into chase or wander
- soft-die when health reaches zero
- free spawn cap immediately on death
- support at least one ranged mob with projectile attack later

Mobs are scene objects in the hybrid architecture. Their count should stay low
enough for Unity GameObjects, Rigidbody2D, Collider2D, Animator, and
SpriteRenderer to be appropriate. Player plus mobs below roughly `50` is an
early performance target, not a hard design cap. Target count is expected to be
far below projectile count.

Planned gameplay to add:

- melee/contact damage
- player damage/death feedback
- ranged mob attack content
- death animation before cleanup
- meaningful per-mob behavior tuning

For the earliest foundation work, any simple wandering mob can act as a target
dummy.

## Runtime Ownership

`MobRoot` owns:

- serialized prefab validation
- animation FSM setup
- behavior FSM setup
- local event queue
- trigger update order
- Rigidbody2D movement application
- optional projectile attack setup
- health and death transition
- cleanup scheduling after death
- local debug widget drawing if enabled

Mob body movement and wall/player collision use Unity Physics2D. Body hitboxes
and damage hurtboxes must remain separately configurable, matching the old
project's split. Mob hurtboxes also register with projectile/AOE/beam target
registries so high-count attack runtimes can snapshot them.

Focused helpers own decisions:

- `MobStateDriver`: behavior state transitions
- `MobBehaviourSelector`: behavior choice from trigger requests
- `MobBlackboard`: shared per-mob runtime state
- `MobEventQueue`: frame-local event intake
- `MobAnimatorDriver`: animation requests
- behavior ScriptableObjects
- trigger ScriptableObjects

## Scene Variants To Rebuild

### Slime

Target:

- speed `30`
- health `35`
- body/hurtbox shape: circle
- no projectile attack
- slow ground chaser

### Skeleton

Target:

- speed `45`
- health `30`
- body/hurtbox shape: capsule
- no projectile attack
- medium-speed ground chaser

### Bat

Target:

- speed `60`
- health `20`
- body shape: small capsule
- hurtbox shape: circle
- projectile attack enabled
- projectile cooldown `1.4`
- projectile range `130`
- projectile speed `220`
- projectile lifetime `1.8`
- projectile damage `1`
- fastest mob and first ranged mob

## Spawn Integration

The previous mob spawning implementation has been removed. The replacement
spawner should instantiate authored `MobRoot` prefabs and then use the existing
mob setup, target binding, and combat-root binding paths.

## Damage And Death

Damage entry should be:

- `TakeDamage(DamageSnapshot damage)` for full path

Damage should stay typed. Avoid adding an integer-only damage path that becomes a
second parallel combat model.

Per-mob status stacks should be generic named or typed slots so poison, burning,
shock, volatile explosions, and later effects can share one status-stack
storage model.

Soft death behavior:

- health reaches zero
- mob becomes not alive
- body and hurtbox colliders disable
- target registry unregisters mob
- visual hides or switches to death animation later
- update stops
- `SoftDied` event fires for spawner accounting
- object cleanup is scheduled after death, immediately by default or after the
  authored death cleanup delay

Projectile/AOE hit replay resolves the target id back to a live target before
each managed callback. If the target was already destroyed or became inactive
earlier in the same hit buffer, replay passes a null target context and skips
direct target mutation. Mob objects no longer need to be retained indefinitely
only to protect hit callbacks.

## Debugging

Per-mob debug widget may show:

- behavior state
- animation state
- active trigger key
- active behavior key
- target name and distance
- health

Shared scene counters belong in `DebugOverlay`, not mob root.
