recommend a way to solve the following in unity.

collision system produce massive amount of hit events.

hit events >> target count. eg, 100k hit events for 500 targets.

hit events should stay atomic. 
ideally, i don't want to have to make hit events merged. 
gameplay would feel inconsistent to players if there is something like gain health on hit.
it is effectively gain health per tick if you got hit in the tick.

hit events need to be applied to scene nodes since targets currently live on the scene tree.

crossing the ecs/game object bridge and/or applying massive amount of hits on main thread single threadedly is already way too expensive.

moving target into the ecs is possible but has it's own set of problems.

targets are going to be simple entities with 1 health component.

unity chunks by data size, each chunk can fit 4096 floats, effectively making hit application single threaded if we try to split work by chunk.

unity also doesn't allow parallel writes to the same chunk even if workers do not have any overlapping write target.



---
create a plan to make an optimization to hit application. 
hits generated from collision systems should be aggregated instead of completely random. 
the aggregation should be per worker map of target id to hit events.
there should be a system at the end of both collision systems to join them together.