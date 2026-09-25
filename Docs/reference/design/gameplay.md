# Gameplay Direction

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Core Loop

Target loop:

- move, dash, aim, and fire from the player
- spawn mobs from authored spawn points
- mobs wander, acquire player, chase, react to damage, attack, and die
- player attacks include projectiles, AOEs, impact explosions, and hit-energy activations
- combat pressure grows through richer mob behavior, attack modifiers, spawn tuning, and damage patterns

## Core Design

- fast top-down movement
- mouse-biased camera
- hold-fire attack loadout
- scoped projectile roots
- scoped AOE roots
- event-driven mob behavior separate from animation
- soft-death path for mobs until death animation exists
- data-first hit detection for high-volume projectile and AOE flows

## Open Design Threads

- combat into rest, reward, or upgrade loop
- difficulty scaling by time, wave, biome, or player power
- gear acquisition through drops, crafting, vendors, auctions, or enchants
- base-building systems that affect risk, reward, or spawn RNG
- shipment-container auction as possible loot-delivery fantasy

These are parking-lot topics until the movement, attack, projectile, AOE,
target, and performance foundation is proven.

## Current Prototype Focus

- no committed first playable win/loss goal yet
- simple wandering target dummies are enough for early combat testing
- player health and death can wait
- camera should focus local combat, with minimap support planned later
- dash behavior should stay configurable enough to support fixed distance,
  fixed time impulse, speed burst, and optional defensive windows
- facing rules should adapt to the chosen sprite set, including 4-direction or
  8-direction animation
