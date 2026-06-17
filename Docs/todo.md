this is what i have gathered from reviewing current code base and roughly what i want to future implementation to look like

the design of the ecs relys on snapshotting everything at spawn time. /snapshotting.md
snapshotting everything does mean recursive spawn, and recursive spawn is limited to 2. 
for example, projectile -> time based child spawn -> projectile -> impact spawn -> aoe
the first projectile carries a time based child spawn component 
the component contains the projectile that will be spawned and a impact aoe component 

life time system is split into 2. 1 for projectile and 1 for aoe. they should combine into 1 since the logic should simply be looping over all entities that have lifetime component, tick time, if time <= 0 then set active = false using IEnableableComponent 

a set of systems should take care of time based spawns if an entitiy has the component for it.
each spawn system should simply loop though entities with the specific kind of TimedSpawnComponent, and produce spawn event.
since everything must be snapshotted at spawn time the component should contain all data fields required produce spawn event that contains raw data instead of referencing some kind of centralized storage.

projectile collision system can remain as it is. for the most part.
aoe collision system same as above
collision systems are expected to produce spawn events (aoe explosion on projectile hit, the projectile carries an aoe spawn component)

projectile contact gate system should also stay as it is.

projectile tracking should also stay as it is.

spawn system is intended to reuse dead slots to reduce the # of entities going through ecb and memory management.
it should read events produced from collision systems use entity query to query for archetypes with dead slots, spawn workers to loop through them and reuse dead slots.
the native container used by producers and consumers should be contention free since we guarantee writers are done before readers start.
slot distribution doesn't have to be perfect, a few remainder is nothing compared to having to full scan something just to have perfect distribution.

still considering how damage application should be done. will provide more detail.

