# Mobs

## Summary

Current mob implementation is real gameplay code, not just scaffold.

Mobs can:

- spawn from scene-authored spawn points
- idle for one tick, then wander
- detect player by radius
- chase player with behaviour-selected movement
- switch into hurt state when damaged
- recover back into chase or wander
- soft-die when health reaches zero
- free spawn cap immediately on soft death

One mob type also has a working ranged attack:

- `Bat` fires projectiles through `MobProjectileManager`
- bat projectile tracking is supported

Missing still:

- melee attacks
- player damage/death feedback tied to normal mob contact
- death animation path before soft death hide/disable flow
- meaningful per-mob behaviour tuning between slime and skeleton

## Runtime Ownership

Mob runtime root is [`scripts_cs/Mob/Mob.cs`](../scripts_cs/Mob/Mob.cs).

Root owns:

- scene export validation
- animation FSM setup
- behaviour FSM setup
- local event queue
- trigger update order
- movement application through rigid-body `LinearVelocity`
- optional projectile attack setup
- soft-death handling
- local debug widget drawing

Focused helpers own mob decisions:

- [`MobStateDriver`](../scripts_cs/Mob/MobStateDriver.cs): consumes local events and transitions behaviour state
- [`MobBehaviourSelector`](../scripts_cs/Mob/MobBehaviourSelector.cs): picks active behaviour from trigger requests
- [`MobBlackboard`](../scripts_cs/Mob/MobBlackboard.cs): per-mob shared runtime state like target, active trigger, health, and selected behaviour key
- [`MobEventQueue`](../scripts_cs/Mob/MobEventQueue.cs): frame-local event intake
- [`MobAnimator`](../scripts_cs/Mob/MobAnimator.cs): animation requests for `IDLE`, `WALK`, and `HURT`
- behaviour resources under [`scripts_cs/Mob/Behaviours/`](../scripts_cs/Mob/Behaviours/)
- trigger resources under [`scripts_cs/Mob/Triggers/`](../scripts_cs/Mob/Triggers/)

This follows project root-script rule from [`coding-standards.md`](./coding-standards.md): root coordinates, helpers decide.

## Behaviour State Flow

Current behaviour states in [`MobBehaviourState`](../scripts_cs/Mob/MobBehaviourState.cs):

- `Idle`
- `Wander`
- `Chase`
- `Hurt`
- `Dead`

Current transition rules from [`MobStateDriver`](../scripts_cs/Mob/MobStateDriver.cs):

- spawn starts in `Idle`
- `Tick` moves `Idle -> Wander`
- `TargetSeen` moves `Idle/Wander -> Chase`
- `TargetLost` moves `Chase -> Wander`
- `Damaged` moves any live state into `Hurt` and requests `on_hit`
- `Recovered` moves `Hurt -> Chase` if target still visible, else `Hurt -> Wander`
- `Died` moves any state into `Dead`

Animation mapping from [`Mob.cs`](../scripts_cs/Mob/Mob.cs):

- `Idle` -> idle animation
- `Wander` and `Chase` -> walk animation
- `Hurt` -> hurt animation
- `Dead` -> `SoftDie()`

## Trigger And Behaviour Composition

Each mob scene exports:

- `behaviours`
- `triggers`
- `trigger_behaviour_map`

Mob setup fails fast if:

- behaviour list is empty
- trigger list is empty
- trigger map is empty
- `on_spawn` mapping is missing
- a trigger maps to a missing behaviour key

Current shared behaviour resources:

- [`WanderBehaviour`](../scripts_cs/Mob/Behaviours/WanderBehaviour.cs): random roaming, default `speed_scale = 0.65`
- [`SwarmTargetBehaviour`](../scripts_cs/Mob/Behaviours/SwarmTargetBehaviour.cs): direct chase at full mob speed
- [`KeepDistanceBehaviour`](../scripts_cs/Mob/Behaviours/KeepDistanceBehaviour.cs): backs off when target gets too close, default preferred distance `72`
- [`BobAndWeaveBehaviour`](../scripts_cs/Mob/Behaviours/BobAndWeaveBehaviour.cs): chase with lateral weave
- [`PanicBehaviour`](../scripts_cs/Mob/Behaviours/PanicBehaviour.cs): flee/jitter movement at `1.2x` speed

Current shared triggers:

- [`TargetSensorTrigger`](../scripts_cs/Mob/Triggers/TargetSensorTrigger.cs): detect player at `140`, lose at `180`
- [`DistanceBehaviourTrigger`](../scripts_cs/Mob/Triggers/DistanceBehaviourTrigger.cs): request `on_close_to_target` under `52`
- [`BobAndWeaveTrigger`](../scripts_cs/Mob/Triggers/BobAndWeaveTrigger.cs): request `on_weave_range` between `64` and `130`
- [`PanicTrigger`](../scripts_cs/Mob/Triggers/PanicTrigger.cs): request `on_low_hp` at `<= 35%` health
- [`HurtRecoveryTrigger`](../scripts_cs/Mob/Triggers/HurtRecoveryTrigger.cs): emit `Recovered` after `0.35s` in hurt state

