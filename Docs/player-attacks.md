# Player Attacks

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Equipped Attack Loadout

Player attacks should stay loadout-driven.

The intended player fantasy is scaling spells and attacks to absurd density.
Attack authoring must support high counts without forcing every added projectile,
AOE, beam, or particle into a separate expensive GameObject path.

Attack components live on scene-object actors, but spawned attack gameplay lives
in data runtimes. The player is a GameObject; its projectiles, AOEs, and beams
are not authoritative GameObjects.

Target:

- `PlayerRoot` owns setup and delegates attack updates to `PlayerAttackLoadout`
- attack components under the player's `Attacks` child are equipped attacks
- each `attack_container_*` child is a configured attack slot; its Transform
  position is the firing offset
- `maxAttackCount` caps how many child attacks are active
- holding primary fire asks every ready equipped attack to perform
- basic attack prefabs own sprite and projectile hurtbox authoring and can be
  assigned by dragging the prefab into an attack container slot; valid basic
  attack prefabs need a `Visual` child `SpriteRenderer` and a `Hurtbox` child
  `CircleCollider2D`, `BoxCollider2D`, or `CapsuleCollider2D`
- container attack prefabs reference one primary basic attack prefab, can
  reference a separate child basic attack prefab, and own recovery, damage,
  sound, tracking, count, spread, jitter, pierce, and payload settings

Multiple firing points should be modeled as multiple child attack prefabs at
different local positions. `maxAttackCount` is both a balance cap and a
performance safety cap, but balance is the primary reason.

Attack damage and secondary payloads are snapshotted when the attack fires.
This includes crit results, child projectile/AOE spawn definitions, and effect
damage. Active projectiles and AOEs should not recalculate from live player stat
objects.

If many equipped attacks fire on the same held-input frame, audio timing can be
offset later to avoid harsh stacked sounds without changing the attack loadout
model.

## Attack Offset

An attack fires from its own Transform position.

Example:

- left muzzle attack local position: `(-8, 0)`
- right muzzle attack local position: `(8, 0)`
- one held fire performs both
- each shot aims at cursor from its own muzzle position

Do not add a second hidden spawn-offset field unless a future weapon needs it.

## Projectile Volley

Each projectile attack can author:

- `projectileCount`
- `projectileVolleySpreadDegrees`
- `projectileJitterDegrees`

Rules:

- count `1` fires straight
- count greater than `1` distributes shots evenly across spread
- jitter applies after spread placement
- all shots originate from attack Transform position

Example:

- count `3`, spread `30` gives `-15`, `0`, `+15`

## Attack Types

`ProjectileAttack`:

- registers projectile template with scoped `ProjectileRoot`
- builds volley spawn commands
- snapshots damage before spawn
- applies recovery once per perform
- plays sound once per perform
- can spawn impact AOE on hit
- can host child hit effects such as stack-triggered explosions

`AoeAttack`:

- registers AOE effect template with scoped `AoeRoot`
- spawns pulse or lingering AOEs
- can spawn at attack position, owner position, or aim position
- can distribute multiple AOEs by ring or random burst radius

Future `BeamAttack`:

- future work, not part of the first projectile/AOE foundation
- registers beam definition with scoped beam root
- supports continuous laser, burst beam, sweep, or chain beam behavior
- owns tick interval, width, range, damage snapshot, visual style, and target mask
- uses beam-specific collision instead of spawning projectile strips

## Scaling Rules

Attack scaling should compose through data:

- count
- spread
- chain count
- pierce count
- fork count
- AOE count
- AOE radius
- beam count
- beam width
- duration
- tick rate
- effect priority

High-scale modifiers should add spawn commands or runtime definitions, not
Instantiate calls in a loop without pooling or batching.

## Content Examples

Basic shot:

- `projectileCount = 1`
- `projectileVolleySpreadDegrees = 0`

Twin barrels:

- equip two basic projectile attacks
- place one at `(-8, 0)`
- place one at `(8, 0)`

Volley spread:

- `projectileCount = 4`
- `projectileVolleySpreadDegrees = 20`

Jitter shot:

- `projectileCount = 1`
- `projectileJitterDegrees = 25`

Stacking explosive shot:

- projectile hit adds status stacks to mob runtime state
- threshold clears stack slot
- threshold spawns AOE at mob position through player AOE root

Laser build:

- equip one or more beam attacks
- each beam uses width/range/tick interval for gameplay
- visual uses line, mesh, or batched beam renderer
- particles are capped visual garnish, not the damage source
