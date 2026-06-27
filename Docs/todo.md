spawner spawns increasingly tankier mobs with a defined curve.

projectile spawn expansion system/job isn't multi threaded.
basic projetile spawn job is also not multi threaded

damage application
hit events => random entitiy
hit events => a random entity in a chunk

overhaul faction in the ecs.
faction should simply be a field in a component for projectiles, aoes, mobs, player.
faction should simple allow projectile and aoes to ignore targets if they have the same faction.
1 world and 1 root for both player and mobs, existing CombatEcsRoot_PlayerToMobs and CombatEcsRoot_MobToPlayer should be combined. 
collision and target aquisition should be the only systems that will require substantial changes to handle factions.
collision and target aquisition's spatial hash can be more ganular but focus on structural changes in this overhaul. this optmization can be done later.
spawn reuse should be able to reuse any entitiy with the same archetype regardless of faction.