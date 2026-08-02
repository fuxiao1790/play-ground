minor performance related refacor
- collision system writing into event queue instead of native stream, reader must free every element per read. (not sure if this is even problematic)

- collision system use aabb tree instead of spatial hash. aabb tree is more simd friendly.

minor fixes
- multiple aoe should have 1 aoe aimed at where the cursor is.
- multiple projectile should also have 1 projectile aimed at where the cursor is.'

changes to existing gameplay
- more interaction between skills instead of just a single skill that does everything. damage currently is the only axis, is there something else that can be added.

enhancements/addition to existing
- interval spawn direction (only side spray is supported currently)
- on crit trigger and others.

needed for poc
- rename projectiles to missiles.
- targeted skills. no physics, single hit / interval ticking.
- damage number vfx graph.
- more aoe shape kinds
- better mob ai.
