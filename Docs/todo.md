minor performance related refacor
- collision system writing into event queue instead of native stream, reader must free every element per read. (not sure if this is even problematic)

- collision system use aabb tree instead of spatial hash. aabb tree is more simd friendly.

minor fixes
- multiple aoe should have 1 aoe aimed at where the cursor is.
- multiple projectile should also have 1 projectile aimed at where the cursor is.

new feature
- ui  improvements
- interval spawn direction (only side spray is supported currently.)
- damage number vfx graph
- cast on crit and other conditions.
- fast projectiles, these cannot have tracking, requires tunneling check
- targeted skills. no physics, just hits at an interval.
- skills react to other skills through debuffs
- unit related, these have dependency on each other 
    - player summons (these should just be mobs but with different faction)
