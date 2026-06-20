occasionally, there will be a huge amount of aoe vfx stacked on top of each other playing around somewhere in the middle.
disabled vfxs and used sprites to check if this is a vf issue or some kind of bug in ecs.

the issue is either with the graph itself or the ecs system that feeds the graph.
when using sprites for aoes, the sprites are always rendered correctly.

i suspect it's the spawn count and the graphics buffer we are sending to the gpu that is causing this.
check if the size of the graphics buffer sent to gpu is always the same as spawn count

-----------------

bug is likely somewhere else. 
in an isolated tests with just projectile -> child spawn -> projectile -> stack trigger -> aoe, this never shows up.