minor performance related refacor
- collision system writing into event queue instead of native stream, reader must free every element per read. (not sure if this is even problematic)

- collision system use aabb tree instead of spatial hash. aabb tree is more simd friendly.

new feature
- mob spawn rework
- cast on crit and other conditions.
- fast projectiles, these cannot have tracking, requires tunneling check
- unit related, these have dependency on each other 
    - unit stats
    - mob skills (mob should also use the same skill loadout for player, player skill loadout should be generic)
    - player summons (these should just be mobs but with different faction)
