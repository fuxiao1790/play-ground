minor performance related refacor
- projectile and aoe sharing component causing spawn jobs to be in sequantial order becuase unity cannot prove they are fully disjoint.
- lingering aoe and impact aoe are not separated cleanly, some combined some separate. eg. collision system, spawn expansion system, spawn apply system.
- collision system writing into event queue instead of native stream, reader must free every element per read. (not sure if this is even problematic)

new feature
- delayed aoe with indicator.
- mob spawn rework