minor performance related refacor
- collision system writing into event queue instead of native stream, reader must free every element per read. (not sure if this is even problematic)

- collision system use aabb tree instead of spatial hash. aabb tree is more simd friendly.

- clean up job can fire during busy scene. 

new feature
- ui
- damage number vfx graph
- cast on crit and other conditions.
- fast projectiles, these cannot have tracking, requires tunneling check
- unit related, these have dependency on each other 
    - player summons (these should just be mobs but with different faction)
