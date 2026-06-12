# Move Actor Health Into ECS

**Summary**
- Move player and mob direct health damage into one ECS path.
- Use one ECS health entity per combat target id.
- Tag health entities with `CombatTargetHealth`.
- Keep GameObjects responsible for movement, colliders, animation, input, and behavior. Mob death is a hard destroy; player retains death presentation.
- MonoBehaviours do not touch ECS directly.

**Key Data**
- Add `CombatTargetHealth : IComponentData` as the health-target marker.
- Add `CombatHealthComponent` with `TargetId`, `CurrentHealth`, `MaxHealth`, and `IsAlive`.
- Add one health event singleton with buffers for direct damage events and compact health sync records.
- `CombatDamageEventElement` stores `TargetId`, rolled damage amount, crit flag, hit kind, and event order.
- `CombatHealthSyncElement` stores one presentation record per changed target.

**Damage Event Flow**
- Projectile/AOE collision jobs keep detecting hits from target snapshots.
- Hit flush jobs append `CombatDamageEventElement` when direct damage is enabled and positive.
- Direct damage is no longer applied by `target.ReceiveHit`.
- Semantic replay remains separate for impact AOE, impact projectile, AOE projectile burst, and VFX.
- Status effects are out of scope and may be removed or left temporarily broken.

**Damage Aggregation Decision**
- Aggregating damage by target id is the central implementation concern and is not decided yet.
- Option A: chunk-bucket aggregation.
  - Build `TargetId -> health entity/chunk` lookup for all `CombatTargetHealth` entities.
  - Route damage events into buckets where each bucket only contains events for health entities in one ECS chunk.
  - Apply each bucket by streaming the chunk’s `CombatHealthComponent` array and reducing target events locally.
  - Better chunk locality and fewer random component writes, but more implementation complexity.
- Option B: entity-id aggregation.
  - Build `TargetId -> health entity` lookup.
  - Aggregate events directly by health entity or target id.
  - Apply one summed damage value per entity.
  - Simpler first implementation, but can become too granular and less cache-friendly.
- Plan default: keep the aggregation layer behind a small internal interface/data-flow boundary so either strategy can be implemented without changing collision event creation, health components, or presentation sync.

**Health Apply Flow**
- `CombatHealthFrameBeginSystem` clears health event and sync buffers once per frame.
- Registration system creates/destroys one `CombatTargetHealth + CombatHealthComponent` entity per registered player or mob.
- `CombatDamageApplySystem` runs after projectile/AOE hit flush:
  - consumes direct damage events;
  - uses the chosen aggregation strategy;
  - applies summed damage to each health entity once per frame;
  - clamps health to zero;
  - flips `IsAlive` when dead;
  - emits one sync record per changed target.
- Missing health entity means the target is not registered for ECS health, so damage is skipped.

**Actor Registration And Presentation**
- Add a non-ECS actor health registry owned by `GameRoot`.
- `PlayerRoot` and `MobRoot` register target id, max health, and presentation sink through scene setup/spawn setup.
- Managed ECS systems read registry changes and call registered sinks by target id during presentation sync.
- `MobRoot` sync updates mirrored health, blackboard, and hurt/crit events; on death the GameObject is immediately destroyed (hard death, no death animation).
- `PlayerRoot`/`PlayerHealth` sync updates mirrored health, hurt animation, and player death presentation.
- No `Entity`, `World`, or `EntityManager` is stored on actor MonoBehaviours.

**Tests**
- Player and mob take projectile damage through the same ECS health path.
- Player and mob take AOE damage through the same ECS health path.
- Many hits on one target in one frame produce one health sync record.
- Aggregation tests cover both target-id correctness and no duplicate per-frame health application.
- `directDamageEnabled = false` creates no damage event but keeps semantic hit event.
- Mob death destroys the GameObject (hard death); player death presentation still works.

**Assumptions**
- `TargetId` is unique across player and mobs.
- `CombatTargetHealth` marks health entities only; projectile and AOE entities remain separated by their existing tags and scopes.
- Final aggregation strategy remains undecided between chunk-bucket and entity-id grouping.
