delayed aoe with indicator.

rendering rework.

since there is no sub archetype within an archetype any more,
respawn reuse can be massively simplified to just query for disabled entities in the corresponding archetype and schedule parallel worker to reuse. 