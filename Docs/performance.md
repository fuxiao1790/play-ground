# Performance

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Goal

The game should support extreme player scaling: dense projectile storms, lasers,
AOE fields, chained explosions, and heavy visual effects.

Performance is a design requirement for combat systems.

## Core Rules

- no unbounded `Instantiate`/`Destroy` during combat
- no authoritative damage based on thousands of live trigger callbacks
- no scene searches on combat hot paths
- no per-entity allocations in projectile, AOE, beam, or mob update loops
- bake prefab-derived shape and render data once
- use target registries and snapshots
- separate gameplay simulation from visuals
- cap or degrade low-priority visuals before gameplay correctness suffers

## Runtime Strategy

Hybrid boundary:

- scene objects for player, mobs, walls, camera, spawners, audio, and debug
- expected target count is far below projectile count; player plus mobs below
  roughly `50` is an early performance target
- Unity Physics2D handles body movement and collision for player, mobs, and walls
- data runtimes handle projectiles, AOEs, beams, lasers, and high-count hit effects
- projectiles use Entities/DOTS for authoritative projectile state; do not create
  one GameObject/scene node per projectile

Simulation:

- plain data with Unity Jobs/Burst-friendly layout
- arrays or reusable lists for hot state
- spatial partitioning for broad-phase target queries
- callbacks replayed after simulation
- Jobs/Burst for high-volume projectile work
- projectile and AOE despawn stays disable-in-place on the hot path; disabled
  pools are reclaimed later by a bounded end-of-simulation cleanup system only
  when frame-time headroom exists

Visuals:

- batched sprites for the projectile proof of concept
- pooled prefabs for low/medium AOE or debug visuals
- batched meshes, rings, or line render data for other high-count effects later
- particle systems treated as capped visual effects
- no gameplay damage owned by particles

Audio:

- pooled AudioSources
- same-clip duplicate culling
- priority or distance culling for impact spam

## Required Counters

Expose debug counters for:

- active projectiles
- projectile spawns per second
- projectile despawns per second
- projectile simulation time
- projectile render time
- active AOEs
- AOE hit events per second
- AOE simulation time
- active beams
- beam tick/query time
- active mobs
- mob AI time
- active pooled visuals
- active particle systems
- audio requests culled
- managed allocations per frame where available

## Stress Scenes

Create stress scenes before content becomes complex:

- projectile count ramp
- projectile tracking ramp
- projectile pierce/contact gate ramp
- AOE overlap field
- lingering AOE tick field
- beam count and beam width ramp
- mob count with target sensing
- mixed combat: projectiles, AOEs, beams, particles, and mobs together

Stress scenes should expose counters first. Clear pass/fail thresholds can wait
until a Unity baseline exists.

## Design Implications

Projectiles:

- state and simulation are ECS/DOTS
- high-count path needs batched rendering by projectile type
- gameplay collision should use baked shapes and target proxy data
- simulation stages stay split by responsibility: target tracking, movement,
  child projectile creation, lifetime expiry disable, contact gates, and collision
- `CombatRoot` exposes active count, spawn/despawn totals, hit event count,
  simulation milliseconds, and render milliseconds
- performance target: about 50k projectiles on screen with 20 targets at 120 fps
- retained disabled projectile pools should drain gradually after spikes without
  deleting below the configured retention or active-ratio floor

AOEs:

- many AOEs need spatial hash target queries
- visuals must be optional, pooled, or batched
- retained disabled AOE pools follow the same bounded cleanup rule as
  projectiles, split by impact and lingering reuse pools

Beams and lasers:

- use beam-specific runtime
- do not model lasers as thousands of projectiles
- tick damage at controlled intervals

Particles:

- visual-only by default
- cap by priority, distance, or effect owner
