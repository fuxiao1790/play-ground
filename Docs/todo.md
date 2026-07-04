delayed aoe with indicator.

spawning

since there is no sub archetype within an archetype any more,
respawn reuse now queries disabled entities in the corresponding archetype and runs one single-threaded Burst reuse job per pool.

projectile and aoe sharing component causing spawn jobs to be in sequantial order becuase unity canno prove they are fully disjoint.