All current mob scenes use same trigger map:

- `on_spawn` -> `wander`
- `on_target_seen` -> `swarm_target`
- `on_close_to_target` -> `keep_distance`
- `on_weave_range` -> `bob_and_weave`
- `on_hit` -> `swarm_target`
- `on_low_hp` -> `panic`

So current mob variety mostly comes from stats and optional projectile attack, not from different AI composition.

## Scene Variants

### Slime

Scene: [`scenes/mobs/slime.tscn`](../scenes/mobs/slime.tscn)

- speed `30`
- health `35`
- body/hurtbox shape: circle
- no projectile attack
- debug widget enabled

Current role: slow ground chaser with wander, keep-distance, weave, hurt, and panic responses.

### Skeleton

Scene: [`scenes/mobs/skeleton.tscn`](../scenes/mobs/skeleton.tscn)

- speed `45`
- health `30`
- body/hurtbox shape: capsule
- no projectile attack
- debug widget enabled

Current role: medium-speed ground chaser. Behaviour composition is same as slime.

### Bat

Scene: [`scenes/mobs/bat.tscn`](../scenes/mobs/bat.tscn)

- speed `60`
- health `20`
- body shape: small capsule
- hurtbox shape: circle
- projectile attack enabled
- projectile scene: `res://scenes/projectiles/bat_projectile.tscn`
- projectile cooldown `1.4`
- default projectile range `130`
- default projectile speed `220`
- default projectile lifetime `1.8`
- default projectile damage `1`
- debug widget enabled

Current role: fastest mob and only ranged mob.

Projectile attack is implemented by [`scripts_cs/Mob/MobProjectileAttack.cs`](../scripts_cs/Mob/MobProjectileAttack.cs). It:

- resolves `../MobProjectileManager`
- registers projectile type once
- checks cooldown and target validity
- fires toward current target if in range
- uses projectile hit callback to apply damage to valid target

Tracking exports exist on `Mob` and are used by smoke coverage.

## Spawning Integration

Wall scene [`scenes/level/play_area_wall.tscn`](../scenes/level/play_area_wall.tscn) currently has:

- one `MobSpawnner` root
- seven spawn points that spawn bat/slime mixes
- one spawn point that spawns skeleton/slime mix

Spawner flow:

- [`SpawnPoint`](../scripts_cs/Spawn/SpawnPoint.cs) runs timer-based spawn requests
- [`MobSpawnerRoot`](../scripts_cs/Spawn/MobSpawnerRoot.cs) enforces `max_mobs`
- spawned mob instances are added to current scene
- root tracks soft-dead mobs separately from tree-exited mobs
- soft death frees spawn cap before node leaves tree

## Damage And Death

Damage entry point is `take_damage(int amount = 1)` on [`Mob`](../scripts_cs/Mob/Mob.cs).

Current death path is soft death, not immediate `QueueFree()`:

- health drops to `0`
- mob becomes not alive
- body and hurtbox collision are disabled
- projectile target and actor groups are removed
- node is hidden
- `_PhysicsProcess()` and `_Process()` stop
- `SoftDied` event fires for spawner accounting

This means dead mobs stay as valid scene nodes for a while, which current projectile and spawn accounting tests rely on.

## Debugging

When `debug_draw = true`, each mob draws local widget text above self with:

- behaviour state and animation state
- active trigger key
- active behaviour key
- target name and distance
- health bar

This is mob-local debug UI, not shared top-left debug text.

## Test Coverage

Current mob-related smoke coverage:

- [`tests/soft_death_smoke.gd`](../tests/soft_death_smoke.gd): verifies soft death keeps node valid, disables targeting, and frees spawn cap
- [`tests/mob_tracking_attack_smoke.gd`](../tests/mob_tracking_attack_smoke.gd): verifies bat projectile attack spawns through `MobProjectileManager` and tracking bends toward moved player
- [`tests/projectile_main_scene_player_to_mob_smoke.gd`](../tests/projectile_main_scene_player_to_mob_smoke.gd): verifies player projectile can hit mob in main-scene setup

## Current Gaps

Important gaps in current mob implementation:

- no contact damage or melee attack subsystem
- no separate action/combat state machine yet; future mob runtime should keep AI intent state and action state independent
- no mob-specific authored tuning for trigger thresholds or behaviour resources in scene files
- no death animation or corpse cleanup flow after soft death
- no attack behaviour state in behaviour FSM
- target sensing is simple group lookup plus radius check; no line-of-sight or obstacle test
