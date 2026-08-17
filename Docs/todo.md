refactor debt
- spawn event structs are field-for-field identical across lanes (`ProjectileSpawnEvent`,
  `ImpactAoeSpawnEvent`, `LingeringAoeSpawnEvent`, and `TargetedSpawnEvent` = four copies),
  each with its own singleton lane and expansion system. Collapse to one
  `CombatSpawnEvent` discriminated by the `IntervalChildKind` the struct already carries.
  Deferred deliberately during targeted skills; see `.agent/targeted-skills/requirements.md` §9.

minor performance related refacor. (highly unlikely to have any actual performance benefit)
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
- damage number vfx graph.
- more aoe shape kinds
- better mob ai.
