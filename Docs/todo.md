minor performance related refacor
- collision system writing into event queue instead of native stream, reader must free every element per read. (not sure if this is even problematic)

- collision system use aabb tree instead of spatial hash. aabb tree is more simd friendly.

minor fixes
- multiple aoe should have 1 aoe aimed at where the cursor is.
- multiple projectile should also have 1 projectile aimed at where the cursor is.

changes to existing gaemplay 
- change travel spawn to an energy based system instead of an interval based system.
  spawner gains energy over a duration and simply spawns when threshold is reached.
  different skills will simply need different amount of energy to spawn.

- skills should have a mana cost which should be converted into energy cost with some function when it's triggered.
  things like additional projectiles, piercing, homing can modify the mana cost which will modify the energy cost.
  this should make things much easier to balance.

new feature
- ui  improvements
- interval spawn direction (only side spray is supported currently)
- damage number vfx graph
- cast on crit and other conditions.
- fast projectiles, these cannot have tracking, requires tunneling check
- targeted skills. no physics, just hits at an interval.
- more interaction between skills instead of just a single skill that does everything. damage currently is the only axis, is there something else that can be added.
- unit related, these have dependency on each other 
    - player summons (these should just be mobs but with different faction)
