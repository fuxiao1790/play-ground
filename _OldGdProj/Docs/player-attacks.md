# Player Attacks

## Equipped Attack Loadout

Player projectile firing is now loadout-driven:

- [`scripts_cs/Player/Player.cs`](../scripts_cs/Player/Player.cs) owns player setup and delegates attack work to [`scripts_cs/Player/PlayerAttack.cs`](../scripts_cs/Player/PlayerAttack.cs)
- attack scene instances added as direct children of the player are the equipped attack list
- `max_attack_count` caps how many child attacks are configured
- holding `primary_attack` asks every equipped attack to perform in the same frame
- each attack keeps its own `recovery_seconds`, projectile scene, sound, damage, tracking, count, spread, and jitter
- projectile attacks can own child hit-effect components, such as stack-triggered
  explosions that add enum-indexed status stacks and spawn AOE on the threshold hit

This replaces the player-side "one attack pretending to be many offset muzzles" flow. To give the player multiple simultaneous firing points, add multiple attack scene instances under the player and set each child node's position in the player scene.

Attack scenes include a `MuzzleMarker` at their root origin. It is an editor visual guide for the projectile spawn point; gameplay still uses the attack node's own `GlobalPosition`.

## Attack Node Offset

[`scripts_cs/Attack/ProjectileAttack.cs`](../scripts_cs/Attack/ProjectileAttack.cs) fires from the attack node's `GlobalPosition`.

That means an equipped attack can be positioned under the player like a muzzle:

- attack A local position: `(-8, 0)`
- attack B local position: `(8, 0)`
- one held fire input performs both attacks
- each shot aims at the cursor from its own attack node position

The attack node position is the muzzle offset. There is no separate attack-local spawn-offset export.

## Projectile Volley Support

Each individual attack scene can still author volley shaping:

- `projectile_volley_spread_degrees`
- `projectile_jitter_degrees`
- `projectile_count`

Prefer multiple equipped attacks for separate weapon/muzzle behavior. Use one attack's volley fields for a single attack pattern such as a fan, burst, or jitter shot.

## Runtime Flow

[`scripts_cs/Attack/ProjectileVolleyBuilder.cs`](../scripts_cs/Attack/ProjectileVolleyBuilder.cs) is the shared math path for volley creation.

It builds one spawn request per projectile with:

- world spawn position
- velocity

Current volley-spread behavior:

- `projectile_count <= 1` fires one straight shot
- repeated shots distribute evenly across the attack's `projectile_volley_spread_degrees` cone
- `projectile_jitter_degrees` adds random per-shot angle variation after volley-spread placement
- all shots originate from the attack node's `GlobalPosition`

Example:

- `count 3`, `volley spread 30` produces `-15`, `0`, `+15`

## Player Integration

[`scripts_cs/Player/PlayerAttack.cs`](../scripts_cs/Player/PlayerAttack.cs):

- equips attack scenes as player children
- caps loadout size with `max_attack_count`
- updates each attack recovery timer every physics frame
- performs every ready attack while `primary_attack` is held

[`scripts_cs/Attack/ProjectileAttack.cs`](../scripts_cs/Attack/ProjectileAttack.cs):

- builds a volley through the shared builder
- calls `Root.SpawnProjectile(...)` once per built shot
- applies recovery once per attack perform
- plays perform sound once per attack perform
- applies jitter only to shot shaping, not attack recovery timing

## Content Examples

- basic shot:
  - `projectile_count = 1`
  - `projectile_volley_spread_degrees = 0`
- twin barrels:
  - equip two basic attack scenes
  - place one at `(-8, 0)` and one at `(8, 0)`
  - give each `projectile_count = 1`
- volley spread:
  - `projectile_count = 4`
  - `projectile_volley_spread_degrees = 20`
- jitter shot:
  - `projectile_count = 1`
  - `projectile_jitter_degrees = 25`

Reference content fixture:

- [`tests/scenes/attacks/volley_spread_projectile_attack.tscn`](../tests/scenes/attacks/volley_spread_projectile_attack.tscn)

Authored runtime attack:

- [`scenes/attacks/stacking_explosive_projectile_attack.tscn`](../scenes/attacks/stacking_explosive_projectile_attack.tscn):
  tracking projectile that adds `Volatile` status stacks to mobs and explodes
  through the AOE system every third hit

## Verification

Covered now by:

- [`tests/basic_projectile_attack_smoke.gd`](../tests/basic_projectile_attack_smoke.gd)
- [`tests/jitter_projectile_attack_smoke.gd`](../tests/jitter_projectile_attack_smoke.gd)
- [`tests/volley_spread_projectile_attack_smoke.gd`](../tests/volley_spread_projectile_attack_smoke.gd)
- [`tests/player_attack_loadout_smoke.gd`](../tests/player_attack_loadout_smoke.gd)
- [`tests/player_multi_attack_fire_smoke.gd`](../tests/player_multi_attack_fire_smoke.gd)
- [`tests/projectile_main_scene_player_to_mob_smoke.gd`](../tests/projectile_main_scene_player_to_mob_smoke.gd)
- [`tests/stacking_projectile_aoe_attack_smoke.gd`](../tests/stacking_projectile_aoe_attack_smoke.gd)
