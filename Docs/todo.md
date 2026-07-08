minor performance related refacor
- collision system writing into event queue instead of native stream, reader must free every element per read. (not sure if this is even problematic)


- collision system use aabb tree instead of spatial hash. aabb tree is more simd friendly.

new feature
- mob spawn rework
- lingering aoe spawning other skills, rework projectile interval spawn into generic interval spawn.
- fast projectiles, these cannot have tracking, requires tunneling check
