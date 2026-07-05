minor performance related refacor
- projectile and aoe sharing component causing spawn jobs to be in sequantial order becuase unity cannot prove they are fully disjoint.
- done: lingering aoe and impact aoe now use separate event queues, expansion systems, command lists, apply systems, and collision lanes.
- collision system writing into event queue instead of native stream, reader must free every element per read. (not sure if this is even problematic)

new feature
- delayed aoe with indicator.
- mob spawn rework
